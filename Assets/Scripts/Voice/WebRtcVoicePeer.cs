// Owns one WebRTC audio peer connection for one remote player.
// Used by: VoicePeerManager.
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.WebRTC;
using UnityEngine;

public sealed class WebRtcVoicePeer : IDisposable
{
    private readonly MonoBehaviour coroutineOwner;
    private readonly string localPlayerId;
    private readonly VoiceSignalingController signaling;
    private readonly LocalVoiceSource localVoiceSource;
    private readonly VoiceChatConfig config;
    private readonly Action<string> log;
    private readonly List<WebRtcIceCandidateDto> pendingIceCandidates = new List<WebRtcIceCandidateDto>();

    private RTCPeerConnection peerConnection;
    private MediaStream sendStream;
    private MediaStream receiveStream;
    private RTCRtpSender localAudioSender;
    private RemoteVoiceSpeaker speaker;
    private bool disposed;
    private bool negotiationStarted;
    private bool negotiationInProgress;
    private bool remoteDescriptionSet;
    private bool hasRemoteAudio;

    public WebRtcVoicePeer(
        MonoBehaviour coroutineOwner,
        string localPlayerId,
        string remotePlayerId,
        Transform remotePlayerTransform,
        VoiceSignalingController signaling,
        LocalVoiceSource localVoiceSource,
        VoiceChatConfig config,
        Action<string> log)
    {
        this.coroutineOwner = coroutineOwner;
        this.localPlayerId = localPlayerId;
        RemotePlayerId = remotePlayerId;
        this.signaling = signaling;
        this.localVoiceSource = localVoiceSource;
        this.config = config ?? new VoiceChatConfig();
        this.log = log;
        speaker = new RemoteVoiceSpeaker(remotePlayerId, remotePlayerTransform, this.config);
    }

    public string RemotePlayerId { get; }
    public bool NegotiationStarted => negotiationStarted;
    public string IceConnectionState { get; private set; } = "new";
    public bool HasRemoteAudio => hasRemoteAudio;
    public event Action<WebRtcVoicePeer, string> PeerFailed;

    public void RefreshRemoteTransform(Transform remotePlayerTransform)
    {
        speaker?.AttachTo(remotePlayerTransform);
    }

    public void ApplyInterest(VoicePeerInterestDto interest)
    {
        if (interest == null)
        {
            return;
        }

        speaker?.ApplyInterest(interest);
    }

    public void CreateOffer()
    {
        if (!CanRunPeerOperation())
        {
            return;
        }

        if (!EnsurePeerConnection())
        {
            return;
        }

        coroutineOwner.StartCoroutine(CreateOfferRoutine());
    }

    public void ReceiveOffer(WebRtcOfferDto offer)
    {
        if (!CanRunPeerOperation() || offer == null || string.IsNullOrWhiteSpace(offer.sdp))
        {
            return;
        }

        if (!EnsurePeerConnection())
        {
            return;
        }

        coroutineOwner.StartCoroutine(ReceiveOfferRoutine(offer.sdp));
    }

    public void ReceiveAnswer(WebRtcAnswerDto answer)
    {
        if (!CanRunPeerOperation() || answer == null || string.IsNullOrWhiteSpace(answer.sdp))
        {
            return;
        }

        if (!EnsurePeerConnection())
        {
            return;
        }

        coroutineOwner.StartCoroutine(ReceiveAnswerRoutine(answer.sdp));
    }

    public void ReceiveIce(WebRtcIceCandidateDto ice)
    {
        if (disposed || ice == null || string.IsNullOrWhiteSpace(ice.candidate))
        {
            return;
        }

        if (!EnsurePeerConnection())
        {
            return;
        }

        if (!remoteDescriptionSet)
        {
            pendingIceCandidates.Add(ice);
            return;
        }

        AddIceCandidate(ice);
    }

    public void Dispose()
    {
        disposed = true;
        pendingIceCandidates.Clear();

        try
        {
            if (peerConnection != null && localAudioSender != null)
            {
                peerConnection.RemoveTrack(localAudioSender);
                localAudioSender = null;
            }

            receiveStream?.Dispose();
            sendStream?.Dispose();
            peerConnection?.Close();
            peerConnection?.Dispose();
            speaker?.Dispose();
        }
        catch (Exception exception)
        {
            LogDebug($"Voice peer cleanup for {RemotePlayerId} hit a WebRTC error: {exception.Message}");
        }
        finally
        {
            localAudioSender = null;
            receiveStream = null;
            sendStream = null;
            peerConnection = null;
            speaker = null;
        }

        LogDebug($"Voice peer disposed for {RemotePlayerId}.");
    }

    private bool EnsurePeerConnection()
    {
        if (peerConnection != null)
        {
            return true;
        }

        try
        {
            RTCConfiguration rtcConfiguration = default;
            rtcConfiguration.iceServers = new[]
            {
                new RTCIceServer
                {
                    urls = new[] { ResolveStunServer() },
                },
            };

            peerConnection = new RTCPeerConnection(ref rtcConfiguration);
            peerConnection.OnIceCandidate = candidate =>
            {
                if (candidate == null || string.IsNullOrWhiteSpace(candidate.Candidate))
                {
                    return;
                }

                signaling.SendIce(
                    RemotePlayerId,
                    candidate.Candidate,
                    string.IsNullOrWhiteSpace(candidate.SdpMid) ? "0" : candidate.SdpMid,
                    candidate.SdpMLineIndex.HasValue ? candidate.SdpMLineIndex.Value : 0
                );
                LogDebug($"Voice ICE sent to {RemotePlayerId}.");
            };
            peerConnection.OnIceConnectionChange = HandleIceConnectionChange;
            peerConnection.OnTrack = trackEvent =>
            {
                if (trackEvent.Track != null && trackEvent.Track.Kind == TrackKind.Audio)
                {
                    receiveStream.AddTrack(trackEvent.Track);
                }
            };

            sendStream = new MediaStream();
            receiveStream = new MediaStream();
            receiveStream.OnAddTrack += trackEvent =>
            {
                if (trackEvent.Track is AudioStreamTrack audioTrack)
                {
                    hasRemoteAudio = true;
                    speaker?.AttachTrack(audioTrack);
                    LogDebug($"Voice audio track attached for {RemotePlayerId}.");
                }
            };

            if (localVoiceSource.Track != null)
            {
                localAudioSender = peerConnection.AddTrack(localVoiceSource.Track, sendStream);
            }
            else
            {
                LogDebug($"Voice peer {RemotePlayerId} created without a local microphone track.");
            }

            return true;
        }
        catch (Exception exception)
        {
            LogDebug($"Voice peer connection setup failed for {RemotePlayerId}: {exception.Message}");
            PeerFailed?.Invoke(this, exception.Message);
            return false;
        }
    }

    private IEnumerator CreateOfferRoutine()
    {
        if (negotiationInProgress || disposed)
        {
            yield break;
        }

        negotiationStarted = true;
        negotiationInProgress = true;
        RTCSessionDescriptionAsyncOperation offerOperation = peerConnection.CreateOffer();
        yield return offerOperation;
        if (offerOperation.IsError)
        {
            LogDebug($"Voice offer creation failed for {RemotePlayerId}: {offerOperation.Error.message}");
            negotiationInProgress = false;
            PeerFailed?.Invoke(this, offerOperation.Error.message);
            yield break;
        }

        RTCSessionDescription description = offerOperation.Desc;
        RTCSetSessionDescriptionAsyncOperation setLocalOperation = peerConnection.SetLocalDescription(ref description);
        yield return setLocalOperation;
        if (setLocalOperation.IsError)
        {
            LogDebug($"Voice local offer description failed for {RemotePlayerId}: {setLocalOperation.Error.message}");
            negotiationInProgress = false;
            PeerFailed?.Invoke(this, setLocalOperation.Error.message);
            yield break;
        }

        signaling.SendOffer(RemotePlayerId, description.sdp);
        LogDebug($"Voice offer sent to {RemotePlayerId}.");
        negotiationInProgress = false;
    }

    private IEnumerator ReceiveOfferRoutine(string sdp)
    {
        if (negotiationInProgress || disposed)
        {
            yield break;
        }

        negotiationStarted = true;
        negotiationInProgress = true;
        RTCSessionDescription remoteDescription = new RTCSessionDescription
        {
            type = RTCSdpType.Offer,
            sdp = sdp,
        };
        RTCSetSessionDescriptionAsyncOperation setRemoteOperation = peerConnection.SetRemoteDescription(ref remoteDescription);
        yield return setRemoteOperation;
        if (setRemoteOperation.IsError)
        {
            LogDebug($"Voice remote offer description failed for {RemotePlayerId}: {setRemoteOperation.Error.message}");
            negotiationInProgress = false;
            PeerFailed?.Invoke(this, setRemoteOperation.Error.message);
            yield break;
        }

        remoteDescriptionSet = true;
        FlushPendingIceCandidates();

        RTCSessionDescriptionAsyncOperation answerOperation = peerConnection.CreateAnswer();
        yield return answerOperation;
        if (answerOperation.IsError)
        {
            LogDebug($"Voice answer creation failed for {RemotePlayerId}: {answerOperation.Error.message}");
            negotiationInProgress = false;
            PeerFailed?.Invoke(this, answerOperation.Error.message);
            yield break;
        }

        RTCSessionDescription localDescription = answerOperation.Desc;
        RTCSetSessionDescriptionAsyncOperation setLocalOperation = peerConnection.SetLocalDescription(ref localDescription);
        yield return setLocalOperation;
        if (setLocalOperation.IsError)
        {
            LogDebug($"Voice local answer description failed for {RemotePlayerId}: {setLocalOperation.Error.message}");
            negotiationInProgress = false;
            PeerFailed?.Invoke(this, setLocalOperation.Error.message);
            yield break;
        }

        signaling.SendAnswer(RemotePlayerId, localDescription.sdp);
        LogDebug($"Voice answer sent to {RemotePlayerId}.");
        negotiationInProgress = false;
    }

    private IEnumerator ReceiveAnswerRoutine(string sdp)
    {
        if (disposed)
        {
            yield break;
        }

        RTCSessionDescription remoteDescription = new RTCSessionDescription
        {
            type = RTCSdpType.Answer,
            sdp = sdp,
        };
        RTCSetSessionDescriptionAsyncOperation setRemoteOperation = peerConnection.SetRemoteDescription(ref remoteDescription);
        yield return setRemoteOperation;
        if (setRemoteOperation.IsError)
        {
            LogDebug($"Voice remote answer description failed for {RemotePlayerId}: {setRemoteOperation.Error.message}");
            PeerFailed?.Invoke(this, setRemoteOperation.Error.message);
            yield break;
        }

        remoteDescriptionSet = true;
        FlushPendingIceCandidates();
    }

    private void FlushPendingIceCandidates()
    {
        foreach (WebRtcIceCandidateDto ice in pendingIceCandidates)
        {
            AddIceCandidate(ice);
        }

        pendingIceCandidates.Clear();
    }

    private void AddIceCandidate(WebRtcIceCandidateDto ice)
    {
        try
        {
            RTCIceCandidate candidate = new RTCIceCandidate(
                new RTCIceCandidateInit
                {
                    candidate = ice.candidate,
                    sdpMid = string.IsNullOrWhiteSpace(ice.sdpMid) ? "0" : ice.sdpMid,
                    sdpMLineIndex = Mathf.Max(0, ice.sdpMLineIndex),
                }
            );
            bool added = peerConnection.AddIceCandidate(candidate);
            LogDebug($"Voice ICE candidate from {RemotePlayerId} added={added}.");
        }
        catch (Exception exception)
        {
            LogDebug($"Voice ICE candidate from {RemotePlayerId} failed: {exception.Message}");
        }
    }

    private bool CanRunPeerOperation()
    {
        return !disposed && coroutineOwner != null && localVoiceSource != null;
    }

    private void HandleIceConnectionChange(RTCIceConnectionState state)
    {
        IceConnectionState = state.ToString();
        LogDebug($"Voice ICE state {localPlayerId}->{RemotePlayerId}: {state}");
        if (state == RTCIceConnectionState.Failed
            || state == RTCIceConnectionState.Disconnected
            || state == RTCIceConnectionState.Closed)
        {
            PeerFailed?.Invoke(this, $"ICE connection {state}.");
        }
    }

    private string ResolveStunServer()
    {
        return string.IsNullOrWhiteSpace(config.stunServer)
            ? "stun:stun.l.google.com:19302"
            : config.stunServer;
    }

    private void LogDebug(string message)
    {
        if (config.debugLogging)
        {
            log?.Invoke(message);
        }
    }
}
