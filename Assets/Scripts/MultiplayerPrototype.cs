using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

public sealed class MultiplayerPrototype : MonoBehaviour
{
    private const string DefaultServerUrl = "https://cavegame-production.up.railway.app";
    private const float StateSendInterval = 1f / 30f;
    private const float ForcedStateSendInterval = 0.5f;
    private const float MinPositionDeltaSqr = 0.0004f;
    private const float MinRotationDelta = 1.5f;
    private const float MammothStateSendInterval = 1f / 20f;
    private const float MammothForcedStateSendInterval = 0.2f;
    private const float MammothRemoteLerpSpeed = 10f;
    private const float LobbyPingInterval = 2f;
    private const float GamePingInterval = 1f;
    private const float LobbyHeartbeatTimeout = 8f;
    private const float GameHeartbeatTimeout = 5f;
    private const float ReconnectBaseDelay = 0.35f;
    private const float ReconnectMaxDelay = 6f;
    private const float HudRefreshInterval = 0.25f;
    private const float SpawnHeightOffset = 0.75f;
    private const float SpawnNavMeshProbeHeight = 40f;
    private const float SpawnNavMeshSampleRadius = 80f;
    private const float SpawnRaycastHeight = 200f;
    private const float SpawnRaycastDistance = 600f;
    private const string BuiltInFontName = "LegacyRuntime.ttf";
    private const int DefaultMaxPlayers = 4;
    private static Font cachedUiFont;
    private static Shader cachedObjectShader;
    public static MultiplayerPrototype Instance { get; private set; }

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
    private bool lobbyReconnectQueued;
    private bool suppressLobbyReconnect;
    private bool gameReconnectQueued;
    private int currentGameLobbyId = -1;

    private string authToken;
    private UserDto currentUser;
    private LobbyDto currentLobby;
    private LobbyMemberDto localMember;
    private bool gameStarted;
    private int stateSeq;
    private float nextStateSendTime;
    private float lastStateSendTime;
    private bool hasSentInitialState;
    private Vector3 lastSentPosition;
    private Vector3 lastSentEulerAngles;
    private float nextGamePingTime;
    private float nextLobbyPingTime;
    private float nextHudRefreshTime;
    private float lastGameRttMs = -1f;
    private float lastRemoteStateReceiveTime = -1f;
    private float remoteStateRateWindowStart;
    private int remoteStatesInWindow;
    private int remoteStatesPerSecond;

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
    private WorldChunkRenderer worldChunkRenderer;
    private LocalCubeController localCube;
    private readonly Dictionary<string, RemoteCubeController> remoteCubes = new Dictionary<string, RemoteCubeController>();
    private readonly Dictionary<string, int> playerSlotsById = new Dictionary<string, int>();
    private Vector3 runtimeSpawnAnchor = Vector3.zero;
    private MammothStateDto pendingMammothState;
    private MammothHealthDto pendingMammothHealth;
    private EnemyHealth cachedMammothEnemy;
    private bool mammothRuntimeConfigured;
    private float nextMammothStateSendTime;
    private float lastMammothStateSendTime;
    private bool hasSentInitialMammothState;
    private Vector3 lastSentMammothPosition;
    private Vector3 lastSentMammothEulerAngles;
    private Vector3 targetRemoteMammothPosition;
    private Quaternion targetRemoteMammothRotation = Quaternion.identity;
    private bool hasRemoteMammothPose;

    [Header("Networking Debug")]
    [SerializeField] private bool verboseNetworkingLogs = true;
    [SerializeField] private bool logSocketPayloads = false;
    [SerializeField] private bool logRemoteStateDecisions = true;
    [SerializeField] private bool logHeartbeatMessages = false;

    private string debugClientTag;
    private int lobbyMessagesReceived;
    private int gameMessagesReceived;
    private int remoteStatesApplied;
    private int remoteStatesDroppedAsLocal;
    private int remoteStatesDroppedInvalid;
    private int remoteStatesSpawned;
    private int gameSocketReconnectAttempts;
    private int lobbySocketReconnectAttempts;
    private string lastLobbySocketCloseCode = "none";
    private string lastGameSocketCloseCode = "none";
    private bool isShuttingDown;
    private float lastGamePingSendTime = -1f;
    private float lastGamePongReceiveTime = -1f;
    private float lastLobbyPingSendTime = -1f;
    private float lastLobbyPongReceiveTime = -1f;
    private float lastGameHeartbeatReceiveTime = -1f;
    private float lastLobbyHeartbeatReceiveTime = -1f;
    private float lastLobbySocketCloseTime = -1f;
    private float lastGameSocketCloseTime = -1f;
    private float lastGameSocketCloseGapMs = -1f;
    private string lastLobbyEnvelopeType = "none";
    private string lastGameEnvelopeType = "none";
    private float lastLobbyEnvelopeTime = -1f;
    private float lastGameEnvelopeTime = -1f;
    private bool lobbyCloseExpected;
    private string lobbyCloseExpectedReason = "none";
    private bool gameCloseExpected;
    private string gameCloseExpectedReason = "none";
    private int lobbySocketGeneration;
    private int gameSocketGeneration;
    private bool lobbyHeartbeatCloseRequested;
    private bool gameHeartbeatCloseRequested;
    private float nextLobbyReconnectAllowedAt;
    private float nextGameReconnectAllowedAt;

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
        Instance = this;
        Application.runInBackground = true;
        debugClientTag = System.Guid.NewGuid().ToString("N").Substring(0, 6);
        api = new CaveGameApiClient(DefaultServerUrl, () => authToken);
        BuildUi();
        NetLog("Bootstrap complete.");
        ShowLogin("Enter a display name, then authenticate with the backend.");
    }

    private void Update()
    {
        lobbySocket?.Pump();
        gameSocket?.Pump();

        TryConfigureMammothRuntime();
        UpdateRemoteMammothPose();

        if (pendingMammothState != null)
        {
            TryApplyMammothState(pendingMammothState);
        }

        if (pendingMammothHealth != null)
        {
            TryApplyMammothHealth(pendingMammothHealth);
        }

        if (Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame)
        {
            DumpMultiplayerDebugSnapshot();
        }

        if (!gameStarted && lobbySocket != null && lobbySocket.IsOpen && Time.unscaledTime >= nextLobbyPingTime)
        {
            nextLobbyPingTime = Time.unscaledTime + LobbyPingInterval;
            lastLobbyPingSendTime = Time.unscaledTime;
            lobbySocket.SendJson(JsonUtility.ToJson(new PingDto { clientTime = Time.realtimeSinceStartupAsDouble }));
            lobbySocket.SendJson(JsonUtility.ToJson(new HeartbeatDto { clientTime = Time.realtimeSinceStartupAsDouble }));
        }

        if (!gameStarted
            && lobbySocket != null
            && lobbySocket.IsOpen
            && !lobbyHeartbeatCloseRequested
            && IsHeartbeatTimedOut(lastLobbyPingSendTime, lastLobbyPongReceiveTime, lastLobbyHeartbeatReceiveTime, LobbyHeartbeatTimeout))
        {
            lobbyHeartbeatCloseRequested = true;
            NetLog($"Lobby heartbeat timeout detected. {DescribeHeartbeat(lastLobbyPingSendTime, lastLobbyPongReceiveTime, lastLobbyHeartbeatReceiveTime)}", true);
            QueueLobbyReconnectIfNeeded();
            MarkExpectedLobbyClose("heartbeat_timeout");
            lobbySocket.Close();
        }

        if (!gameStarted || gameSocket == null || !gameSocket.IsOpen)
        {
            return;
        }

        if (localCube != null && Time.unscaledTime >= nextStateSendTime && ShouldSendStateNow(Time.unscaledTime))
        {
            string outboundPlayerId = GetLocalPlayerId();
            if (string.IsNullOrWhiteSpace(outboundPlayerId))
            {
                return;
            }

            nextStateSendTime = Time.unscaledTime + StateSendInterval;
            PlayerStateDto state = PlayerStateDto.FromTransform(
                outboundPlayerId,
                localMember != null ? localMember.userId : (currentUser != null ? currentUser.id : 0),
                ++stateSeq,
                localCube.transform,
                localCube.Velocity
            );
            gameSocket.SendJson(JsonUtility.ToJson(state));
            MarkStateSent(Time.unscaledTime);
        }

        if (Time.unscaledTime >= nextMammothStateSendTime && ShouldSendMammothStateNow(Time.unscaledTime))
        {
            nextMammothStateSendTime = Time.unscaledTime + MammothStateSendInterval;
            SendMammothStateUpdate();
        }

        if (Time.unscaledTime >= nextGamePingTime)
        {
            nextGamePingTime = Time.unscaledTime + GamePingInterval;
            lastGamePingSendTime = Time.unscaledTime;
            gameSocket.SendJson(JsonUtility.ToJson(new PingDto { clientTime = Time.realtimeSinceStartupAsDouble }));
            gameSocket.SendJson(JsonUtility.ToJson(new HeartbeatDto { clientTime = Time.realtimeSinceStartupAsDouble }));
        }

        if (!gameHeartbeatCloseRequested
            && IsHeartbeatTimedOut(lastGamePingSendTime, lastGamePongReceiveTime, lastGameHeartbeatReceiveTime, GameHeartbeatTimeout))
        {
            gameHeartbeatCloseRequested = true;
            NetLog($"Game heartbeat timeout detected. {DescribeHeartbeat(lastGamePingSendTime, lastGamePongReceiveTime, lastGameHeartbeatReceiveTime)}", true);
            QueueGameReconnectIfNeeded();
            MarkExpectedGameClose("heartbeat_timeout");
            gameSocket.Close();
        }

        if (Time.unscaledTime >= nextHudRefreshTime)
        {
            nextHudRefreshTime = Time.unscaledTime + HudRefreshInterval;
            RefreshGameHud();
        }
    }

    private void OnDestroy()
    {
        isShuttingDown = true;
        suppressLobbyReconnect = true;
        DetachWorldChunkRendererPlayers();
        lobbySocket?.Close();
        gameSocket?.Close();
        if (ReferenceEquals(Instance, this))
        {
            Instance = null;
        }
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
        suppressLobbyReconnect = true;
        MarkExpectedLobbyClose("leave_lobby");
        lobbySocket?.Close();
        lobbySocket = null;
        currentLobby = null;
        localMember = null;
        playerSlotsById.Clear();
        ShowFind("Left lobby. Create a new room or jump into another code.");
    }

    private void OpenLobbySocket()
    {
        suppressLobbyReconnect = false;
        lobbyReconnectQueued = false;
        if (lobbySocket != null && lobbySocket.IsOpen)
        {
            NetLog("Lobby socket open request skipped because socket is already open.");
            return;
        }

        int generation = ++lobbySocketGeneration;
        if (lobbySocket != null)
        {
            MarkExpectedLobbyClose("replace_lobby_socket");
            lobbySocket.Close();
        }

        CaveGameSocketClient socketClient = new CaveGameSocketClient();
        lobbySocket = socketClient;
        string url = api.BuildWebSocketUrl($"/ws/lobby/{currentLobby.id}/");
        NetLog($"Opening lobby socket: {url}");
        socketClient.Opened += () =>
        {
            if (generation != lobbySocketGeneration || isShuttingDown || this == null)
            {
                return;
            }

            SetText(lobbyStatusText, "Connected to lobby socket.");
            NetLog("Lobby socket opened.");
            nextLobbyPingTime = Time.unscaledTime;
            lastLobbyPingSendTime = Time.unscaledTime;
            lastLobbyPongReceiveTime = Time.unscaledTime;
            lastLobbyHeartbeatReceiveTime = Time.unscaledTime;
            lobbyHeartbeatCloseRequested = false;
            lobbySocketReconnectAttempts = 0;
            nextLobbyReconnectAllowedAt = 0f;
        };
        socketClient.ErrorReceived += error =>
        {
            if (generation != lobbySocketGeneration)
            {
                return;
            }

            SetText(lobbyStatusText, "Lobby socket error: " + error);
            NetLog("Lobby socket error: " + error, true);
            LogSocketTrace("Lobby socket trace on error", socketClient, true, false, "socket_error");
        };
        socketClient.Closed += closeCode =>
        {
            if (generation != lobbySocketGeneration || isShuttingDown || this == null)
            {
                return;
            }

            bool isCurrentSocket = ReferenceEquals(lobbySocket, socketClient);
            bool intentionalClose = !isCurrentSocket;
            string closeReason = intentionalClose ? "stale_socket_replaced" : "none";
            if (TryConsumeExpectedLobbyClose(out string expectedReason))
            {
                intentionalClose = true;
                closeReason = expectedReason;
            }

            lastLobbySocketCloseCode = closeCode;
            lastLobbySocketCloseTime = Time.unscaledTime;
            SetText(lobbyStatusText, "Lobby socket closed (" + closeCode + ").");
            bool warning = !intentionalClose && IsSocketCloseWarning(closeCode);
            NetLog($"Lobby socket closed: {closeCode} (intentional={intentionalClose}, reason={closeReason})", warning);
            LogSocketTrace("Lobby socket trace on close", socketClient, warning, intentionalClose, closeReason);
            if (!intentionalClose && isCurrentSocket)
            {
                QueueLobbyReconnectIfNeeded();
            }
        };
        socketClient.MessageReceived += HandleLobbySocketMessage;
        socketClient.Connect(url);
    }

    private void QueueLobbyReconnectIfNeeded()
    {
        if (suppressLobbyReconnect || lobbyReconnectQueued || currentLobby == null || gameStarted)
        {
            return;
        }
        if (lobbySocket != null && lobbySocket.IsOpen)
        {
            return;
        }
        if (Time.unscaledTime < nextLobbyReconnectAllowedAt)
        {
            return;
        }

        lobbyReconnectQueued = true;
        StartCoroutine(ReconnectLobbySocketAfterDelay());
    }

    private IEnumerator ReconnectLobbySocketAfterDelay()
    {
        float reconnectDelay = ComputeReconnectDelay(lobbySocketReconnectAttempts);
        yield return new WaitForSecondsRealtime(reconnectDelay);
        lobbyReconnectQueued = false;

        if (suppressLobbyReconnect || currentLobby == null || gameStarted)
        {
            yield break;
        }
        if (lobbySocket != null && lobbySocket.IsOpen)
        {
            yield break;
        }

        lobbySocketReconnectAttempts++;
        nextLobbyReconnectAllowedAt = Time.unscaledTime + reconnectDelay;
        SetText(lobbyStatusText, "Reconnecting lobby socket...");
        NetLog($"Lobby reconnect attempt #{lobbySocketReconnectAttempts} after {reconnectDelay:0.00}s.");
        OpenLobbySocket();
    }

    private void HandleLobbySocketMessage(string json)
    {
        if (isShuttingDown || this == null)
        {
            return;
        }

        lobbyMessagesReceived++;
        SocketTypeEnvelopeDto envelope = JsonUtility.FromJson<SocketTypeEnvelopeDto>(json);
        string envelopeType = envelope != null && !string.IsNullOrWhiteSpace(envelope.type) ? envelope.type : "unknown";
        if (logHeartbeatMessages || !IsHeartbeatEnvelope(envelopeType))
        {
            NetLog($"Lobby message #{lobbyMessagesReceived}: {envelopeType}");
        }
        lastLobbyEnvelopeType = envelopeType;
        lastLobbyEnvelopeTime = Time.unscaledTime;
        if (string.Equals(lastLobbyEnvelopeType, "pong", StringComparison.OrdinalIgnoreCase))
        {
            lastLobbyPongReceiveTime = Time.unscaledTime;
        }
        if (string.Equals(lastLobbyEnvelopeType, "heartbeat", StringComparison.OrdinalIgnoreCase))
        {
            lastLobbyHeartbeatReceiveTime = Time.unscaledTime;
        }
        if (logSocketPayloads)
        {
            NetLog($"Lobby payload: {json}");
        }
        switch (envelopeType)
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
        ResetStateSendTracking();
        EnsureLocalMemberForGameStart(start);
        CacheGameStartedPlayerSlots(start);
        currentGameLobbyId = start != null ? start.lobbyId : -1;
        gameReconnectQueued = false;
        suppressLobbyReconnect = true;
        MarkExpectedLobbyClose("transition_to_game");
        lobbySocket?.Close();
        HideAllPanels();
        gameHudPanel.SetActive(true);
        SetText(gameStatusText, $"Game started in lobby {start.lobbyId}. WASD to move, Space to jump.");
        NetLog("Entering game. " + DescribeGameStarted(start));

        BuildGameWorld();
        TryApplyMammothState(pendingMammothState);
        TryApplyMammothHealth(pendingMammothHealth);
        PreSpawnRemotePlayers(start);
        OpenGameSocket(currentGameLobbyId);
    }

    private void EnsureLocalMemberForGameStart(GameStartedDto start)
    {
        if (currentUser == null || start?.players == null)
        {
            return;
        }

        foreach (GameStartedPlayerDto player in start.players)
        {
            if (player.userId != currentUser.id)
            {
                continue;
            }

            if (localMember == null)
            {
                localMember = new LobbyMemberDto
                {
                    userId = currentUser.id,
                    username = currentUser.username,
                    playerId = player.playerId,
                    slot = player.slot,
                    isReady = true
                };
            }
            else
            {
                localMember.playerId = player.playerId;
                localMember.slot = player.slot;
            }

            return;
        }
    }

    private void BuildGameWorld()
    {
        DetachWorldChunkRendererPlayers();

        if (worldRoot != null)
        {
            Destroy(worldRoot);
        }

        remoteCubes.Clear();
        worldRoot = new GameObject("Multiplayer Runtime World");
        worldChunkRenderer = FindAnyObjectByType<WorldChunkRenderer>();
        runtimeSpawnAnchor = ResolveRuntimeSpawnAnchor();

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

        CameraFollow sceneCameraFollow = camera.GetComponent<CameraFollow>();
        if (sceneCameraFollow != null && sceneCameraFollow.enabled)
        {
            sceneCameraFollow.enabled = false;
            NetLog("Disabled scene CameraFollow for multiplayer runtime camera.");
        }

        camera.clearFlags = CameraClearFlags.Skybox;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 1000f;

        Vector3 spawn = ResolveSafeSpawnPosition(ResolveLocalSpawnSlot(), currentUser != null ? currentUser.id : 0, "local");
        string localKey = BuildPlayerKey(localMember != null ? localMember.playerId : null, currentUser != null ? currentUser.id : 0);
        GameObject local = CreatePlayerCube("Local Player Cube", spawn, GetPlayerColor(localKey), true);
        localCube = local.GetComponent<LocalCubeController>();
        localCube.Setup(camera.transform);
        RegisterWorldChunkRendererPlayer(localCube.transform, true);
    }

    private void PreSpawnRemotePlayers(GameStartedDto start)
    {
        if (start?.players == null)
        {
            return;
        }

        foreach (GameStartedPlayerDto player in start.players)
        {
            if (player == null)
            {
                continue;
            }

            if (currentUser != null && player.userId == currentUser.id)
            {
                continue;
            }

            string key = BuildPlayerKey(player.playerId, player.userId);
            if (remoteCubes.ContainsKey(key))
            {
                NetLog($"Remote cube pre-spawn skipped (already exists): {key}");
                continue;
            }

            GameObject remoteObject = CreatePlayerCube(
                "Remote Player Cube " + key,
                ResolveSafeSpawnPosition(player.slot, player.userId, $"remote-pre:{key}"),
                GetPlayerColor(key),
                false
            );
            RemoteCubeController remote = remoteObject.GetComponent<RemoteCubeController>();
            remoteCubes[key] = remote;
            RegisterWorldChunkRendererPlayer(remote.transform, false);
            remoteStatesSpawned++;
            NetLog($"Pre-spawned remote cube key={key}, slot={player.slot}, userId={player.userId}, pos={remoteObject.transform.position}");
        }
    }

    private void OpenGameSocket(int lobbyId)
    {
        if (gameSocket != null && gameSocket.IsOpen)
        {
            NetLog("Game socket open request skipped because socket is already open.");
            return;
        }

        int generation = ++gameSocketGeneration;
        if (gameSocket != null)
        {
            MarkExpectedGameClose("replace_game_socket");
            gameSocket.Close();
        }

        CaveGameSocketClient socketClient = new CaveGameSocketClient();
        gameSocket = socketClient;
        string url = api.BuildWebSocketUrl($"/ws/game/{lobbyId}/");
        NetLog($"Opening game socket: {url}");
        socketClient.Opened += () =>
        {
            if (generation != gameSocketGeneration || isShuttingDown || this == null)
            {
                return;
            }

            SetText(gameStatusText, "Connected to game socket. Sending transform state at up to 30 Hz.");
            NetLog("Game socket opened.");
            nextGamePingTime = Time.unscaledTime;
            lastGamePingSendTime = Time.unscaledTime;
            lastGamePongReceiveTime = Time.unscaledTime;
            lastGameHeartbeatReceiveTime = Time.unscaledTime;
            gameHeartbeatCloseRequested = false;
            gameSocketReconnectAttempts = 0;
            nextGameReconnectAllowedAt = 0f;
        };
        socketClient.ErrorReceived += error =>
        {
            if (generation != gameSocketGeneration || isShuttingDown || this == null)
            {
                return;
            }

            SetText(gameStatusText, "Game socket error: " + error);
            NetLog("Game socket error: " + error, true);
            LogSocketTrace("Game socket trace on error", socketClient, true, false, "socket_error");
        };
        socketClient.Closed += closeCode =>
        {
            if (generation != gameSocketGeneration || isShuttingDown || this == null)
            {
                return;
            }

            bool isCurrentSocket = ReferenceEquals(gameSocket, socketClient);
            bool intentionalClose = !isCurrentSocket;
            string closeReason = intentionalClose ? "stale_socket_replaced" : "none";
            if (TryConsumeExpectedGameClose(out string expectedReason))
            {
                intentionalClose = true;
                closeReason = expectedReason;
            }

            float now = Time.unscaledTime;
            lastGameSocketCloseGapMs = lastGameSocketCloseTime >= 0f ? (now - lastGameSocketCloseTime) * 1000f : -1f;
            lastGameSocketCloseTime = now;
            lastGameSocketCloseCode = closeCode;
            SetText(gameStatusText, "Game socket closed (" + closeCode + ").");
            bool warning = !intentionalClose && IsSocketCloseWarning(closeCode);
            NetLog($"Game socket closed: {closeCode} (intentional={intentionalClose}, reason={closeReason})", warning);
            NetLog($"Game heartbeat before close: {DescribeHeartbeat(lastGamePingSendTime, lastGamePongReceiveTime, lastGameHeartbeatReceiveTime)}", warning);
            if (lastGameSocketCloseGapMs >= 0f)
            {
                NetLog($"Game socket close cadence: {lastGameSocketCloseGapMs:0}ms since previous close.", warning);
            }
            LogSocketTrace("Game socket trace on close", socketClient, warning, intentionalClose, closeReason);
            if (!intentionalClose && isCurrentSocket)
            {
                QueueGameReconnectIfNeeded();
            }
        };
        socketClient.MessageReceived += HandleGameSocketMessage;
        socketClient.Connect(url);
    }

    private void QueueGameReconnectIfNeeded()
    {
        if (!gameStarted || gameReconnectQueued || currentGameLobbyId <= 0)
        {
            return;
        }
        if (gameSocket != null && gameSocket.IsOpen)
        {
            return;
        }
        if (Time.unscaledTime < nextGameReconnectAllowedAt)
        {
            return;
        }

        gameReconnectQueued = true;
        StartCoroutine(ReconnectGameSocketAfterDelay());
    }

    private IEnumerator ReconnectGameSocketAfterDelay()
    {
        float reconnectDelay = ComputeReconnectDelay(gameSocketReconnectAttempts);
        yield return new WaitForSecondsRealtime(reconnectDelay);
        gameReconnectQueued = false;

        if (!gameStarted || currentGameLobbyId <= 0)
        {
            yield break;
        }
        if (gameSocket != null && gameSocket.IsOpen)
        {
            yield break;
        }

        gameSocketReconnectAttempts++;
        nextGameReconnectAllowedAt = Time.unscaledTime + reconnectDelay;
        SetText(gameStatusText, "Reconnecting game socket...");
        NetLog($"Game reconnect attempt #{gameSocketReconnectAttempts} after {reconnectDelay:0.00}s.");
        OpenGameSocket(currentGameLobbyId);
    }

    private void HandleGameSocketMessage(string json)
    {
        if (isShuttingDown || this == null)
        {
            return;
        }

        gameMessagesReceived++;
        SocketTypeEnvelopeDto envelope = JsonUtility.FromJson<SocketTypeEnvelopeDto>(json);
        string envelopeType = envelope != null && !string.IsNullOrWhiteSpace(envelope.type) ? envelope.type : "unknown";
        if (logHeartbeatMessages || !IsHeartbeatEnvelope(envelopeType))
        {
            NetLog($"Game message #{gameMessagesReceived}: {envelopeType}");
        }
        lastGameEnvelopeType = envelopeType;
        lastGameEnvelopeTime = Time.unscaledTime;
        if (logSocketPayloads)
        {
            NetLog($"Game payload: {json}");
        }
        switch (envelopeType)
        {
            case "room_snapshot":
                RoomSnapshotDto snapshot = JsonUtility.FromJson<RoomSnapshotDto>(json);
                if (snapshot.players == null)
                {
                    snapshot.players = Array.Empty<PlayerStateDto>();
                }

                foreach (PlayerStateDto player in snapshot.players)
                {
                    ApplyRemoteState(player);
                }

                TryApplyMammothState(snapshot.mammothState);
                TryApplyMammothHealth(snapshot.mammothHealth);
                break;
            case "player_state":
                ApplyRemoteState(JsonUtility.FromJson<PlayerStateDto>(json));
                break;
            case "mammoth_state":
                TryApplyMammothState(JsonUtility.FromJson<MammothStateDto>(json));
                break;
            case "mammoth_health":
                TryApplyMammothHealth(JsonUtility.FromJson<MammothHealthDto>(json));
                break;
            case "pong":
                HandleGamePong(JsonUtility.FromJson<PongDto>(json));
                break;
            case "heartbeat":
                lastGameHeartbeatReceiveTime = Time.unscaledTime;
                break;
            case "player_left":
                LobbyEventDto left = JsonUtility.FromJson<LobbyEventDto>(json);
                RemoveRemotePlayer(BuildPlayerKey(left != null ? left.playerId : null, left != null ? left.userId : 0));
                break;
        }
    }

    private void ApplyRemoteState(PlayerStateDto state)
    {
        if (state == null)
        {
            remoteStatesDroppedInvalid++;
            return;
        }

        bool hasLocalUserId = (localMember != null && localMember.userId > 0) || (currentUser != null && currentUser.id > 0);
        int effectiveLocalUserId = localMember != null && localMember.userId > 0
            ? localMember.userId
            : (currentUser != null ? currentUser.id : 0);

        bool isLocalByUserId = hasLocalUserId && state.userId > 0 && state.userId == effectiveLocalUserId;
        bool isLocalByPlayerIdFallback = !hasLocalUserId
            && localMember != null
            && !string.IsNullOrWhiteSpace(localMember.playerId)
            && !string.IsNullOrWhiteSpace(state.playerId)
            && state.playerId == localMember.playerId;

        if (isLocalByUserId || isLocalByPlayerIdFallback)
        {
            remoteStatesDroppedAsLocal++;
            if (logRemoteStateDecisions)
            {
                NetLog($"Dropped remote state as local. state.playerId={state.playerId}, state.userId={state.userId}, local.playerId={localMember?.playerId}, local.userId={effectiveLocalUserId}");
            }
            return;
        }

        string remoteKey = BuildPlayerKey(state.playerId, state.userId);

        if (!remoteCubes.TryGetValue(remoteKey, out RemoteCubeController remote))
        {
            Vector3 initialPosition = MultiplayerJson.ArrayToVector(state.position);
            if (initialPosition == Vector3.zero)
            {
                initialPosition = ResolveSafeSpawnPosition(0, state.userId, $"remote-state:{remoteKey}");
            }
            GameObject remoteObject = CreatePlayerCube(
                "Remote Player Cube " + remoteKey,
                initialPosition,
                GetPlayerColor(remoteKey),
                false
            );
            remote = remoteObject.GetComponent<RemoteCubeController>();
            remoteCubes[remoteKey] = remote;
            RegisterWorldChunkRendererPlayer(remote.transform, false);
            remoteStatesSpawned++;
            if (logRemoteStateDecisions)
            {
                NetLog($"Spawned remote from state key={remoteKey}, state.userId={state.userId}, state.pos={initialPosition}");
            }
        }

        remote.ApplyState(state);
        remoteStatesApplied++;
        RecordRemoteStateReceived();
    }

    private void HandleGamePong(PongDto pong)
    {
        if (pong == null || pong.clientTime <= 0)
        {
            return;
        }

        lastGamePongReceiveTime = Time.unscaledTime;
        lastGameRttMs = Mathf.Max(0f, (float)((Time.realtimeSinceStartupAsDouble - pong.clientTime) * 1000.0));
    }

    private static bool IsHeartbeatEnvelope(string envelopeType)
    {
        return string.Equals(envelopeType, "ping", StringComparison.OrdinalIgnoreCase)
            || string.Equals(envelopeType, "pong", StringComparison.OrdinalIgnoreCase)
            || string.Equals(envelopeType, "heartbeat", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHeartbeatTimedOut(float lastPingTime, float lastPongTime, float lastHeartbeatTime, float timeoutSeconds)
    {
        if (lastPingTime < 0f)
        {
            return false;
        }

        float lastSignalTime = Mathf.Max(lastPongTime, lastHeartbeatTime);
        if (lastPingTime > lastSignalTime)
        {
            // A new ping was just sent and we have not received an ack yet.
            // Start timeout from that ping instead of stale previous-session acks.
            return (Time.unscaledTime - lastPingTime) > timeoutSeconds;
        }

        if (lastSignalTime < 0f)
        {
            return (Time.unscaledTime - lastPingTime) > timeoutSeconds;
        }

        return (Time.unscaledTime - lastSignalTime) > timeoutSeconds;
    }

    private static float ComputeReconnectDelay(int previousAttempts)
    {
        float expDelay = ReconnectBaseDelay * Mathf.Pow(2f, Mathf.Clamp(previousAttempts, 0, 6));
        return Mathf.Min(ReconnectMaxDelay, expDelay);
    }

    private static string DescribeHeartbeat(float lastPingTime, float lastPongTime, float lastHeartbeatTime)
    {
        string pingAge = lastPingTime >= 0f ? $"{Mathf.Max(0f, (Time.unscaledTime - lastPingTime) * 1000f):0}ms ago" : "n/a";
        string pongAge = lastPongTime >= 0f ? $"{Mathf.Max(0f, (Time.unscaledTime - lastPongTime) * 1000f):0}ms ago" : "n/a";
        string heartbeatAge = lastHeartbeatTime >= 0f ? $"{Mathf.Max(0f, (Time.unscaledTime - lastHeartbeatTime) * 1000f):0}ms ago" : "n/a";
        return $"lastPing={pingAge}, lastPong={pongAge}, lastHeartbeat={heartbeatAge}";
    }

    private void RecordRemoteStateReceived()
    {
        float now = Time.unscaledTime;
        lastRemoteStateReceiveTime = now;

        if (now - remoteStateRateWindowStart >= 1f)
        {
            remoteStatesPerSecond = remoteStatesInWindow;
            remoteStatesInWindow = 0;
            remoteStateRateWindowStart = now;
        }

        remoteStatesInWindow++;
    }

    private void RemoveRemotePlayer(string playerId)
    {
        if (!remoteCubes.TryGetValue(playerId, out RemoteCubeController remote))
        {
            return;
        }

        worldChunkRenderer?.UnregisterTrackedPlayer(remote.transform);
        Destroy(remote.gameObject);
        remoteCubes.Remove(playerId);
    }

    private void RegisterWorldChunkRendererPlayer(Transform playerTransform, bool isPrimaryPlayer)
    {
        if (playerTransform == null)
        {
            return;
        }

        if (worldChunkRenderer == null)
        {
            worldChunkRenderer = FindAnyObjectByType<WorldChunkRenderer>();
        }

        if (worldChunkRenderer == null)
        {
            return;
        }

        if (isPrimaryPlayer)
        {
            worldChunkRenderer.SetPrimaryPlayer(playerTransform);
            return;
        }

        worldChunkRenderer.RegisterTrackedPlayer(playerTransform);
    }

    private void DetachWorldChunkRendererPlayers()
    {
        if (worldChunkRenderer == null)
        {
            return;
        }

        if (localCube != null)
        {
            worldChunkRenderer.UnregisterTrackedPlayer(localCube.transform);
        }

        foreach (RemoteCubeController remote in remoteCubes.Values)
        {
            if (remote != null)
            {
                worldChunkRenderer.UnregisterTrackedPlayer(remote.transform);
            }
        }
    }

    private string GetLocalPlayerId()
    {
        if (localMember != null && !string.IsNullOrWhiteSpace(localMember.playerId))
        {
            return localMember.playerId;
        }

        if (currentUser != null && currentUser.id > 0)
        {
            return $"player_{currentUser.id}";
        }

        return null;
    }

    private int ResolveLocalSpawnSlot()
    {
        if (localMember != null && localMember.slot >= 0)
        {
            return localMember.slot;
        }

        if (currentUser != null)
        {
            return Mathf.Abs(currentUser.id) % DefaultMaxPlayers;
        }

        return 0;
    }

    private bool ShouldSendStateNow(float now)
    {
        if (!hasSentInitialState)
        {
            return true;
        }

        if (now - lastStateSendTime >= ForcedStateSendInterval)
        {
            return true;
        }

        Transform cubeTransform = localCube.transform;
        if ((cubeTransform.position - lastSentPosition).sqrMagnitude >= MinPositionDeltaSqr)
        {
            return true;
        }

        return Quaternion.Angle(Quaternion.Euler(lastSentEulerAngles), cubeTransform.rotation) >= MinRotationDelta;
    }

    private void MarkStateSent(float now)
    {
        hasSentInitialState = true;
        lastStateSendTime = now;
        lastSentPosition = localCube.transform.position;
        lastSentEulerAngles = localCube.transform.eulerAngles;
    }

    private bool IsLocalMammothAuthority()
    {
        return gameStarted && ResolveLocalSpawnSlot() == 0;
    }

    private void TryConfigureMammothRuntime()
    {
        if (!gameStarted)
        {
            return;
        }

        EnemyHealth mammoth = GetCachedMammothEnemy();
        if (mammoth == null)
        {
            mammothRuntimeConfigured = false;
            return;
        }

        if (!mammothRuntimeConfigured)
        {
            SetMammothAuthorityMode(mammoth, IsLocalMammothAuthority());
            mammothRuntimeConfigured = true;
        }
    }

    private EnemyHealth GetCachedMammothEnemy()
    {
        if (cachedMammothEnemy != null)
        {
            return cachedMammothEnemy;
        }

        cachedMammothEnemy = FindMammothEnemy();
        return cachedMammothEnemy;
    }

    private static void SetBehaviourEnabled<T>(Component root, bool isEnabled) where T : Behaviour
    {
        if (root == null)
        {
            return;
        }

        T behaviour = root.GetComponent<T>();
        if (behaviour != null)
        {
            behaviour.enabled = isEnabled;
        }
    }

    private static void SetMammothAuthorityMode(EnemyHealth mammoth, bool isAuthority)
    {
        if (mammoth == null)
        {
            return;
        }

        MammothMovement movement = mammoth.GetComponent<MammothMovement>();
        if (!isAuthority && movement != null)
        {
            movement.Stop();
        }

        NavMeshAgent agent = mammoth.GetComponent<NavMeshAgent>();
        if (agent != null)
        {
            if (!isAuthority && agent.enabled)
            {
                agent.ResetPath();
                agent.velocity = Vector3.zero;
            }

            agent.enabled = isAuthority;
        }

        SetBehaviourEnabled<MammothBrain>(mammoth, isAuthority);
        SetBehaviourEnabled<MammothActionController>(mammoth, isAuthority);
        SetBehaviourEnabled<MammothCombat>(mammoth, isAuthority);
        SetBehaviourEnabled<MammothSenses>(mammoth, isAuthority);
        SetBehaviourEnabled<MammothMovement>(mammoth, isAuthority);
    }

    private bool ShouldSendMammothStateNow(float now)
    {
        if (!IsLocalMammothAuthority() || !gameStarted || gameSocket == null || !gameSocket.IsOpen)
        {
            return false;
        }

        EnemyHealth mammoth = GetCachedMammothEnemy();
        if (mammoth == null || mammoth.IsDead)
        {
            return false;
        }

        if (!hasSentInitialMammothState)
        {
            return true;
        }

        if (now - lastMammothStateSendTime >= MammothForcedStateSendInterval)
        {
            return true;
        }

        Transform mammothTransform = mammoth.transform;
        if ((mammothTransform.position - lastSentMammothPosition).sqrMagnitude >= MinPositionDeltaSqr)
        {
            return true;
        }

        return Quaternion.Angle(Quaternion.Euler(lastSentMammothEulerAngles), mammothTransform.rotation) >= MinRotationDelta;
    }

    private void SendMammothStateUpdate()
    {
        EnemyHealth mammoth = GetCachedMammothEnemy();
        if (mammoth == null)
        {
            return;
        }

        MammothStateDto state = MammothStateDto.FromEnemyHealth(
            currentGameLobbyId,
            currentUser != null ? currentUser.id : 0,
            mammoth
        );
        gameSocket.SendJson(JsonUtility.ToJson(state));

        hasSentInitialMammothState = true;
        lastMammothStateSendTime = Time.unscaledTime;
        lastSentMammothPosition = mammoth.transform.position;
        lastSentMammothEulerAngles = mammoth.transform.eulerAngles;
    }

    private void UpdateRemoteMammothPose()
    {
        if (IsLocalMammothAuthority() || !hasRemoteMammothPose)
        {
            return;
        }

        EnemyHealth mammoth = GetCachedMammothEnemy();
        if (mammoth == null || mammoth.IsDead)
        {
            return;
        }

        float step = Mathf.Clamp01(Time.unscaledDeltaTime * MammothRemoteLerpSpeed);
        Transform mammothTransform = mammoth.transform;
        mammothTransform.position = Vector3.Lerp(mammothTransform.position, targetRemoteMammothPosition, step);
        mammothTransform.rotation = Quaternion.Slerp(mammothTransform.rotation, targetRemoteMammothRotation, step);
    }

    private void RefreshGameHud()
    {
        string rtt = lastGameRttMs >= 0f ? $"{lastGameRttMs:0} ms" : "measuring";
        string lastRemote = lastRemoteStateReceiveTime >= 0f
            ? $"{Mathf.Max(0f, (Time.unscaledTime - lastRemoteStateReceiveTime) * 1000f):0} ms ago"
            : "none yet";

        SetText(
            gameStatusText,
            $"WASD move, Space jump\nSocket RTT: {rtt} | Remote states: {remoteStatesPerSecond}/s | Last remote: {lastRemote}\nRelay: direct Daphne process, up to 30 Hz\nDBG {debugClientTag}: gameMsg={gameMessagesReceived}, applied={remoteStatesApplied}, spawned={remoteStatesSpawned}, droppedLocal={remoteStatesDroppedAsLocal}");
    }

    public static void NotifyEnemyDamaged(EnemyHealth enemyHealth, int damage)
    {
        if (Instance == null)
        {
            return;
        }

        Instance.SendMammothHealthUpdate(enemyHealth, damage);
    }

    public static bool ShouldDeferEnemyDeath(EnemyHealth enemyHealth)
    {
        return Instance != null && Instance.gameStarted && IsMammothEnemy(enemyHealth);
    }

    private void SendMammothHealthUpdate(EnemyHealth enemyHealth, int damage)
    {
        if (!gameStarted || gameSocket == null || !gameSocket.IsOpen || enemyHealth == null || !IsMammothEnemy(enemyHealth))
        {
            return;
        }

        MammothHealthDto update = MammothHealthDto.FromEnemyHealth(currentGameLobbyId, enemyHealth, damage);
        gameSocket.SendJson(JsonUtility.ToJson(update));
    }

    private void TryApplyMammothState(MammothStateDto mammothState)
    {
        if (mammothState == null)
        {
            return;
        }

        EnemyHealth mammoth = GetCachedMammothEnemy();
        if (mammoth == null)
        {
            pendingMammothState = mammothState;
            return;
        }

        pendingMammothState = null;
        TryConfigureMammothRuntime();
        mammoth.ApplyNetworkHealth(mammothState.currentHealth, mammothState.maxHealth);

        if (IsLocalMammothAuthority())
        {
            return;
        }

        targetRemoteMammothPosition = MultiplayerJson.ArrayToVector(mammothState.position);
        targetRemoteMammothRotation = Quaternion.Euler(MultiplayerJson.ArrayToVector(mammothState.rotation));
        hasRemoteMammothPose = true;

        Transform mammothTransform = mammoth.transform;
        if ((mammothTransform.position - targetRemoteMammothPosition).sqrMagnitude > 100f)
        {
            mammothTransform.position = targetRemoteMammothPosition;
        }
    }

    private void TryApplyMammothHealth(MammothHealthDto mammothHealth)
    {
        if (mammothHealth == null)
        {
            return;
        }

        EnemyHealth mammoth = GetCachedMammothEnemy();
        if (mammoth == null)
        {
            pendingMammothHealth = mammothHealth;
            return;
        }

        pendingMammothHealth = null;
        mammoth.ApplyNetworkHealth(mammothHealth.currentHealth, mammothHealth.maxHealth);
    }

    private static EnemyHealth FindMammothEnemy()
    {
        EnemyHealth[] enemies = FindObjectsByType<EnemyHealth>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (EnemyHealth enemy in enemies)
        {
            if (IsMammothEnemy(enemy))
            {
                return enemy;
            }
        }

        return null;
    }

    private static bool IsMammothEnemy(EnemyHealth enemyHealth)
    {
        if (enemyHealth == null)
        {
            return false;
        }

        string enemyName = enemyHealth.gameObject.name;
        return enemyName.IndexOf("Mammoth", StringComparison.OrdinalIgnoreCase) >= 0
            || enemyName.IndexOf("Mamoth", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void ResetStateSendTracking()
    {
        stateSeq = 0;
        nextStateSendTime = 0f;
        lastStateSendTime = 0f;
        hasSentInitialState = false;
        lastSentPosition = Vector3.zero;
        lastSentEulerAngles = Vector3.zero;
        nextMammothStateSendTime = 0f;
        lastMammothStateSendTime = 0f;
        hasSentInitialMammothState = false;
        lastSentMammothPosition = Vector3.zero;
        lastSentMammothEulerAngles = Vector3.zero;
        hasRemoteMammothPose = false;
        targetRemoteMammothPosition = Vector3.zero;
        targetRemoteMammothRotation = Quaternion.identity;
        cachedMammothEnemy = null;
        mammothRuntimeConfigured = false;
        nextGamePingTime = 0f;
        nextLobbyPingTime = 0f;
        nextHudRefreshTime = 0f;
        lastGameRttMs = -1f;
        lastRemoteStateReceiveTime = -1f;
        remoteStateRateWindowStart = Time.unscaledTime;
        remoteStatesInWindow = 0;
        remoteStatesPerSecond = 0;
        gameMessagesReceived = 0;
        remoteStatesApplied = 0;
        remoteStatesDroppedAsLocal = 0;
        remoteStatesDroppedInvalid = 0;
        remoteStatesSpawned = 0;
        lastGamePingSendTime = -1f;
        lastGamePongReceiveTime = -1f;
        lastLobbyPingSendTime = -1f;
        lastLobbyPongReceiveTime = -1f;
        lastGameHeartbeatReceiveTime = -1f;
        lastLobbyHeartbeatReceiveTime = -1f;
        gameHeartbeatCloseRequested = false;
        lobbyHeartbeatCloseRequested = false;
        lastGameSocketCloseTime = -1f;
        lastGameSocketCloseGapMs = -1f;
        lastGameEnvelopeType = "none";
        lastGameEnvelopeTime = -1f;
        lastLobbyEnvelopeType = "none";
        lastLobbyEnvelopeTime = -1f;
    }

    private void DumpMultiplayerDebugSnapshot()
    {
        StringBuilder sb = new StringBuilder(256);
        sb.Append("=== Multiplayer Debug Snapshot === ");
        sb.Append("client=").Append(debugClientTag);
        sb.Append(", userId=").Append(currentUser != null ? currentUser.id : 0);
        sb.Append(", localPlayerId=").Append(GetLocalPlayerId() ?? "null");
        sb.Append(", lobbyId=").Append(currentLobby != null ? currentLobby.id : -1);
        sb.Append(", gameLobbyId=").Append(currentGameLobbyId);
        sb.Append(", lobbyMsg=").Append(lobbyMessagesReceived);
        sb.Append(", gameMsg=").Append(gameMessagesReceived);
        sb.Append(", remoteApplied=").Append(remoteStatesApplied);
        sb.Append(", remoteSpawned=").Append(remoteStatesSpawned);
        sb.Append(", remoteDroppedLocal=").Append(remoteStatesDroppedAsLocal);
        sb.Append(", remoteDroppedInvalid=").Append(remoteStatesDroppedInvalid);
        sb.Append(", remoteCubeCount=").Append(remoteCubes.Count);
        sb.Append(", lastLobbyClose=").Append(lastLobbySocketCloseCode);
        sb.Append(", lastGameClose=").Append(lastGameSocketCloseCode);
        sb.Append(", lastPingAgoMs=").Append(lastGamePingSendTime >= 0f ? ((Time.unscaledTime - lastGamePingSendTime) * 1000f).ToString("0") : "n/a");
        sb.Append(", lastPongAgoMs=").Append(lastGamePongReceiveTime >= 0f ? ((Time.unscaledTime - lastGamePongReceiveTime) * 1000f).ToString("0") : "n/a");
        sb.Append(", lobbyPingAgoMs=").Append(lastLobbyPingSendTime >= 0f ? ((Time.unscaledTime - lastLobbyPingSendTime) * 1000f).ToString("0") : "n/a");
        sb.Append(", lobbyPongAgoMs=").Append(lastLobbyPongReceiveTime >= 0f ? ((Time.unscaledTime - lastLobbyPongReceiveTime) * 1000f).ToString("0") : "n/a");
        sb.Append(", lastLobbyMsg=").Append(lastLobbyEnvelopeType).Append("@").Append(FormatAgo(lastLobbyEnvelopeTime));
        sb.Append(", lastGameMsg=").Append(lastGameEnvelopeType).Append("@").Append(FormatAgo(lastGameEnvelopeTime));
        sb.Append(", lastGameCloseGapMs=").Append(lastGameSocketCloseGapMs >= 0f ? lastGameSocketCloseGapMs.ToString("0") : "n/a");
        if (lobbySocket != null)
        {
            sb.Append(", lobbySocket={").Append(lobbySocket.GetDebugSnapshot()).Append("}");
        }
        if (gameSocket != null)
        {
            sb.Append(", gameSocket={").Append(gameSocket.GetDebugSnapshot()).Append("}");
        }
        Debug.Log(sb.ToString());
    }

    private string DescribeGameStarted(GameStartedDto start)
    {
        if (start?.players == null)
        {
            return "game_started payload missing players.";
        }

        StringBuilder sb = new StringBuilder(128);
        sb.Append("game_started players=");
        for (int i = 0; i < start.players.Length; i++)
        {
            GameStartedPlayerDto p = start.players[i];
            if (p == null)
            {
                continue;
            }

            if (i > 0)
            {
                sb.Append(" | ");
            }

            sb.Append("{uid=").Append(p.userId)
              .Append(", pid=").Append(p.playerId)
              .Append(", slot=").Append(p.slot)
              .Append("}");
        }

        return sb.ToString();
    }

    private void NetLog(string message, bool warning = false)
    {
        if (!verboseNetworkingLogs)
        {
            return;
        }

        string formatted = $"[MP:{debugClientTag}] {message}";
        if (warning)
        {
            Debug.LogWarning(formatted);
        }
        else
        {
            Debug.Log(formatted);
        }
    }

    private void LogSocketTrace(string prefix, CaveGameSocketClient socket, bool warning, bool intentionalClose, string reason)
    {
        if (socket == null)
        {
            NetLog(prefix + ": socket=null", warning);
            return;
        }

        NetLog($"{prefix}: intentional={intentionalClose}, reason={reason}, {socket.GetDebugSnapshot()}", warning);
    }

    private void MarkExpectedLobbyClose(string reason)
    {
        lobbyCloseExpected = true;
        lobbyCloseExpectedReason = reason;
    }

    private void MarkExpectedGameClose(string reason)
    {
        gameCloseExpected = true;
        gameCloseExpectedReason = reason;
    }

    private bool TryConsumeExpectedLobbyClose(out string reason)
    {
        reason = lobbyCloseExpectedReason;
        if (!lobbyCloseExpected)
        {
            return false;
        }

        lobbyCloseExpected = false;
        lobbyCloseExpectedReason = "none";
        return true;
    }

    private bool TryConsumeExpectedGameClose(out string reason)
    {
        reason = gameCloseExpectedReason;
        if (!gameCloseExpected)
        {
            return false;
        }

        gameCloseExpected = false;
        gameCloseExpectedReason = "none";
        return true;
    }

    private static bool TryExtractCloseCode(string closeCode, out int numericCode)
    {
        numericCode = -1;
        if (string.IsNullOrWhiteSpace(closeCode))
        {
            return false;
        }

        int i = 0;
        while (i < closeCode.Length && char.IsWhiteSpace(closeCode[i]))
        {
            i++;
        }

        int start = i;
        while (i < closeCode.Length && char.IsDigit(closeCode[i]))
        {
            i++;
        }

        if (i <= start)
        {
            return false;
        }

        return int.TryParse(closeCode.Substring(start, i - start), out numericCode);
    }

    private static string FormatAgo(float timestamp)
    {
        if (timestamp < 0f)
        {
            return "n/a";
        }

        return $"{Mathf.Max(0f, (Time.unscaledTime - timestamp) * 1000f):0}ms";
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

            if (member.userId > 0)
            {
                playerSlotsById[$"player_{member.userId}"] = member.slot;
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

            if (player.userId > 0)
            {
                playerSlotsById[$"player_{player.userId}"] = player.slot;
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

    private Vector3 SpawnForPlayer(int slot, int userId)
    {
        Vector3 baseSpawn = SpawnForSlot(slot);
        if (userId == 0)
        {
            return runtimeSpawnAnchor + baseSpawn;
        }

        // Secondary separation in case backend sends duplicate/invalid slots.
        int hash = Mathf.Abs(userId);
        float offsetX = ((hash % 3) - 1) * 6f;
        float offsetZ = (((hash / 3) % 3) - 1) * 6f;
        return runtimeSpawnAnchor + baseSpawn + new Vector3(offsetX, 0f, offsetZ);
    }

    private Vector3 ResolveSafeSpawnPosition(int slot, int userId, string context)
    {
        Vector3 candidate = SpawnForPlayer(slot, userId);
        if (TrySampleNavMeshSpawn(candidate, out Vector3 navMeshPosition))
        {
            return navMeshPosition;
        }

        if (TryResolveGroundHeight(candidate, out float groundHeight))
        {
            Vector3 grounded = new Vector3(candidate.x, groundHeight + SpawnHeightOffset, candidate.z);
            NetLog($"Ground-height spawn fallback ({context}) at {grounded}");
            return grounded;
        }

        float safeY = Mathf.Max(candidate.y + SpawnHeightOffset, runtimeSpawnAnchor.y + SpawnHeightOffset, SpawnHeightOffset);
        Vector3 fallback = new Vector3(candidate.x, safeY, candidate.z);
        NetLog($"Final spawn fallback ({context}) at {fallback}", true);
        return fallback;
    }

    private static bool TrySampleNavMeshSpawn(Vector3 nearPosition, out Vector3 safePosition)
    {
        Vector3 probe = nearPosition + Vector3.up * SpawnNavMeshProbeHeight;
        if (NavMesh.SamplePosition(probe, out NavMeshHit hit, SpawnNavMeshSampleRadius, NavMesh.AllAreas))
        {
            safePosition = hit.position + Vector3.up * SpawnHeightOffset;
            return true;
        }

        safePosition = Vector3.zero;
        return false;
    }

    private static bool TryResolveGroundHeight(Vector3 nearPosition, out float groundHeight)
    {
        Vector3 rayOrigin = nearPosition + Vector3.up * SpawnRaycastHeight;
        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, SpawnRaycastDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            groundHeight = hit.point.y;
            return true;
        }

        Terrain terrain = Terrain.activeTerrain;
        if (terrain != null)
        {
            groundHeight = terrain.SampleHeight(nearPosition) + terrain.transform.position.y;
            return true;
        }

        groundHeight = 0f;
        return false;
    }

    private static string BuildPlayerKey(string playerId, int userId)
    {
        if (!string.IsNullOrWhiteSpace(playerId))
        {
            return playerId;
        }

        if (userId > 0)
        {
            return $"player_{userId}";
        }

        return "player_0";
    }

    private GameObject CreatePlayerCube(string objectName, Vector3 position, Color color, bool isLocal)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = objectName;
        cube.transform.SetParent(worldRoot.transform);
        cube.transform.position = position;
        SetRendererColor(cube, color);

        Rigidbody body = cube.GetComponent<Rigidbody>();
        if (body == null)
        {
            body = cube.AddComponent<Rigidbody>();
        }

        body.freezeRotation = true;
        if (isLocal)
        {
            body.isKinematic = false;
            body.useGravity = true;
            if (cube.GetComponent<LocalCubeController>() == null)
            {
                cube.AddComponent<LocalCubeController>();
            }
        }
        else
        {
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            if (cube.GetComponent<RemoteCubeController>() == null)
            {
                cube.AddComponent<RemoteCubeController>();
            }
        }

        return cube;
    }

    private Vector3 ResolveRuntimeSpawnAnchor()
    {
        GameObject[] roots = FindObjectsByType<GameObject>(FindObjectsInactive.Exclude);
        foreach (GameObject root in roots)
        {
            if (root == null || !root.activeInHierarchy)
            {
                continue;
            }

            if (!string.Equals(root.name, "Player", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (root.GetComponent<LocalCubeController>() != null || root.transform.IsChildOf(transform))
            {
                continue;
            }

            Vector3 anchor = root.transform.position;
            root.SetActive(false);
            NetLog($"Disabled scene Player object at {anchor} and using it as multiplayer spawn anchor.");
            return new Vector3(anchor.x, Mathf.Max(anchor.y, 0.5f), anchor.z);
        }

        Vector3 fallback = Vector3.zero;
        if (Camera.main != null)
        {
            fallback = Camera.main.transform.position;
        }

        if (TrySampleNavMeshSpawn(fallback, out Vector3 navMeshFallback))
        {
            NetLog($"No active scene Player found; using NavMesh fallback anchor at {navMeshFallback}.");
            return navMeshFallback;
        }

        if (TryResolveGroundHeight(fallback, out float groundHeight))
        {
            Vector3 groundedFallback = new Vector3(fallback.x, groundHeight + SpawnHeightOffset, fallback.z);
            NetLog($"No active scene Player found; using ground fallback anchor at {groundedFallback}.");
            return groundedFallback;
        }

        Vector3 finalFallback = new Vector3(fallback.x, Mathf.Max(fallback.y, SpawnHeightOffset), fallback.z);
        NetLog($"No active scene Player found; using default fallback anchor at {finalFallback}.", true);
        return finalFallback;
    }

    private static bool IsSocketCloseWarning(string closeCode)
    {
        if (TryExtractCloseCode(closeCode, out int numericCode))
        {
            return numericCode != 1000;
        }

        return !string.Equals(closeCode, "Normal", StringComparison.OrdinalIgnoreCase);
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
    [SerializeField] private float aimRotationSpeed = 18f;

    [Header("Combat Setup")]
    [SerializeField] private Vector3 weaponHolderLocalPosition = new Vector3(0.6f, 0.3f, 0.8f);
    [SerializeField] private Vector3 attackPointLocalPosition = new Vector3(0f, 0.5f, 1.4f);
    [SerializeField] private float pickupRangeRadius = 1.5f;

    private Rigidbody body;
    private Transform cameraTransform;
    private bool isGrounded = true;
    private Vector3 previousPosition;
    private PlayerMouseAim mouseAim;

    public Vector3 Velocity { get; private set; }

    public void Setup(Transform cameraTransform)
    {
        this.cameraTransform = cameraTransform;
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        previousPosition = transform.position;
        mouseAim = GetComponent<PlayerMouseAim>();

        SetupCombat();
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
        if (mouseAim == null)
        {
            mouseAim = GetComponent<PlayerMouseAim>();
        }

        Keyboard keyboard = Keyboard.current;
        Vector2 input = Vector2.zero;
        bool isAimLocked = mouseAim != null && mouseAim.IsAimModifierPressed;

        if (keyboard != null)
        {
            if (!isAimLocked)
            {
                if (keyboard.wKey.isPressed) input.y += 1f;
                if (keyboard.sKey.isPressed) input.y -= 1f;
                if (keyboard.dKey.isPressed) input.x += 1f;
                if (keyboard.aKey.isPressed) input.x -= 1f;
            }

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

        if (isAimLocked && mouseAim != null && mouseAim.TryGetAimDirection(out Vector3 aimDirection, false))
        {
            Quaternion targetRotation = Quaternion.LookRotation(aimDirection, Vector3.up);
            body.MoveRotation(
                Quaternion.Slerp(
                    body.rotation,
                    targetRotation,
                    aimRotationSpeed * Time.fixedDeltaTime
                )
            );
        }
        else if (movement.sqrMagnitude > 0.001f)
        {
            body.MoveRotation(Quaternion.LookRotation(movement));
        }

        Velocity = (transform.position - previousPosition) / Time.fixedDeltaTime;
        previousPosition = transform.position;
    }

    private void SetupCombat()
    {
        Transform weaponHolder = CreateChildIfMissing(
            "WeaponHolder",
            weaponHolderLocalPosition
        );

        Transform attackPoint = CreateChildIfMissing(
            "AttackPoint",
            attackPointLocalPosition
        );

        Transform pickupRange = CreateChildIfMissing(
            "PickupRange",
            Vector3.zero
        );

        SphereCollider pickupCollider = pickupRange.GetComponent<SphereCollider>();

        if (pickupCollider == null)
        {
            pickupCollider = pickupRange.gameObject.AddComponent<SphereCollider>();
        }

        pickupCollider.isTrigger = true;
        pickupCollider.radius = pickupRangeRadius;

        PlayerWeaponPickup weaponPickup = GetComponent<PlayerWeaponPickup>();

        if (weaponPickup == null)
        {
            weaponPickup = gameObject.AddComponent<PlayerWeaponPickup>();
        }

        if (mouseAim == null)
        {
            mouseAim = GetComponent<PlayerMouseAim>();
        }

        if (mouseAim == null)
        {
            mouseAim = gameObject.AddComponent<PlayerMouseAim>();
        }

        weaponPickup.Initialize(weaponHolder);

        PlayerCombat combat = GetComponent<PlayerCombat>();

        if (combat == null)
        {
            combat = gameObject.AddComponent<PlayerCombat>();
        }

        int enemyLayerMask = LayerMask.GetMask("Enemy");

        if (enemyLayerMask == 0)
        {
            Debug.LogWarning("Enemy layer was not found. Create a layer named Enemy and assign it to your enemy.");
        }

        combat.Initialize(weaponPickup, attackPoint, enemyLayerMask);

        PlayerHealth health = GetComponent<PlayerHealth>();

        if (health == null)
        {
            health = gameObject.AddComponent<PlayerHealth>();
        }

        gameObject.tag = "Player";

        Debug.Log("Combat setup added to local multiplayer player with PlayerHealth.");
    }

    private Transform CreateChildIfMissing(string childName, Vector3 localPosition)
    {
        Transform existing = transform.Find(childName);

        if (existing != null)
        {
            existing.localPosition = localPosition;
            existing.localRotation = Quaternion.identity;
            existing.localScale = Vector3.one;
            return existing;
        }

        GameObject child = new GameObject(childName);
        child.transform.SetParent(transform);
        child.transform.localPosition = localPosition;
        child.transform.localRotation = Quaternion.identity;
        child.transform.localScale = Vector3.one;

        return child.transform;
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
