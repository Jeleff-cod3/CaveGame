// Routes proximity voice signaling messages through the existing game WebSocket.
// Used by: MultiplayerPrototype, VoicePeerManager, and WebRtcVoicePeer.
using System;
using UnityEngine;

public sealed class VoiceSignalingController
{
    private readonly Func<string, Transform> remotePlayerTransformProvider;
    private readonly Action<string> sendJson;
    private readonly VoiceChatConfig config;
    private readonly Action<string> log;

    public VoiceSignalingController(
        string localPlayerId,
        Action<string> sendJson,
        Func<string, Transform> remotePlayerTransformProvider,
        VoiceChatConfig config,
        Action<string> log = null)
    {
        LocalPlayerId = localPlayerId;
        this.sendJson = sendJson;
        this.remotePlayerTransformProvider = remotePlayerTransformProvider;
        this.config = config ?? new VoiceChatConfig();
        this.log = log;
    }

    public event Action<WebRtcOfferDto> OnOfferReceived;
    public event Action<WebRtcAnswerDto> OnAnswerReceived;
    public event Action<WebRtcIceCandidateDto> OnIceReceived;
    public event Action<VoiceInterestSnapshotDto> OnInterestSnapshotReceived;
    public event Action<VoicePeerLeftDto> OnPeerLeft;

    public string LocalPlayerId { get; }

    public bool HandleSocketMessage(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        SocketTypeEnvelopeDto envelope;
        try
        {
            envelope = JsonUtility.FromJson<SocketTypeEnvelopeDto>(json);
        }
        catch (ArgumentException exception)
        {
            LogDebug($"Voice signaling ignored malformed JSON envelope: {exception.Message}");
            return false;
        }

        string messageType = envelope != null ? envelope.type : null;
        if (!VoiceMessageTypes.IsVoiceMessage(messageType))
        {
            return false;
        }

        DispatchVoiceMessage(messageType, json);
        return true;
    }

    public void SendOffer(string targetPlayerId, string sdp)
    {
        if (!CanSendToTarget(targetPlayerId) || string.IsNullOrWhiteSpace(sdp))
        {
            return;
        }

        SendJson(
            new WebRtcOfferDto
            {
                targetPlayerId = targetPlayerId,
                sdpType = "offer",
                sdp = sdp,
            }
        );
    }

    public void SendAnswer(string targetPlayerId, string sdp)
    {
        if (!CanSendToTarget(targetPlayerId) || string.IsNullOrWhiteSpace(sdp))
        {
            return;
        }

        SendJson(
            new WebRtcAnswerDto
            {
                targetPlayerId = targetPlayerId,
                sdpType = "answer",
                sdp = sdp,
            }
        );
    }

    public void SendIce(string targetPlayerId, string candidate, string sdpMid, int sdpMLineIndex)
    {
        if (!CanSendToTarget(targetPlayerId) || string.IsNullOrWhiteSpace(candidate))
        {
            return;
        }

        SendJson(
            new WebRtcIceCandidateDto
            {
                targetPlayerId = targetPlayerId,
                candidate = candidate,
                sdpMid = string.IsNullOrWhiteSpace(sdpMid) ? "0" : sdpMid,
                sdpMLineIndex = Mathf.Max(0, sdpMLineIndex),
            }
        );
    }

    public void SendReady(bool isReady, bool isMuted)
    {
        if (sendJson == null)
        {
            return;
        }

        SendJson(new VoiceReadyDto { isReady = isReady, isMuted = isMuted });
    }

    public Transform GetRemotePlayerTransform(string playerId)
    {
        return remotePlayerTransformProvider?.Invoke(playerId);
    }

    private void DispatchVoiceMessage(string messageType, string json)
    {
        try
        {
            switch (messageType)
            {
                case VoiceMessageTypes.VoiceInterestSnapshot:
                    HandleInterestSnapshot(JsonUtility.FromJson<VoiceInterestSnapshotDto>(json));
                    break;
                case VoiceMessageTypes.WebRtcOffer:
                    HandleOffer(JsonUtility.FromJson<WebRtcOfferDto>(json));
                    break;
                case VoiceMessageTypes.WebRtcAnswer:
                    HandleAnswer(JsonUtility.FromJson<WebRtcAnswerDto>(json));
                    break;
                case VoiceMessageTypes.WebRtcIce:
                    HandleIce(JsonUtility.FromJson<WebRtcIceCandidateDto>(json));
                    break;
                case VoiceMessageTypes.VoicePeerLeft:
                    HandlePeerLeft(JsonUtility.FromJson<VoicePeerLeftDto>(json));
                    break;
            }
        }
        catch (ArgumentException exception)
        {
            LogDebug($"Voice signaling could not parse {messageType}: {exception.Message}");
        }
    }

    private void HandleInterestSnapshot(VoiceInterestSnapshotDto snapshot)
    {
        if (snapshot == null)
        {
            return;
        }

        if (snapshot.audiblePeers == null)
        {
            snapshot.audiblePeers = Array.Empty<VoicePeerInterestDto>();
        }

        LogDebug($"Voice interest snapshot for {snapshot.selfPlayerId}: audible={snapshot.audiblePeers.Length}");
        OnInterestSnapshotReceived?.Invoke(snapshot);
    }

    private void HandleOffer(WebRtcOfferDto offer)
    {
        if (offer == null || string.IsNullOrWhiteSpace(offer.fromPlayerId))
        {
            return;
        }

        LogDebug($"Voice offer received from {offer.fromPlayerId} to {offer.targetPlayerId}.");
        OnOfferReceived?.Invoke(offer);
    }

    private void HandleAnswer(WebRtcAnswerDto answer)
    {
        if (answer == null || string.IsNullOrWhiteSpace(answer.fromPlayerId))
        {
            return;
        }

        LogDebug($"Voice answer received from {answer.fromPlayerId} to {answer.targetPlayerId}.");
        OnAnswerReceived?.Invoke(answer);
    }

    private void HandleIce(WebRtcIceCandidateDto ice)
    {
        if (ice == null || string.IsNullOrWhiteSpace(ice.fromPlayerId))
        {
            return;
        }

        LogDebug($"Voice ICE received from {ice.fromPlayerId} to {ice.targetPlayerId}.");
        OnIceReceived?.Invoke(ice);
    }

    private void HandlePeerLeft(VoicePeerLeftDto peerLeft)
    {
        if (peerLeft == null || string.IsNullOrWhiteSpace(peerLeft.playerId))
        {
            return;
        }

        LogDebug($"Voice peer left: {peerLeft.playerId}.");
        OnPeerLeft?.Invoke(peerLeft);
    }

    private bool CanSendToTarget(string targetPlayerId)
    {
        if (sendJson == null)
        {
            LogDebug("Voice signaling send skipped because no socket sender is available.");
            return false;
        }

        if (!config.voiceEnabled)
        {
            LogDebug("Voice signaling send skipped because voice is disabled.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(LocalPlayerId))
        {
            LogDebug("Voice signaling send skipped because local player id is missing.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(targetPlayerId))
        {
            LogDebug("Voice signaling send skipped because target player id is missing.");
            return false;
        }

        return true;
    }

    private void SendJson<T>(T dto)
    {
        string json = JsonUtility.ToJson(dto);
        sendJson(json);
        LogDebug($"Voice signaling sent {typeof(T).Name}.");
    }

    private void LogDebug(string message)
    {
        if (config.debugLogging)
        {
            log?.Invoke(message);
        }
    }
}
