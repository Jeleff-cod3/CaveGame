// Starts the Unity WebRTC runtime update loop once for proximity voice.
// Used by: VoicePeerManager or a manually placed scene object.
using System;
using System.Collections;
using Unity.WebRTC;
using UnityEngine;

public sealed class VoiceWebRtcBootstrap : MonoBehaviour
{
    private static VoiceWebRtcBootstrap activeInstance;
    private static bool webRtcRuntimeStarted;
    private static bool applicationIsQuitting;

    [SerializeField] private VoiceChatConfig config = new VoiceChatConfig();

    private Coroutine webRtcUpdateCoroutine;

    public static bool IsStarted => webRtcRuntimeStarted;
    public VoiceChatConfig Config => config;

    public static VoiceWebRtcBootstrap EnsureExists()
    {
        if (activeInstance != null)
        {
            return activeInstance;
        }

        VoiceWebRtcBootstrap existing = UnityEngine.Object.FindAnyObjectByType<VoiceWebRtcBootstrap>();
        if (existing != null)
        {
            return existing;
        }

        GameObject bootstrapObject = new GameObject("Voice WebRTC Bootstrap");
        return bootstrapObject.AddComponent<VoiceWebRtcBootstrap>();
    }

    private void Awake()
    {
        applicationIsQuitting = false;

        if (activeInstance != null && activeInstance != this)
        {
            LogDuplicateInstance();
            Destroy(gameObject);
            return;
        }

        activeInstance = this;
        if (transform.parent != null)
        {
            transform.SetParent(null);
        }

        DontDestroyOnLoad(gameObject);
        StartWebRtcRuntimeIfNeeded();
    }

    private void OnApplicationQuit()
    {
        applicationIsQuitting = true;
        StopWebRtcRuntimeIfOwned();
    }

    private void OnDestroy()
    {
        if (activeInstance == this)
        {
            StopWebRtcRuntimeIfOwned();
            activeInstance = null;
        }
    }

    private void StartWebRtcRuntimeIfNeeded()
    {
        if (webRtcRuntimeStarted)
        {
            LogDebug("Unity WebRTC runtime was already started.");
            return;
        }

        try
        {
            WebRTC.ConfigureNativeLogging(DebugLoggingEnabled(), NativeLoggingSeverity.Warning);
            webRtcUpdateCoroutine = StartCoroutine(RunWebRtcUpdateLoop());
            webRtcRuntimeStarted = true;
            Debug.Log("VoiceWebRtcBootstrap started Unity WebRTC update loop.");
        }
        catch (Exception exception)
        {
            webRtcRuntimeStarted = false;
            Debug.LogError($"VoiceWebRtcBootstrap failed to start Unity WebRTC: {exception.Message}");
        }
    }

    private IEnumerator RunWebRtcUpdateLoop()
    {
        IEnumerator update = WebRTC.Update();
        while (!applicationIsQuitting)
        {
            object current;
            try
            {
                if (!update.MoveNext())
                {
                    break;
                }

                current = update.Current;
            }
            catch (Exception exception)
            {
                webRtcRuntimeStarted = false;
                Debug.LogError($"VoiceWebRtcBootstrap WebRTC update loop failed: {exception.Message}");
                yield break;
            }

            yield return current;
        }
    }

    private void StopWebRtcRuntimeIfOwned()
    {
        if (webRtcUpdateCoroutine != null)
        {
            StopCoroutine(webRtcUpdateCoroutine);
            webRtcUpdateCoroutine = null;
        }

        if (webRtcRuntimeStarted)
        {
            webRtcRuntimeStarted = false;
            Debug.Log("VoiceWebRtcBootstrap stopped Unity WebRTC update loop.");
        }
    }

    private void LogDuplicateInstance()
    {
        if (DebugLoggingEnabled())
        {
            Debug.Log("VoiceWebRtcBootstrap duplicate instance destroyed; existing WebRTC update loop remains active.");
        }
    }

    private void LogDebug(string message)
    {
        if (DebugLoggingEnabled())
        {
            Debug.Log(message);
        }
    }

    private bool DebugLoggingEnabled()
    {
        return config != null && config.debugLogging;
    }
}
