// Defines JsonUtility-friendly DTOs for proximity voice signaling over the game socket.
// Used by: VoiceSignalingController, VoicePeerManager, and WebRtcVoicePeer.
using System;

[Serializable]
public sealed class VoiceInterestSnapshotDto
{
    public string type = VoiceMessageTypes.VoiceInterestSnapshot;
    public int lobbyId;
    public string selfPlayerId;
    public VoicePeerInterestDto[] audiblePeers;
    public double serverTime;
}

[Serializable]
public sealed class VoicePeerInterestDto
{
    public string playerId;
    public float distance;
    public float gain;
    public bool isMuted;
}

[Serializable]
public sealed class WebRtcOfferDto
{
    public string type = VoiceMessageTypes.WebRtcOffer;
    public int lobbyId;
    public string targetPlayerId;
    public string fromPlayerId;
    public string sdpType = "offer";
    public string sdp;
}

[Serializable]
public sealed class WebRtcAnswerDto
{
    public string type = VoiceMessageTypes.WebRtcAnswer;
    public int lobbyId;
    public string targetPlayerId;
    public string fromPlayerId;
    public string sdpType = "answer";
    public string sdp;
}

[Serializable]
public sealed class WebRtcIceCandidateDto
{
    public string type = VoiceMessageTypes.WebRtcIce;
    public int lobbyId;
    public string targetPlayerId;
    public string fromPlayerId;
    public string candidate;
    public string sdpMid;
    public int sdpMLineIndex;
}

[Serializable]
public sealed class VoicePeerLeftDto
{
    public string type = VoiceMessageTypes.VoicePeerLeft;
    public int lobbyId;
    public string playerId;
    public int userId;
}

[Serializable]
public sealed class VoiceReadyDto
{
    public string type = VoiceMessageTypes.VoicePresence;
    public bool isReady = true;
    public bool isMuted;
}

public static class VoiceMessageTypes
{
    public const string VoiceInterestSnapshot = "voice_interest_snapshot";
    public const string VoicePeerLeft = "voice_peer_left";
    public const string VoicePresence = "voice_presence";
    public const string WebRtcAnswer = "webrtc_answer";
    public const string WebRtcIce = "webrtc_ice";
    public const string WebRtcOffer = "webrtc_offer";

    public static bool IsVoiceMessage(string type)
    {
        return string.Equals(type, VoiceInterestSnapshot, StringComparison.Ordinal)
            || string.Equals(type, VoicePeerLeft, StringComparison.Ordinal)
            || string.Equals(type, VoicePresence, StringComparison.Ordinal)
            || string.Equals(type, WebRtcAnswer, StringComparison.Ordinal)
            || string.Equals(type, WebRtcIce, StringComparison.Ordinal)
            || string.Equals(type, WebRtcOffer, StringComparison.Ordinal);
    }
}
