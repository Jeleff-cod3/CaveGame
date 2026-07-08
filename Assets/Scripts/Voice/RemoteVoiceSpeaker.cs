// Manages spatial playback for one remote player's incoming WebRTC audio track.
// Used by: WebRtcVoicePeer.
using System;
using Unity.WebRTC;
using UnityEngine;

public sealed class RemoteVoiceSpeaker : IDisposable
{
    private readonly string playerId;
    private readonly VoiceChatConfig config;
    private GameObject speakerObject;
    private AudioSource audioSource;
    private AudioStreamTrack remoteTrack;
    private float currentGain = 1f;
    private float currentDistance;
    private bool muted;

    public RemoteVoiceSpeaker(string playerId, Transform remotePlayerTransform, VoiceChatConfig config)
    {
        this.playerId = playerId;
        this.config = config ?? new VoiceChatConfig();
        AttachTo(remotePlayerTransform);
    }

    public void AttachTo(Transform remotePlayerTransform)
    {
        if (remotePlayerTransform == null)
        {
            return;
        }

        if (speakerObject == null)
        {
            speakerObject = new GameObject("Voice Speaker " + playerId);
            audioSource = speakerObject.AddComponent<AudioSource>();
            ConfigureAudioSource();
        }

        speakerObject.transform.SetParent(remotePlayerTransform, false);
        speakerObject.transform.localPosition = new Vector3(0f, 1.6f, 0f);
        speakerObject.transform.localRotation = Quaternion.identity;
        speakerObject.transform.localScale = Vector3.one;
    }

    public void AttachTrack(AudioStreamTrack track)
    {
        if (track == null || audioSource == null)
        {
            return;
        }

        remoteTrack = track;
        audioSource.SetTrack(track);
        audioSource.loop = true;
        audioSource.Play();
        ApplyVolume();
    }

    public void ApplyInterest(VoicePeerInterestDto interest)
    {
        if (interest == null)
        {
            return;
        }

        currentDistance = Mathf.Max(0f, interest.distance);
        currentGain = Mathf.Clamp01(interest.gain);
        muted = interest.isMuted || currentDistance > Mathf.Max(0f, config.maxVoiceDistance);
        ApplyVolume();
    }

    public void ApplyGain(float gain, bool isMuted)
    {
        currentGain = Mathf.Clamp01(gain);
        muted = isMuted;
        ApplyVolume();
    }

    public void SetMuted(bool isMuted)
    {
        muted = isMuted;
        ApplyVolume();
    }

    public void Dispose()
    {
        remoteTrack = null;
        if (audioSource != null)
        {
            audioSource.Stop();
        }

        if (speakerObject != null)
        {
            UnityEngine.Object.Destroy(speakerObject);
            speakerObject = null;
            audioSource = null;
        }
    }

    private void ConfigureAudioSource()
    {
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;
        audioSource.rolloffMode = AudioRolloffMode.Linear;
        audioSource.minDistance = Mathf.Max(0f, config.minVoiceDistance);
        audioSource.maxDistance = Mathf.Max(audioSource.minDistance, config.maxVoiceDistance);
        audioSource.dopplerLevel = 0f;
        ApplyVolume();
    }

    private void ApplyVolume()
    {
        if (audioSource != null)
        {
            audioSource.volume = muted ? 0f : Mathf.Clamp01(currentGain * EffectiveRemoteVoiceVolume());
        }
    }

    private float EffectiveRemoteVoiceVolume()
    {
        if (config == null || config.remoteVoiceVolume <= 0f)
        {
            return 1.75f;
        }

        return config.remoteVoiceVolume;
    }
}
