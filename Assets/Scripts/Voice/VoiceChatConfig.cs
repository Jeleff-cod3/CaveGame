// Holds proximity voice settings for WebRTC voice managers.
// Used by: VoiceWebRtcBootstrap, VoiceSignalingController, and voice peer classes.
using System;
using UnityEngine;

[Serializable]
public sealed class VoiceChatConfig
{
    public bool voiceEnabled = true;
    [Min(0f)] public float maxVoiceDistance = 18f;
    [Min(0f)] public float voiceKeepAliveDistance = 22f;
    [Min(0f)] public float minVoiceDistance = 2f;
    [Min(0.1f)] public float peerReconnectDelaySeconds = 2f;
    public string stunServer = "stun:stun.l.google.com:19302";
    public bool debugLogging;
}
