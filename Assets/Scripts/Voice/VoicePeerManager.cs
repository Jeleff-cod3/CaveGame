// Owns local microphone capture and all remote WebRTC voice peer lifecycles.
// Used by: MultiplayerPrototype.
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public sealed class VoicePeerManager : IDisposable
{
    private readonly MonoBehaviour coroutineOwner;
    private readonly string localPlayerId;
    private readonly VoiceSignalingController signaling;
    private readonly Func<string, Transform> remotePlayerTransformProvider;
    private readonly VoiceChatConfig config;
    private readonly Action<string> log;
    private readonly Dictionary<string, WebRtcVoicePeer> peersByPlayerId = new Dictionary<string, WebRtcVoicePeer>();
    private readonly Dictionary<string, VoicePeerInterestDto> latestInterestByPlayerId = new Dictionary<string, VoicePeerInterestDto>();
    private readonly Dictionary<string, float> retryAllowedAtByPlayerId = new Dictionary<string, float>();

    private LocalVoiceSource localVoiceSource;
    private GameObject localVoiceObject;
    private float lastInterestSnapshotReceivedAt = -1f;
    private bool disposed;

    public VoicePeerManager(
        MonoBehaviour coroutineOwner,
        string localPlayerId,
        VoiceSignalingController signaling,
        Func<string, Transform> remotePlayerTransformProvider,
        VoiceChatConfig config,
        Action<string> log)
    {
        this.coroutineOwner = coroutineOwner;
        this.localPlayerId = localPlayerId;
        this.signaling = signaling;
        this.remotePlayerTransformProvider = remotePlayerTransformProvider;
        this.config = config ?? new VoiceChatConfig();
        this.log = log;

        signaling.OnInterestSnapshotReceived += HandleInterestSnapshot;
        signaling.OnOfferReceived += HandleOfferReceived;
        signaling.OnAnswerReceived += HandleAnswerReceived;
        signaling.OnIceReceived += HandleIceReceived;
        signaling.OnPeerLeft += HandlePeerLeft;
    }

    public int ActivePeerCount => peersByPlayerId.Count;

    public string GetDebugSnapshot()
    {
        StringBuilder sb = new StringBuilder(128);
        sb.Append("enabled=").Append(config.voiceEnabled);
        sb.Append(", mic=").Append(localVoiceSource != null && !string.IsNullOrWhiteSpace(localVoiceSource.ActiveMicrophoneDevice) ? localVoiceSource.ActiveMicrophoneDevice : "none");
        sb.Append(", activePeers=").Append(peersByPlayerId.Count);
        sb.Append(", lastSnapshotAgoMs=").Append(lastInterestSnapshotReceivedAt >= 0f ? ((Time.unscaledTime - lastInterestSnapshotReceivedAt) * 1000f).ToString("0") : "n/a");

        foreach (KeyValuePair<string, WebRtcVoicePeer> entry in peersByPlayerId)
        {
            WebRtcVoicePeer peer = entry.Value;
            sb.Append(", ").Append(entry.Key).Append("={ice=").Append(peer.IceConnectionState);
            sb.Append(", audio=").Append(peer.HasRemoteAudio ? "yes" : "no").Append("}");
        }

        return sb.ToString();
    }

    public void RefreshPeerTransform(string playerId)
    {
        if (disposed || string.IsNullOrWhiteSpace(playerId))
        {
            return;
        }

        if (peersByPlayerId.TryGetValue(playerId, out WebRtcVoicePeer existingPeer))
        {
            existingPeer.RefreshRemoteTransform(remotePlayerTransformProvider?.Invoke(playerId));
            return;
        }

        if (latestInterestByPlayerId.TryGetValue(playerId, out VoicePeerInterestDto interest))
        {
            CreateOrUpdatePeer(interest);
        }
    }

    public void RemovePeer(string playerId)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return;
        }

        latestInterestByPlayerId.Remove(playerId);
        if (!peersByPlayerId.TryGetValue(playerId, out WebRtcVoicePeer peer))
        {
            return;
        }

        peer.PeerFailed -= HandlePeerFailed;
        peer.Dispose();
        peersByPlayerId.Remove(playerId);
        LogDebug($"Voice peer disposed for {playerId}.");
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        signaling.OnInterestSnapshotReceived -= HandleInterestSnapshot;
        signaling.OnOfferReceived -= HandleOfferReceived;
        signaling.OnAnswerReceived -= HandleAnswerReceived;
        signaling.OnIceReceived -= HandleIceReceived;
        signaling.OnPeerLeft -= HandlePeerLeft;

        foreach (WebRtcVoicePeer peer in peersByPlayerId.Values)
        {
            peer.PeerFailed -= HandlePeerFailed;
            peer.Dispose();
        }

        peersByPlayerId.Clear();
        latestInterestByPlayerId.Clear();
        retryAllowedAtByPlayerId.Clear();

        if (localVoiceSource != null)
        {
            localVoiceSource.StopCapture();
            localVoiceSource = null;
        }

        if (localVoiceObject != null)
        {
            UnityEngine.Object.Destroy(localVoiceObject);
            localVoiceObject = null;
        }
    }

    private void HandleInterestSnapshot(VoiceInterestSnapshotDto snapshot)
    {
        if (disposed || snapshot == null || !config.voiceEnabled)
        {
            RemoveAllPeers();
            return;
        }

        lastInterestSnapshotReceivedAt = Time.unscaledTime;
        LogDebug($"Voice snapshot received: self={snapshot.selfPlayerId}, audible={snapshot.audiblePeers?.Length ?? 0}.");
        HashSet<string> peersToKeep = new HashSet<string>();
        if (snapshot.audiblePeers != null)
        {
            foreach (VoicePeerInterestDto interest in snapshot.audiblePeers)
            {
                if (interest == null || string.IsNullOrWhiteSpace(interest.playerId) || interest.playerId == localPlayerId)
                {
                    continue;
                }

                peersToKeep.Add(interest.playerId);
                latestInterestByPlayerId[interest.playerId] = interest;
                CreateOrUpdatePeer(interest);
            }
        }

        RemovePeersNotIn(peersToKeep);
    }

    private void HandleOfferReceived(WebRtcOfferDto offer)
    {
        if (disposed || offer == null || string.IsNullOrWhiteSpace(offer.fromPlayerId) || offer.fromPlayerId == localPlayerId)
        {
            return;
        }

        WebRtcVoicePeer peer = CreatePeerIfPossible(offer.fromPlayerId);
        LogDebug($"Voice offer received from {offer.fromPlayerId}.");
        peer?.ReceiveOffer(offer);
    }

    private void HandleAnswerReceived(WebRtcAnswerDto answer)
    {
        if (disposed || answer == null || string.IsNullOrWhiteSpace(answer.fromPlayerId))
        {
            return;
        }

        if (peersByPlayerId.TryGetValue(answer.fromPlayerId, out WebRtcVoicePeer peer))
        {
            LogDebug($"Voice answer received from {answer.fromPlayerId}.");
            peer.ReceiveAnswer(answer);
        }
    }

    private void HandleIceReceived(WebRtcIceCandidateDto ice)
    {
        if (disposed || ice == null || string.IsNullOrWhiteSpace(ice.fromPlayerId))
        {
            return;
        }

        WebRtcVoicePeer peer = CreatePeerIfPossible(ice.fromPlayerId);
        LogDebug($"Voice ICE received from {ice.fromPlayerId}.");
        peer?.ReceiveIce(ice);
    }

    private void HandlePeerLeft(VoicePeerLeftDto peerLeft)
    {
        if (peerLeft != null)
        {
            RemovePeer(peerLeft.playerId);
        }
    }

    private void CreateOrUpdatePeer(VoicePeerInterestDto interest)
    {
        WebRtcVoicePeer peer = CreatePeerIfPossible(interest.playerId);
        if (peer == null)
        {
            return;
        }

        peer.ApplyInterest(interest);
        if (ShouldInitiateOffer(interest.playerId) && !peer.NegotiationStarted)
        {
            peer.CreateOffer();
        }
    }

    private WebRtcVoicePeer CreatePeerIfPossible(string remotePlayerId)
    {
        if (string.IsNullOrWhiteSpace(remotePlayerId) || remotePlayerId == localPlayerId)
        {
            return null;
        }

        if (peersByPlayerId.TryGetValue(remotePlayerId, out WebRtcVoicePeer existingPeer))
        {
            existingPeer.RefreshRemoteTransform(remotePlayerTransformProvider?.Invoke(remotePlayerId));
            return existingPeer;
        }

        if (!CanRetryPeer(remotePlayerId))
        {
            return null;
        }

        if (!EnsureLocalVoiceSource())
        {
            return null;
        }

        Transform remoteTransform = remotePlayerTransformProvider?.Invoke(remotePlayerId);
        if (remoteTransform == null)
        {
            LogDebug($"Voice peer {remotePlayerId} is audible but no remote transform exists yet.");
            return null;
        }

        WebRtcVoicePeer peer = new WebRtcVoicePeer(
            coroutineOwner,
            localPlayerId,
            remotePlayerId,
            remoteTransform,
            signaling,
            localVoiceSource,
            config,
            LogDebug
        );
        peer.PeerFailed += HandlePeerFailed;
        peersByPlayerId[remotePlayerId] = peer;
        LogDebug($"Voice peer created for {remotePlayerId}.");
        return peer;
    }

    private bool EnsureLocalVoiceSource()
    {
        if (localVoiceSource != null && localVoiceSource.IsCapturing)
        {
            return true;
        }

        VoiceWebRtcBootstrap.EnsureExists();
        if (localVoiceObject == null)
        {
            localVoiceObject = new GameObject("Local Voice Source");
            UnityEngine.Object.DontDestroyOnLoad(localVoiceObject);
            localVoiceSource = localVoiceObject.AddComponent<LocalVoiceSource>();
            localVoiceSource.Configure(config, LogDebug);
        }

        bool started = localVoiceSource.StartCapture();
        if (!started)
        {
            LogDebug("Voice microphone capture is unavailable; voice peers will not be created yet.");
        }

        return started;
    }

    private bool ShouldInitiateOffer(string remotePlayerId)
    {
        return string.CompareOrdinal(localPlayerId, remotePlayerId) < 0;
    }

    private void RemovePeersNotIn(HashSet<string> peersToKeep)
    {
        List<string> peersToRemove = new List<string>();
        foreach (string playerId in peersByPlayerId.Keys)
        {
            if (!peersToKeep.Contains(playerId))
            {
                peersToRemove.Add(playerId);
            }
        }

        foreach (string playerId in peersToRemove)
        {
            RemovePeer(playerId);
        }
    }

    private void RemoveAllPeers()
    {
        RemovePeersNotIn(new HashSet<string>());
    }

    private bool CanRetryPeer(string remotePlayerId)
    {
        if (!retryAllowedAtByPlayerId.TryGetValue(remotePlayerId, out float retryAt))
        {
            return true;
        }

        if (Time.unscaledTime >= retryAt)
        {
            retryAllowedAtByPlayerId.Remove(remotePlayerId);
            return true;
        }

        LogDebug($"Voice peer creation for {remotePlayerId} waiting for retry window.");
        return false;
    }

    private void HandlePeerFailed(WebRtcVoicePeer peer, string reason)
    {
        if (peer == null || disposed)
        {
            return;
        }

        string remotePlayerId = peer.RemotePlayerId;
        LogDebug($"Voice peer {remotePlayerId} failed: {reason}");
        retryAllowedAtByPlayerId[remotePlayerId] = Time.unscaledTime + Mathf.Max(0.1f, config.peerReconnectDelaySeconds);
        RemovePeer(remotePlayerId);
    }

    private void LogDebug(string message)
    {
        if (config.debugLogging)
        {
            log?.Invoke(message);
        }
    }
}
