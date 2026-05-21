using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

public sealed class MultiplayerPrototype : MonoBehaviour
{
    private const string DefaultServerUrl = "http://127.0.0.1:8000";
    private const float StateSendInterval = 0.05f;
    private const string BuiltInFontName = "LegacyRuntime.ttf";
    private const int DefaultMaxPlayers = 4;
    private static Font cachedUiFont;
    private static Shader cachedObjectShader;

    private static readonly Color Ink = new Color(0.035f, 0.043f, 0.075f, 0.96f);
    private static readonly Color Panel = new Color(0.07f, 0.09f, 0.16f, 0.94f);
    private static readonly Color PanelSoft = new Color(0.11f, 0.14f, 0.24f, 0.9f);
    private static readonly Color Accent = new Color(0.96f, 0.61f, 0.17f);
    private static readonly Color AccentCool = new Color(0.16f, 0.74f, 1f);
    private static readonly Color Success = new Color(0.24f, 0.88f, 0.48f);
    private static readonly Color MutedText = new Color(0.68f, 0.73f, 0.84f);

    private CaveGameApiClient api;
    private CaveGameSocketClient lobbySocket;
    private CaveGameSocketClient gameSocket;

    private string authToken;
    private UserDto currentUser;
    private LobbyDto currentLobby;
    private LobbyMemberDto localMember;
    private bool gameStarted;
    private int stateSeq;
    private float nextStateSendTime;

    private Canvas canvas;
    private GameObject loginPanel;
    private GameObject findPanel;
    private GameObject lobbyPanel;
    private GameObject gameHudPanel;

    private InputField serverInput;
    private InputField usernameInput;
    private InputField passwordInput;
    private InputField joinCodeInput;
    private Text loginStatusText;
    private Text findStatusText;
    private Text lobbyTitleText;
    private Text lobbyCodeText;
    private Text lobbyHostText;
    private Text lobbyPlayersText;
    private Text lobbyStatusText;
    private Text gameStatusText;
    private Button readyButton;
    private Button startButton;
    private Button copyCodeButton;
    private Button leaveLobbyButton;
    private Image readyButtonImage;
    private Image startButtonImage;
    private readonly List<LobbySlotView> lobbySlotViews = new List<LobbySlotView>();

    private GameObject worldRoot;
    private LocalCubeController localCube;
    private readonly Dictionary<string, RemoteCubeController> remoteCubes = new Dictionary<string, RemoteCubeController>();
    private readonly Dictionary<string, int> playerSlotsById = new Dictionary<string, int>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateRuntimeBootstrap()
    {
        if (FindAnyObjectByType<MultiplayerPrototype>() != null)
        {
            return;
        }

        GameObject bootstrap = new GameObject("Multiplayer Prototype");
        bootstrap.AddComponent<MultiplayerPrototype>();
        DontDestroyOnLoad(bootstrap);
    }

    private void Awake()
    {
        Application.runInBackground = true;
        api = new CaveGameApiClient(DefaultServerUrl, () => authToken);
        BuildUi();
        ShowLogin("Enter a display name, then authenticate with the backend.");
    }

    private void Update()
    {
        lobbySocket?.Pump();
        gameSocket?.Pump();

        if (!gameStarted || gameSocket == null || !gameSocket.IsOpen || localCube == null)
        {
            return;
        }

        if (Time.unscaledTime >= nextStateSendTime)
        {
            nextStateSendTime = Time.unscaledTime + StateSendInterval;
            PlayerStateDto state = PlayerStateDto.FromTransform(localMember.playerId, ++stateSeq, localCube.transform, localCube.Velocity);
            gameSocket.SendJson(JsonUtility.ToJson(state));
        }
    }

    private void OnDestroy()
    {
        lobbySocket?.Close();
        gameSocket?.Close();
    }

    private void Login()
    {
        api = new CaveGameApiClient(serverInput.text, () => authToken);
        SetText(loginStatusText, "Authenticating...");

        StartCoroutine(api.CreateGuest(result =>
        {
            if (!result.IsSuccess)
            {
                SetText(loginStatusText, result.Error);
                return;
            }

            authToken = result.Value.token;
            currentUser = result.Value.user;
            string preferredName = string.IsNullOrWhiteSpace(usernameInput.text) ? currentUser.username : usernameInput.text.Trim();
            ShowFind($"Authenticated as {preferredName} ({currentUser.username}). Backend currently issued a guest token.");
        }));
    }

    private void CreateLobby()
    {
        SetText(findStatusText, "Creating lobby...");
        StartCoroutine(api.CreateLobby(4, result =>
        {
            if (!result.IsSuccess)
            {
                SetText(findStatusText, result.Error);
                return;
            }

            currentLobby = result.Value;
            localMember = FindMember(currentLobby, currentUser.id);
            CacheLobbyPlayerSlots();
            OpenLobbySocket();
            ShowLobby("Lobby created.");
        }));
    }

    private void JoinLobby()
    {
        string code = joinCodeInput.text;
        if (string.IsNullOrWhiteSpace(code))
        {
            SetText(findStatusText, "Enter a lobby code first.");
            return;
        }

        SetText(findStatusText, "Joining lobby...");
        StartCoroutine(api.JoinLobby(code, result =>
        {
            if (!result.IsSuccess)
            {
                SetText(findStatusText, result.Error);
                return;
            }

            currentLobby = result.Value.lobby;
            localMember = result.Value.member;
            CacheLobbyPlayerSlots();
            OpenLobbySocket();
            ShowLobby("Joined lobby.");
        }));
    }

    private void ToggleReady()
    {
        bool nextReady = localMember == null || !localMember.isReady;
        SetText(lobbyStatusText, nextReady ? "Marking ready..." : "Clearing ready...");

        StartCoroutine(api.SetReady(currentLobby.id, nextReady, result =>
        {
            if (!result.IsSuccess)
            {
                SetText(lobbyStatusText, result.Error);
                return;
            }

            ApplyLobbyEvent(result.Value);
            SetText(lobbyStatusText, nextReady ? "Ready." : "Not ready.");
        }));
    }

    private void StartLobby()
    {
        SetText(lobbyStatusText, "Starting lobby...");
        StartCoroutine(api.StartLobby(currentLobby.id, result =>
        {
            if (!result.IsSuccess)
            {
                SetText(lobbyStatusText, result.Error);
                return;
            }

            EnterGame(result.Value);
        }));
    }

    private void CopyLobbyCode()
    {
        if (currentLobby == null || string.IsNullOrWhiteSpace(currentLobby.code))
        {
            SetText(lobbyStatusText, "No lobby code to copy yet.");
            return;
        }

        GUIUtility.systemCopyBuffer = currentLobby.code;
        SetText(lobbyStatusText, $"Copied lobby code {currentLobby.code}.");
    }

    private void LeaveLobby()
    {
        lobbySocket?.Close();
        lobbySocket = null;
        currentLobby = null;
        localMember = null;
        playerSlotsById.Clear();
        ShowFind("Left lobby. Create a new room or jump into another code.");
    }

    private void OpenLobbySocket()
    {
        lobbySocket?.Close();
        lobbySocket = new CaveGameSocketClient();
        lobbySocket.Opened += () => SetText(lobbyStatusText, "Connected to lobby socket.");
        lobbySocket.ErrorReceived += error => SetText(lobbyStatusText, "Lobby socket error: " + error);
        lobbySocket.Closed += _ => SetText(lobbyStatusText, "Lobby socket closed.");
        lobbySocket.MessageReceived += HandleLobbySocketMessage;
        lobbySocket.Connect(api.BuildWebSocketUrl($"/ws/lobby/{currentLobby.id}/"));
    }

    private void HandleLobbySocketMessage(string json)
    {
        SocketTypeEnvelopeDto envelope = JsonUtility.FromJson<SocketTypeEnvelopeDto>(json);
        switch (envelope.type)
        {
            case "lobby_snapshot":
                ApplyLobbySnapshot(JsonUtility.FromJson<LobbySnapshotDto>(json));
                break;
            case "player_ready_changed":
                ApplyLobbyEvent(JsonUtility.FromJson<LobbyEventDto>(json));
                break;
            case "player_joined":
            case "player_left":
                StartCoroutine(RefreshLobby("Lobby membership changed."));
                break;
            case "game_started":
                EnterGame(JsonUtility.FromJson<GameStartedDto>(json));
                break;
        }
    }

    private IEnumerator RefreshLobby(string status)
    {
        yield return api.GetLobby(currentLobby.id, result =>
        {
            if (result.IsSuccess)
            {
                currentLobby = result.Value;
                localMember = FindMember(currentLobby, currentUser.id);
                CacheLobbyPlayerSlots();
                RefreshLobbyUi(status);
            }
            else
            {
                SetText(lobbyStatusText, result.Error);
            }
        });
    }

    private void ApplyLobbySnapshot(LobbySnapshotDto snapshot)
    {
        currentLobby = new LobbyDto
        {
            id = snapshot.lobbyId,
            code = snapshot.code,
            hostId = snapshot.hostId,
            isStarted = snapshot.isStarted,
            members = snapshot.players,
        };
        localMember = FindMember(currentLobby, currentUser.id);
        CacheLobbyPlayerSlots();
        RefreshLobbyUi("Lobby snapshot received.");
    }

    private void ApplyLobbyEvent(LobbyEventDto lobbyEvent)
    {
        if (currentLobby?.members == null)
        {
            return;
        }

        foreach (LobbyMemberDto member in currentLobby.members)
        {
            if (member.userId == lobbyEvent.userId)
            {
                member.isReady = lobbyEvent.isReady;
                if (localMember != null && localMember.userId == member.userId)
                {
                    localMember = member;
                }
                break;
            }
        }

        RefreshLobbyUi(null);
    }

    private void EnterGame(GameStartedDto start)
    {
        if (gameStarted)
        {
            return;
        }

        gameStarted = true;
        CacheGameStartedPlayerSlots(start);
        lobbySocket?.Close();
        HideAllPanels();
        gameHudPanel.SetActive(true);
        SetText(gameStatusText, $"Game started in lobby {start.lobbyId}. WASD to move, Space to jump.");

        BuildGameWorld();
        OpenGameSocket(start.lobbyId);
    }

    private void BuildGameWorld()
    {
        if (worldRoot != null)
        {
            Destroy(worldRoot);
        }

        remoteCubes.Clear();
        worldRoot = new GameObject("Multiplayer Runtime World");

        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Prototype Floor";
        floor.transform.SetParent(worldRoot.transform);
        floor.transform.position = new Vector3(0f, -0.55f, 0f);
        floor.transform.localScale = new Vector3(32f, 1f, 32f);
        SetRendererColor(floor, new Color(0.22f, 0.5f, 0.24f));

        if (FindAnyObjectByType<Light>() == null)
        {
            GameObject lightObject = new GameObject("Directional Light");
            lightObject.transform.SetParent(worldRoot.transform);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        Camera camera = Camera.main;
        if (camera == null)
        {
            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            camera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
        }

        camera.clearFlags = CameraClearFlags.Skybox;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 1000f;

        Vector3 spawn = SpawnForSlot(localMember != null ? localMember.slot : 0);
        GameObject local = GameObject.CreatePrimitive(PrimitiveType.Cube);
        local.name = "Local Player Cube";
        local.transform.SetParent(worldRoot.transform);
        local.transform.position = spawn;
        SetRendererColor(local, GetPlayerColor(localMember != null ? localMember.playerId : null));
        Rigidbody body = local.AddComponent<Rigidbody>();
        body.freezeRotation = true;
        localCube = local.AddComponent<LocalCubeController>();
        localCube.Setup(camera.transform);
    }

    private void OpenGameSocket(int lobbyId)
    {
        gameSocket?.Close();
        gameSocket = new CaveGameSocketClient();
        gameSocket.Opened += () => SetText(gameStatusText, "Connected to game socket. Sending transform state at 20 Hz.");
        gameSocket.ErrorReceived += error => SetText(gameStatusText, "Game socket error: " + error);
        gameSocket.Closed += _ => SetText(gameStatusText, "Game socket closed.");
        gameSocket.MessageReceived += HandleGameSocketMessage;
        gameSocket.Connect(api.BuildWebSocketUrl($"/ws/game/{lobbyId}/"));
    }

    private void HandleGameSocketMessage(string json)
    {
        SocketTypeEnvelopeDto envelope = JsonUtility.FromJson<SocketTypeEnvelopeDto>(json);
        switch (envelope.type)
        {
            case "room_snapshot":
                RoomSnapshotDto snapshot = JsonUtility.FromJson<RoomSnapshotDto>(json);
                if (snapshot.players == null)
                {
                    return;
                }

                foreach (PlayerStateDto player in snapshot.players)
                {
                    ApplyRemoteState(player);
                }
                break;
            case "player_state":
                ApplyRemoteState(JsonUtility.FromJson<PlayerStateDto>(json));
                break;
            case "player_left":
                LobbyEventDto left = JsonUtility.FromJson<LobbyEventDto>(json);
                RemoveRemotePlayer(left.playerId);
                break;
        }
    }

    private void ApplyRemoteState(PlayerStateDto state)
    {
        if (state == null || string.IsNullOrWhiteSpace(state.playerId) || (localMember != null && state.playerId == localMember.playerId))
        {
            return;
        }

        if (!remoteCubes.TryGetValue(state.playerId, out RemoteCubeController remote))
        {
            GameObject remoteObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            remoteObject.name = "Remote Player Cube " + state.playerId;
            remoteObject.transform.SetParent(worldRoot.transform);
            remoteObject.transform.position = MultiplayerJson.ArrayToVector(state.position);
            SetRendererColor(remoteObject, GetPlayerColor(state.playerId));
            remote = remoteObject.AddComponent<RemoteCubeController>();
            remoteCubes[state.playerId] = remote;
        }

        remote.ApplyState(state);
    }

    private void RemoveRemotePlayer(string playerId)
    {
        if (!remoteCubes.TryGetValue(playerId, out RemoteCubeController remote))
        {
            return;
        }

        Destroy(remote.gameObject);
        remoteCubes.Remove(playerId);
    }

    private void BuildUi()
    {
        EnsureEventSystem();

        GameObject canvasObject = new GameObject("Wallow Multiplayer UI");
        DontDestroyOnLoad(canvasObject);
        canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasObject.AddComponent<GraphicRaycaster>();

        loginPanel = CreatePanel("Login Panel");
        loginPanel.GetComponent<RectTransform>().sizeDelta = new Vector2(680f, 620f);
        AddKicker(loginPanel.transform, "WALLOW ONLINE");
        AddTitle(loginPanel.transform, "Enter The Cave");
        AddText(loginPanel.transform, "Spin up a guest token, then create or join a lobby from the same backend.", 18, MutedText, TextAnchor.MiddleLeft, 64f);
        serverInput = AddInput(loginPanel.transform, "Server URL", DefaultServerUrl, false);
        usernameInput = AddInput(loginPanel.transform, "Display Name", "wallow-runner", false);
        passwordInput = AddInput(loginPanel.transform, "Password (reserved)", "", true);
        AddButton(loginPanel.transform, "Connect To Wallow", Login, Accent);
        loginStatusText = AddText(loginPanel.transform, "", 16, MutedText, TextAnchor.MiddleLeft, 56f);

        findPanel = CreatePanel("Find Games Panel");
        findPanel.GetComponent<RectTransform>().sizeDelta = new Vector2(680f, 560f);
        AddKicker(findPanel.transform, "MULTIPLAYER");
        AddTitle(findPanel.transform, "Lobby Control");
        AddText(findPanel.transform, "Host a four-player cave run or enter a friend code to join their lobby.", 18, MutedText, TextAnchor.MiddleLeft, 64f);
        AddButton(findPanel.transform, "Create New Lobby", CreateLobby, Accent);
        joinCodeInput = AddInput(findPanel.transform, "Lobby Code", "", false);
        AddButton(findPanel.transform, "Join By Code", JoinLobby, AccentCool);
        findStatusText = AddText(findPanel.transform, "", 16, MutedText, TextAnchor.MiddleLeft, 56f);

        lobbyPanel = CreatePanel("Lobby Panel");
        AddKicker(lobbyPanel.transform, "WALLOW PARTY");
        lobbyTitleText = AddTitle(lobbyPanel.transform, "Lobby");
        lobbyCodeText = AddText(lobbyPanel.transform, "", 30, Accent, TextAnchor.MiddleLeft, 46f);
        lobbyHostText = AddText(lobbyPanel.transform, "", 16, MutedText, TextAnchor.MiddleLeft, 48f);
        lobbyPlayersText = AddText(lobbyPanel.transform, "", 16, MutedText, TextAnchor.MiddleLeft, 36f);

        GameObject slotGrid = new GameObject("Player Slot Grid");
        slotGrid.transform.SetParent(lobbyPanel.transform, false);
        VerticalLayoutGroup slotLayout = slotGrid.AddComponent<VerticalLayoutGroup>();
        slotLayout.spacing = 8f;
        slotLayout.childControlHeight = true;
        slotLayout.childForceExpandHeight = false;
        slotLayout.childControlWidth = true;
        slotLayout.childForceExpandWidth = true;
        slotGrid.AddComponent<LayoutElement>().preferredHeight = 264f;
        for (int slot = 0; slot < DefaultMaxPlayers; slot++)
        {
            lobbySlotViews.Add(CreateLobbySlot(slotGrid.transform, slot));
        }

        GameObject actionRow = AddRow(lobbyPanel.transform, "Lobby Actions", 52f);
        readyButton = AddButton(actionRow.transform, "Ready Up", ToggleReady, Success);
        readyButtonImage = readyButton.targetGraphic as Image;
        startButton = AddButton(actionRow.transform, "Start Run", StartLobby, Accent);
        startButtonImage = startButton.targetGraphic as Image;

        GameObject utilityRow = AddRow(lobbyPanel.transform, "Lobby Utility", 44f);
        copyCodeButton = AddButton(utilityRow.transform, "Copy Code", CopyLobbyCode, AccentCool);
        leaveLobbyButton = AddButton(utilityRow.transform, "Leave", LeaveLobby, new Color(0.82f, 0.23f, 0.25f));
        lobbyStatusText = AddText(lobbyPanel.transform, "", 16, MutedText, TextAnchor.MiddleLeft, 56f);

        gameHudPanel = CreatePanel("Game HUD");
        gameHudPanel.GetComponent<RectTransform>().anchorMin = new Vector2(0f, 1f);
        gameHudPanel.GetComponent<RectTransform>().anchorMax = new Vector2(0f, 1f);
        gameHudPanel.GetComponent<RectTransform>().pivot = new Vector2(0f, 1f);
        gameHudPanel.GetComponent<RectTransform>().anchoredPosition = new Vector2(20f, -20f);
        gameHudPanel.GetComponent<RectTransform>().sizeDelta = new Vector2(620f, 168f);
        AddKicker(gameHudPanel.transform, "LIVE RUN");
        gameStatusText = AddText(gameHudPanel.transform, "", 16, MutedText, TextAnchor.MiddleLeft, 78f);
    }

    private GameObject CreatePanel(string name)
    {
        GameObject panel = new GameObject(name);
        panel.transform.SetParent(canvas.transform, false);
        Image image = panel.AddComponent<Image>();
        image.color = Panel;
        Shadow shadow = panel.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.5f);
        shadow.effectDistance = new Vector2(0f, -8f);
        Outline outline = panel.AddComponent<Outline>();
        outline.effectColor = new Color(1f, 1f, 1f, 0.08f);
        outline.effectDistance = new Vector2(1f, 1f);
        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(680f, 760f);

        VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(34, 34, 30, 30);
        layout.spacing = 14f;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = false;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;

        return panel;
    }

    private GameObject AddRow(Transform parent, string name, float height)
    {
        GameObject row = new GameObject(name);
        row.transform.SetParent(parent, false);
        HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 12f;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        row.AddComponent<LayoutElement>().preferredHeight = height;
        return row;
    }

    private Text AddKicker(Transform parent, string value)
    {
        Text text = AddText(parent, value, 14, AccentCool, TextAnchor.MiddleLeft, 28f);
        text.fontStyle = FontStyle.Bold;
        return text;
    }

    private Text AddTitle(Transform parent, string value)
    {
        Text text = AddText(parent, value, 38, Color.white, TextAnchor.MiddleLeft, 58f);
        text.fontStyle = FontStyle.Bold;
        return text;
    }

    private Text AddText(Transform parent, string value, int size, Color color, TextAnchor alignment, float preferredHeight)
    {
        GameObject textObject = new GameObject("Text");
        textObject.transform.SetParent(parent, false);
        Text text = textObject.AddComponent<Text>();
        text.text = value;
        text.font = GetUiFont();
        text.fontSize = size;
        text.color = color;
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        LayoutElement layout = textObject.AddComponent<LayoutElement>();
        layout.preferredHeight = preferredHeight;
        return text;
    }

    private InputField AddInput(Transform parent, string placeholder, string initialValue, bool password)
    {
        GameObject root = new GameObject(placeholder);
        root.transform.SetParent(parent, false);
        Image image = root.AddComponent<Image>();
        image.color = new Color(0.92f, 0.94f, 1f, 0.96f);
        Outline outline = root.AddComponent<Outline>();
        outline.effectColor = new Color(1f, 1f, 1f, 0.16f);
        outline.effectDistance = new Vector2(1f, 1f);
        InputField input = root.AddComponent<InputField>();
        input.text = initialValue;
        input.contentType = password ? InputField.ContentType.Password : InputField.ContentType.Standard;
        root.AddComponent<LayoutElement>().preferredHeight = 50f;

        Text text = CreateInputText(root.transform, "Text", Color.black);
        Text placeholderText = CreateInputText(root.transform, "Placeholder", new Color(0.45f, 0.45f, 0.45f));
        placeholderText.text = placeholder;
        input.textComponent = text;
        input.placeholder = placeholderText;
        return input;
    }

    private Text CreateInputText(Transform parent, string name, Color color)
    {
        GameObject textObject = new GameObject(name);
        textObject.transform.SetParent(parent, false);
        Text text = textObject.AddComponent<Text>();
        text.font = GetUiFont();
        text.fontSize = 16;
        text.color = color;
        text.alignment = TextAnchor.MiddleLeft;
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(16f, 8f);
        rect.offsetMax = new Vector2(-16f, -8f);
        return text;
    }

    private Button AddButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick, Color color)
    {
        GameObject buttonObject = new GameObject(label);
        buttonObject.transform.SetParent(parent, false);
        Image image = buttonObject.AddComponent<Image>();
        image.color = color;
        Shadow shadow = buttonObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.28f);
        shadow.effectDistance = new Vector2(0f, -3f);
        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(onClick);
        buttonObject.AddComponent<LayoutElement>().preferredHeight = 48f;

        Text text = AddText(buttonObject.transform, label, 18, Color.white, TextAnchor.MiddleCenter, 48f);
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        RectTransform rect = text.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        Destroy(text.GetComponent<LayoutElement>());
        return button;
    }

    private LobbySlotView CreateLobbySlot(Transform parent, int slot)
    {
        GameObject card = new GameObject("Slot " + (slot + 1));
        card.transform.SetParent(parent, false);
        Image background = card.AddComponent<Image>();
        background.color = PanelSoft;
        Outline outline = card.AddComponent<Outline>();
        outline.effectColor = new Color(1f, 1f, 1f, 0.08f);
        outline.effectDistance = new Vector2(1f, 1f);
        HorizontalLayoutGroup row = card.AddComponent<HorizontalLayoutGroup>();
        row.padding = new RectOffset(0, 14, 0, 0);
        row.spacing = 12f;
        row.childControlHeight = true;
        row.childForceExpandHeight = true;
        row.childControlWidth = false;
        row.childForceExpandWidth = false;
        card.AddComponent<LayoutElement>().preferredHeight = 58f;

        GameObject accent = new GameObject("Accent");
        accent.transform.SetParent(card.transform, false);
        Image accentImage = accent.AddComponent<Image>();
        accentImage.color = GetPlayerColor("slot-" + slot);
        LayoutElement accentLayout = accent.AddComponent<LayoutElement>();
        accentLayout.preferredWidth = 8f;
        accentLayout.minWidth = 8f;

        GameObject content = new GameObject("Content");
        content.transform.SetParent(card.transform, false);
        VerticalLayoutGroup contentLayout = content.AddComponent<VerticalLayoutGroup>();
        contentLayout.padding = new RectOffset(0, 0, 7, 7);
        contentLayout.spacing = 0f;
        contentLayout.childControlHeight = true;
        contentLayout.childForceExpandHeight = false;
        content.AddComponent<LayoutElement>().flexibleWidth = 1f;

        Text nameText = AddText(content.transform, "Open Slot", 18, Color.white, TextAnchor.MiddleLeft, 26f);
        nameText.fontStyle = FontStyle.Bold;
        Text statusText = AddText(content.transform, "Waiting for player", 14, MutedText, TextAnchor.MiddleLeft, 22f);

        return new LobbySlotView(background, accentImage, nameText, statusText);
    }

    private void ApplyLobbySlot(LobbySlotView view, LobbyMemberDto member, int slot)
    {
        if (member == null)
        {
            view.Background.color = new Color(0.09f, 0.11f, 0.18f, 0.8f);
            view.Accent.color = new Color(0.28f, 0.31f, 0.42f);
            view.NameText.text = $"Slot {slot + 1} - Open";
            view.StatusText.text = "Invite a runner with the code above";
            view.StatusText.color = MutedText;
            return;
        }

        bool isLocal = currentUser != null && member.userId == currentUser.id;
        view.Background.color = isLocal ? new Color(0.15f, 0.18f, 0.3f, 0.96f) : PanelSoft;
        view.Accent.color = GetPlayerColor(member.playerId);
        view.NameText.text = $"{member.username}{(isLocal ? " (you)" : string.Empty)}";
        view.StatusText.text = member.isReady ? "Ready for the drop" : "Tuning gear";
        view.StatusText.color = member.isReady ? Success : MutedText;
    }

    private void ShowLogin(string status)
    {
        HideAllPanels();
        loginPanel.SetActive(true);
        SetText(loginStatusText, status);
    }

    private void ShowFind(string status)
    {
        HideAllPanels();
        findPanel.SetActive(true);
        SetText(findStatusText, status);
    }

    private void ShowLobby(string status)
    {
        HideAllPanels();
        lobbyPanel.SetActive(true);
        RefreshLobbyUi(status);
    }

    private void RefreshLobbyUi(string status)
    {
        if (currentLobby == null)
        {
            return;
        }

        bool isHost = currentUser != null && currentLobby.hostId == currentUser.id;
        int memberCount = CountMembers(currentLobby);
        int readyCount = CountReadyMembers(currentLobby);
        bool allReady = memberCount > 0 && readyCount == memberCount;

        SetText(lobbyTitleText, "Lobby " + currentLobby.id);
        SetText(lobbyCodeText, $"CODE {currentLobby.code}");
        SetText(lobbyHostText, isHost
            ? "You are the host. Launch unlocks when every joined player is ready."
            : "Waiting for the host to launch once the party is ready.");
        SetText(lobbyPlayersText, $"{readyCount}/{Mathf.Max(memberCount, 1)} ready - {memberCount}/{LobbyCapacity(currentLobby)} players in cave party");

        if (localMember != null)
        {
            bool localReady = localMember.isReady;
            SetButtonText(readyButton, localReady ? "Stand Down" : "Ready Up");
            SetButtonVisual(readyButton, readyButtonImage, localReady ? AccentCool : Success);
        }

        startButton.interactable = isHost && allReady && !currentLobby.isStarted;
        SetButtonVisual(startButton, startButtonImage, startButton.interactable ? Accent : PanelSoft);
        copyCodeButton.interactable = !string.IsNullOrWhiteSpace(currentLobby.code);
        leaveLobbyButton.interactable = true;

        for (int i = 0; i < lobbySlotViews.Count; i++)
        {
            LobbyMemberDto member = FindMemberInSlot(currentLobby, i);
            ApplyLobbySlot(lobbySlotViews[i], member, i);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            SetText(lobbyStatusText, status);
        }
    }

    private void CacheLobbyPlayerSlots()
    {
        playerSlotsById.Clear();
        if (currentLobby?.members == null)
        {
            return;
        }

        foreach (LobbyMemberDto member in currentLobby.members)
        {
            if (!string.IsNullOrWhiteSpace(member.playerId))
            {
                playerSlotsById[member.playerId] = member.slot;
            }
        }
    }

    private void CacheGameStartedPlayerSlots(GameStartedDto start)
    {
        if (start?.players == null)
        {
            return;
        }

        foreach (GameStartedPlayerDto player in start.players)
        {
            if (!string.IsNullOrWhiteSpace(player.playerId))
            {
                playerSlotsById[player.playerId] = player.slot;
            }
        }
    }

    private void HideAllPanels()
    {
        loginPanel.SetActive(false);
        findPanel.SetActive(false);
        lobbyPanel.SetActive(false);
        gameHudPanel.SetActive(false);
    }

    private static int LobbyCapacity(LobbyDto lobby)
    {
        return lobby != null && lobby.maxPlayers > 0 ? lobby.maxPlayers : DefaultMaxPlayers;
    }

    private static int CountMembers(LobbyDto lobby)
    {
        return lobby?.members == null ? 0 : lobby.members.Length;
    }

    private static int CountReadyMembers(LobbyDto lobby)
    {
        if (lobby?.members == null)
        {
            return 0;
        }

        int ready = 0;
        foreach (LobbyMemberDto member in lobby.members)
        {
            if (member.isReady)
            {
                ready++;
            }
        }

        return ready;
    }

    private static LobbyMemberDto FindMemberInSlot(LobbyDto lobby, int slot)
    {
        if (lobby?.members == null)
        {
            return null;
        }

        foreach (LobbyMemberDto member in lobby.members)
        {
            if (member.slot == slot)
            {
                return member;
            }
        }

        return null;
    }

    private static LobbyMemberDto FindMember(LobbyDto lobby, int userId)
    {
        if (lobby?.members == null)
        {
            return null;
        }

        foreach (LobbyMemberDto member in lobby.members)
        {
            if (member.userId == userId)
            {
                return member;
            }
        }

        return null;
    }

    private static Vector3 SpawnForSlot(int slot)
    {
        Vector3[] spawns =
        {
            new Vector3(-4f, 0.5f, -4f),
            new Vector3(4f, 0.5f, -4f),
            new Vector3(-4f, 0.5f, 4f),
            new Vector3(4f, 0.5f, 4f),
        };
        return spawns[Mathf.Abs(slot) % spawns.Length];
    }

    private Color GetPlayerColor(string playerId)
    {
        Color[] colors =
        {
            new Color(1f, 0.85f, 0.05f), // host / slot 0: yellow
            new Color(0.1f, 0.45f, 1f), // slot 1: blue
            new Color(0.15f, 0.85f, 0.3f), // slot 2: green
            new Color(0.95f, 0.2f, 0.95f), // slot 3: magenta
        };

        if (!string.IsNullOrWhiteSpace(playerId) && playerSlotsById.TryGetValue(playerId, out int slot))
        {
            return colors[Mathf.Abs(slot) % colors.Length];
        }

        return new Color(0.8f, 0.8f, 0.8f);
    }

    private static void SetText(Text text, string value)
    {
        if (text != null)
        {
            text.text = value ?? string.Empty;
        }
    }

    private static void SetButtonText(Button button, string value)
    {
        Text text = button.GetComponentInChildren<Text>();
        if (text != null)
        {
            text.text = value;
        }
    }

    private static void SetButtonVisual(Button button, Image image, Color enabledColor)
    {
        if (image != null)
        {
            image.color = button != null && button.interactable ? enabledColor : new Color(0.2f, 0.23f, 0.32f, 0.9f);
        }
    }

    private static void SetRendererColor(GameObject target, Color color)
    {
        Renderer renderer = target.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material = CreateRuntimeMaterial(color);
        }
    }

    private static void EnsureEventSystem()
    {
        if (FindAnyObjectByType<EventSystem>() != null)
        {
            return;
        }

        GameObject eventSystem = new GameObject("EventSystem");
        eventSystem.AddComponent<EventSystem>();
        eventSystem.AddComponent<InputSystemUIInputModule>();
    }

    private static Font GetUiFont()
    {
        if (cachedUiFont != null)
        {
            return cachedUiFont;
        }

        cachedUiFont = Resources.GetBuiltinResource<Font>(BuiltInFontName);
        return cachedUiFont;
    }

    private static Material CreateRuntimeMaterial(Color color)
    {
        Shader shader = GetRuntimeObjectShader();
        Material material = new Material(shader);
        material.color = color;
        return material;
    }

    private static Shader GetRuntimeObjectShader()
    {
        if (cachedObjectShader != null)
        {
            return cachedObjectShader;
        }

        cachedObjectShader = Shader.Find("Universal Render Pipeline/Lit");
        if (cachedObjectShader == null)
        {
            cachedObjectShader = Shader.Find("Universal Render Pipeline/Unlit");
        }

        if (cachedObjectShader == null)
        {
            cachedObjectShader = Shader.Find("Unlit/Color");
        }

        if (cachedObjectShader == null)
        {
            cachedObjectShader = Shader.Find("Standard");
        }

        return cachedObjectShader;
    }

    private sealed class LobbySlotView
    {
        public LobbySlotView(Image background, Image accent, Text nameText, Text statusText)
        {
            Background = background;
            Accent = accent;
            NameText = nameText;
            StatusText = statusText;
        }

        public Image Background { get; }
        public Image Accent { get; }
        public Text NameText { get; }
        public Text StatusText { get; }
    }
}

public sealed class LocalCubeController : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 6f;
    [SerializeField] private float jumpForce = 5f;

    private Rigidbody body;
    private Transform cameraTransform;
    private bool isGrounded = true;
    private Vector3 previousPosition;

    public Vector3 Velocity { get; private set; }

    public void Setup(Transform cameraTransform)
    {
        this.cameraTransform = cameraTransform;
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        previousPosition = transform.position;
    }

    private void Update()
    {
        if (cameraTransform != null)
        {
            cameraTransform.position = transform.position + new Vector3(0f, 8f, -9f);
            cameraTransform.rotation = Quaternion.Euler(45f, 0f, 0f);
        }
    }

    private void FixedUpdate()
    {
        Keyboard keyboard = Keyboard.current;
        Vector2 input = Vector2.zero;
        if (keyboard != null)
        {
            if (keyboard.wKey.isPressed) input.y += 1f;
            if (keyboard.sKey.isPressed) input.y -= 1f;
            if (keyboard.dKey.isPressed) input.x += 1f;
            if (keyboard.aKey.isPressed) input.x -= 1f;

            if (keyboard.spaceKey.wasPressedThisFrame && isGrounded)
            {
                body.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
                isGrounded = false;
            }
        }

        Vector3 movement = new Vector3(input.x, 0f, input.y);
        if (movement.sqrMagnitude > 1f)
        {
            movement.Normalize();
        }

        body.MovePosition(body.position + movement * moveSpeed * Time.fixedDeltaTime);
        if (movement.sqrMagnitude > 0.001f)
        {
            body.MoveRotation(Quaternion.LookRotation(movement));
        }

        Velocity = (transform.position - previousPosition) / Time.fixedDeltaTime;
        previousPosition = transform.position;
    }

    private void OnCollisionEnter(Collision collision)
    {
        foreach (ContactPoint contact in collision.contacts)
        {
            if (contact.normal.y > 0.5f)
            {
                isGrounded = true;
                break;
            }
        }
    }
}

public sealed class RemoteCubeController : MonoBehaviour
{
    [SerializeField] private float interpolationSpeed = 12f;

    private Vector3 targetPosition;
    private Quaternion targetRotation;

    private void Awake()
    {
        targetPosition = transform.position;
        targetRotation = transform.rotation;
    }

    public void ApplyState(PlayerStateDto state)
    {
        targetPosition = MultiplayerJson.ArrayToVector(state.position);
        targetRotation = Quaternion.Euler(MultiplayerJson.ArrayToVector(state.rotation));
    }

    private void Update()
    {
        transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * interpolationSpeed);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * interpolationSpeed);
    }
}
