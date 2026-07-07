// Captures the default Unity microphone and exposes it as a WebRTC audio track.
// Used by: VoicePeerManager and WebRtcVoicePeer.
using System;
using Unity.WebRTC;
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public sealed class LocalVoiceSource : MonoBehaviour
{
    [SerializeField] private int sampleRate = 48000;
    [SerializeField] private int microphoneClipLengthSeconds = 1;

    private AudioSource microphoneSource;
    private VoiceChatConfig config;
    private Action<string> log;
    private string activeMicrophoneDevice;
    private AudioStreamTrack track;

    public AudioStreamTrack Track => track;
    public bool IsCapturing => track != null;
    public string ActiveMicrophoneDevice => activeMicrophoneDevice;

    private void Awake()
    {
        microphoneSource = GetComponent<AudioSource>();
        ConfigureMicrophoneSource();
    }

    public void Configure(VoiceChatConfig voiceConfig, Action<string> debugLogger)
    {
        config = voiceConfig;
        log = debugLogger;
    }

    public bool StartCapture()
    {
        if (track != null)
        {
            return true;
        }

        if (Microphone.devices == null || Microphone.devices.Length == 0)
        {
            Debug.LogWarning("LocalVoiceSource could not start because no microphone devices were found.");
            return false;
        }

        activeMicrophoneDevice = Microphone.devices[0];
        AudioClip microphoneClip = Microphone.Start(
            activeMicrophoneDevice,
            true,
            Mathf.Max(1, microphoneClipLengthSeconds),
            Mathf.Max(8000, sampleRate)
        );
        if (microphoneClip == null)
        {
            Debug.LogWarning($"LocalVoiceSource could not start microphone device {activeMicrophoneDevice}.");
            activeMicrophoneDevice = null;
            return false;
        }

        microphoneSource.clip = microphoneClip;
        microphoneSource.Play();

        track = new AudioStreamTrack(microphoneSource)
        {
            Loopback = false,
        };

        LogDebug($"LocalVoiceSource started microphone capture from {activeMicrophoneDevice}.");
        return true;
    }

    public void StopCapture()
    {
        bool wasCapturing = track != null || !string.IsNullOrWhiteSpace(activeMicrophoneDevice);
        if (track != null)
        {
            track.Dispose();
            track = null;
        }

        if (microphoneSource != null)
        {
            microphoneSource.Stop();
            microphoneSource.clip = null;
        }

        if (!string.IsNullOrWhiteSpace(activeMicrophoneDevice) && Microphone.IsRecording(activeMicrophoneDevice))
        {
            Microphone.End(activeMicrophoneDevice);
        }

        activeMicrophoneDevice = null;
        if (wasCapturing)
        {
            LogDebug("LocalVoiceSource stopped microphone capture.");
        }
    }

    private void OnDestroy()
    {
        StopCapture();
    }

    private void ConfigureMicrophoneSource()
    {
        microphoneSource.playOnAwake = false;
        microphoneSource.loop = true;
        microphoneSource.spatialBlend = 0f;
        microphoneSource.volume = 1f;
        microphoneSource.mute = false;
    }

    private void LogDebug(string message)
    {
        if (config != null && config.debugLogging)
        {
            log?.Invoke(message);
        }
    }
}
