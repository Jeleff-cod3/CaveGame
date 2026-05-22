using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public sealed class CaveGameApiClient
{
    private readonly string baseHttpUrl;
    private readonly Func<string> tokenProvider;

    public CaveGameApiClient(string baseHttpUrl, Func<string> tokenProvider)
    {
        this.baseHttpUrl = NormalizeBaseUrl(baseHttpUrl);
        this.tokenProvider = tokenProvider;
    }

    public string BuildWebSocketUrl(string path)
    {
        string wsBase = baseHttpUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? "wss://" + baseHttpUrl.Substring("https://".Length)
            : "ws://" + baseHttpUrl.Substring("http://".Length);

        string token = tokenProvider?.Invoke();
        string separator = path.Contains("?") ? "&" : "?";
        return $"{wsBase}{path}{separator}token={UnityWebRequest.EscapeURL(token ?? string.Empty)}";
    }

    public IEnumerator CreateGuest(Action<ApiResult<TokenResponseDto>> onComplete)
    {
        yield return Post("/api/accounts/guest/", "{}", false, onComplete);
    }

    public IEnumerator CreateLobby(int maxPlayers, Action<ApiResult<LobbyDto>> onComplete)
    {
        string body = JsonUtility.ToJson(new CreateLobbyRequestDto { maxPlayers = maxPlayers });
        yield return Post("/api/lobbies/create/", body, true, onComplete);
    }

    public IEnumerator JoinLobby(string code, Action<ApiResult<JoinLobbyResponseDto>> onComplete)
    {
        string safeCode = UnityWebRequest.EscapeURL((code ?? string.Empty).Trim().ToUpperInvariant());
        yield return Post($"/api/lobbies/{safeCode}/join/", "{}", true, onComplete);
    }

    public IEnumerator SetReady(int lobbyId, bool isReady, Action<ApiResult<LobbyEventDto>> onComplete)
    {
        string body = JsonUtility.ToJson(new ReadyRequestDto { isReady = isReady });
        yield return Post($"/api/lobbies/{lobbyId}/ready/", body, true, onComplete);
    }

    public IEnumerator StartLobby(int lobbyId, Action<ApiResult<GameStartedDto>> onComplete)
    {
        string body = JsonUtility.ToJson(new StartLobbyRequestDto());
        yield return Post($"/api/lobbies/{lobbyId}/start/", body, true, onComplete);
    }

    public IEnumerator GetLobby(int lobbyId, Action<ApiResult<LobbyDto>> onComplete)
    {
        yield return Request("GET", $"/api/lobbies/{lobbyId}/", null, true, onComplete);
    }

    private IEnumerator Post<T>(string path, string body, bool requiresAuth, Action<ApiResult<T>> onComplete)
    {
        yield return Request("POST", path, body, requiresAuth, onComplete);
    }

    private IEnumerator Request<T>(string method, string path, string body, bool requiresAuth, Action<ApiResult<T>> onComplete)
    {
        using (UnityWebRequest request = new UnityWebRequest(baseHttpUrl + path, method))
        {
            request.downloadHandler = new DownloadHandlerBuffer();

            if (body != null)
            {
                byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
                request.uploadHandler = new UploadHandlerRaw(bodyBytes);
                request.SetRequestHeader("Content-Type", "application/json");
            }

            if (requiresAuth)
            {
                string token = tokenProvider?.Invoke();
                if (!string.IsNullOrWhiteSpace(token))
                {
                    request.SetRequestHeader("Authorization", "Token " + token);
                }
            }

            yield return request.SendWebRequest();

            string responseText = request.downloadHandler?.text;
            bool success = request.result == UnityWebRequest.Result.Success;
            if (!success)
            {
                string fallback = $"{method} {path} failed ({request.responseCode})";
                onComplete(ApiResult<T>.Failure(MultiplayerJson.ExtractError(responseText, fallback)));
                yield break;
            }

            try
            {
                T response = JsonUtility.FromJson<T>(responseText);
                onComplete(ApiResult<T>.Success(response));
            }
            catch (ArgumentException exception)
            {
                onComplete(ApiResult<T>.Failure($"Could not parse server response: {exception.Message}"));
            }
        }
    }

    private static string NormalizeBaseUrl(string raw)
    {
        string url = string.IsNullOrWhiteSpace(raw) ? "https://cavegame-production.up.railway.app" : raw.Trim();
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = "http://" + url;
        }

        return url.EndsWith("/") ? url.Substring(0, url.Length - 1) : url;
    }
}
