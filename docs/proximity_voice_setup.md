# Proximity Voice Setup

This document explains how to run and verify CaveGame proximity voice chat. Django Channels owns authentication, lobby membership, player positions, audibility snapshots, and targeted WebRTC signaling. Unity WebRTC owns microphone capture, peer connections, ICE, and audio media. Raw live microphone audio is not sent through Django HTTP or WebSocket.

## Architecture Summary

- Unity sends normal gameplay state on the existing `/ws/game/<lobby_id>/` socket.
- Django derives the sender as `player_<user_id>` from the authenticated lobby member; it does not trust client-supplied `fromPlayerId`.
- Django stores latest player positions in `backend/realtime/voice_runtime.py`.
- Django emits `voice_interest_snapshot` at a low cadence, currently around 5 Hz, with audible peers, distance, gain, and mute state.
- Unity `VoicePeerManager` creates one `WebRtcVoicePeer` per audible remote player and removes peers when the server stops listing them.
- Unity `VoiceSignalingController` sends `webrtc_offer`, `webrtc_answer`, and `webrtc_ice` through the existing game socket.
- Django relays WebRTC signaling only to the target player's channel in the same lobby.
- Unity `RemoteVoiceSpeaker` attaches remote audio to the runtime remote cube transform and applies server gain plus Unity 3D falloff.

## Local Backend

From the repo root:

```powershell
cd backend
python -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -r requirements.txt
python manage.py migrate
```

For local one-process testing without Redis:

```powershell
$env:USE_IN_MEMORY_CHANNEL_LAYER="true"
python manage.py runserver
```

For ASGI/Daphne:

```powershell
$env:USE_IN_MEMORY_CHANNEL_LAYER="true"
python -m daphne -b 127.0.0.1 -p 8000 config.asgi:application
```

For Redis-backed Channels:

```powershell
$env:USE_IN_MEMORY_CHANNEL_LAYER="false"
$env:REDIS_URL="redis://127.0.0.1:6379/0"
python -m daphne -b 127.0.0.1 -p 8000 config.asgi:application
```

## Environment Variables

- `USE_IN_MEMORY_CHANNEL_LAYER=true`: uses Channels in-memory layer. Good for one local backend process.
- `USE_IN_MEMORY_CHANNEL_LAYER=false`: uses `channels_redis` and `REDIS_URL`.
- `REDIS_URL`: Redis connection string, default `redis://127.0.0.1:6379/0`.
- `DJANGO_ALLOWED_HOSTS`: comma-separated hosts, default includes localhost and `*`.
- `ENABLE_WS_ORIGIN_VALIDATION=true`: wraps websockets with `AllowedHostsOriginValidator`. Leave off if a local Unity client does not send browser-style origin headers.
- `DJANGO_SECRET_KEY`, `DJANGO_DEBUG`, `DATABASE_URL`, and Postgres env vars follow the normal Django settings in `backend/config/settings.py`.

Redis is optional for local one-process testing. Use Redis when running multiple Daphne/backend processes or testing behavior closer to deployment.

## Backend Verification

```powershell
cd backend
.\.venv\Scripts\Activate.ps1
$env:USE_IN_MEMORY_CHANNEL_LAYER="true"
python manage.py test
```

The existing gameplay socket should still accept `player_state`, `setup_placement`, `teacher_state`, `key_state`, `mammoth_health`, `mammoth_state`, `ping`, and `heartbeat`. Voice adds `webrtc_offer`, `webrtc_answer`, `webrtc_ice`, and `voice_presence`; it does not replace the gameplay message flow.

## Unity Setup

- Required Unity Editor: `6000.4.6f1`.
- Required package: `com.unity.webrtc` version `3.0.0` in `Packages/manifest.json`.
- Existing socket package: `com.endel.nativewebsocket` remains in place.
- Voice scripts live under `Assets/Scripts/Voice`.

`VoiceWebRtcBootstrap` can be added to a multiplayer scene manually, but it does not need to be. `VoicePeerManager` calls `VoiceWebRtcBootstrap.EnsureExists()` before voice capture/peer setup, so the bootstrap is auto-created when voice starts.

To verify the package and scripts:

1. Close duplicate Unity Editor instances for this project.
2. Open the repo root in Unity Hub with Unity `6000.4.6f1`.
3. Let packages restore and scripts compile.
4. Open `Assets/Scenes/multiplayer_and_chunkmap.unity` for the full test, or `Assets/Scenes/multiplayerlobbytesting.unity` for a smaller socket/lobby smoke test.
5. Confirm the Console has no C# compile errors.
6. In the `MultiplayerPrototype` inspector, enable `voiceChatConfig.debugLogging` if you want detailed voice logs.

## Two-Client Manual Test

Use either two standalone builds, or one Editor and one standalone build. Two Editors cannot open the same Unity project folder at the same time.

1. Start the backend with in-memory Channels or Redis-backed Daphne.
2. Start client A and client B.
3. Create or join the same lobby with two authenticated users.
4. Mark players ready and start the game.
5. Confirm both runtime cubes appear.
6. Confirm both clients connect to `/ws/game/<lobby_id>/`.
7. With `debugLogging` enabled, confirm `voice_interest_snapshot` messages arrive.
8. Walk players within about `18` units.
9. Confirm offer/answer/ICE logs appear on both clients.
10. Confirm ICE reaches `Connected` or `Completed`, or logs a specific failure.
11. Speak into client A's microphone and listen from client B near A's remote avatar.
12. Speak into client B's microphone and listen from client A near B's remote avatar.
13. Walk away and confirm volume fades, then hard-mutes beyond max distance while the peer remains stable through the hysteresis band.
14. Walk back into range and confirm voice resumes without restarting the game.
15. Stop one client and confirm the other logs `voice_peer_left` and disposes that voice peer.

Press the existing multiplayer debug snapshot key to print socket and voice status, including microphone device, active peer count, last snapshot age, ICE state, and remote audio status.

## Known Limitations

- STUN-only networking can fail on some NATs.
- Reliable Internet play needs a TURN server in addition to STUN.
- The current design is peer-to-peer audible-peer mesh, not an SFU or central audio mixer.
- This is not a server-side recording, moderation, transcription, or voice storage system.
- Backend voice runtime state is in process memory; use one Daphne process for local testing unless this state is externalized.

## Troubleshooting

### No Microphone

- Check Unity Console for `LocalVoiceSource could not start because no microphone devices were found`.
- Verify OS microphone permission for the Editor or build.
- Confirm `voiceChatConfig.voiceEnabled` is true.

### No WebSocket

- Confirm backend URL and token auth still work.
- Confirm the lobby is started; `/ws/game/<lobby_id>/` rejects unauthenticated users, non-members, and non-started lobbies.
- If using origin validation, try disabling `ENABLE_WS_ORIGIN_VALIDATION` for local Unity testing.

### No Voice Snapshot

- Confirm both players are connected to the same game socket lobby.
- Confirm clients are sending `player_state`.
- Confirm backend logs do not show validation errors.
- Confirm player positions are close enough to start hearing, around `18` units or less.

### ICE Failed

- Confirm both clients can exchange `webrtc_offer`, `webrtc_answer`, and `webrtc_ice`.
- Try both clients on the same LAN.
- Check firewall rules for the Editor/build.
- Add TURN support if STUN-only connection fails across the current networks.

### Remote Audio Track Received But Silent

- Confirm `RemoteVoiceSpeaker` attached to the expected remote cube.
- Confirm server snapshot gain is above `0`.
- Confirm the listener is inside `maxVoiceDistance`.
- Confirm the speaker client has microphone permission and `LocalVoiceSource` started.
- Confirm Unity AudioListener and global audio volume are active.

### Local Echo

- `LocalVoiceSource` sets `AudioStreamTrack.Loopback = false`, which tells Unity WebRTC to send audio remotely without local playback.
- If echo remains, check whether the remote client's speakers are being captured by its microphone.
- Use headphones during testing to separate microphone input from remote playback.
