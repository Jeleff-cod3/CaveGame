from dataclasses import dataclass, field
from math import dist
from time import time

from .message_types import VOICE_INTEREST_SNAPSHOT

DEFAULT_MIN_VOICE_DISTANCE = 2.0
DEFAULT_MAX_VOICE_DISTANCE = 18.0
DEFAULT_KEEP_ALIVE_DISTANCE = 22.0
VOICE_INTEREST_SNAPSHOT_MIN_INTERVAL_SECONDS = 0.2


def vector3_from(value, fallback=None) -> list[float]:
    if fallback is None:
        fallback = [0, 0, 0]
    if not isinstance(value, list) or len(value) != 3:
        value = fallback
    return [float(value[0]), float(value[1]), float(value[2])]


def voice_gain_for_distance(
    distance: float,
    min_voice_distance: float = DEFAULT_MIN_VOICE_DISTANCE,
    max_voice_distance: float = DEFAULT_MAX_VOICE_DISTANCE,
) -> float:
    min_distance = max(0.0, float(min_voice_distance))
    max_distance = max(min_distance, float(max_voice_distance))

    if distance <= min_distance:
        return 1.0

    if distance >= max_distance:
        return 0.0

    fade_span = max_distance - min_distance
    if fade_span <= 0:
        return 0.0

    return max(0.0, min(1.0, 1.0 - ((distance - min_distance) / fade_span)))


@dataclass
class VoicePeerRuntimeState:
    player_id: str
    user_id: int
    channel_name: str
    position: list[float] = field(default_factory=lambda: [0, 0, 0])
    last_seen: float = field(default_factory=time)
    is_ready: bool = True
    is_muted: bool = False

    def update_position(self, position, now: float | None = None) -> None:
        self.position = vector3_from(position, self.position)
        self.last_seen = time() if now is None else now

    def apply_presence(self, is_ready: bool, is_muted: bool = False, now: float | None = None) -> None:
        self.is_ready = bool(is_ready)
        self.is_muted = bool(is_muted)
        self.last_seen = time() if now is None else now


@dataclass
class VoiceRoomRuntimeState:
    lobby_id: int
    min_voice_distance: float = DEFAULT_MIN_VOICE_DISTANCE
    max_voice_distance: float = DEFAULT_MAX_VOICE_DISTANCE
    keep_alive_distance: float = DEFAULT_KEEP_ALIVE_DISTANCE
    peers_by_player_id: dict[str, VoicePeerRuntimeState] = field(default_factory=dict)
    player_id_by_channel_name: dict[str, str] = field(default_factory=dict)
    audible_peers_by_player_id: dict[str, set[str]] = field(default_factory=dict)
    last_interest_snapshot_sent_at: float = 0.0

    def connect_player(
        self,
        player_id: str,
        user_id: int,
        channel_name: str,
        position=None,
        now: float | None = None,
    ) -> VoicePeerRuntimeState:
        if player_id in self.peers_by_player_id:
            old_channel_name = self.peers_by_player_id[player_id].channel_name
            self.player_id_by_channel_name.pop(old_channel_name, None)

        peer = VoicePeerRuntimeState(
            player_id=player_id,
            user_id=int(user_id),
            channel_name=channel_name,
            position=vector3_from(position),
            last_seen=time() if now is None else now,
        )
        self.peers_by_player_id[player_id] = peer
        self.player_id_by_channel_name[channel_name] = player_id
        self.audible_peers_by_player_id.setdefault(player_id, set())
        return peer

    def update_position(self, player_id: str, position, now: float | None = None) -> None:
        peer = self.peers_by_player_id.get(player_id)
        if peer is not None:
            peer.update_position(position, now)

    def apply_presence(
        self,
        player_id: str,
        is_ready: bool,
        is_muted: bool = False,
        now: float | None = None,
    ) -> None:
        peer = self.peers_by_player_id.get(player_id)
        if peer is not None:
            peer.apply_presence(is_ready, is_muted, now)

    def disconnect_player(
        self,
        player_id: str | None = None,
        channel_name: str | None = None,
    ) -> str | None:
        resolved_player_id = player_id
        if resolved_player_id is None and channel_name is not None:
            resolved_player_id = self.player_id_by_channel_name.get(channel_name)

        if resolved_player_id is None:
            return None

        peer = self.peers_by_player_id.pop(resolved_player_id, None)
        if peer is not None:
            self.player_id_by_channel_name.pop(peer.channel_name, None)

        self.audible_peers_by_player_id.pop(resolved_player_id, None)
        for audible_peers in self.audible_peers_by_player_id.values():
            audible_peers.discard(resolved_player_id)

        return resolved_player_id

    def channel_name_for_player(self, player_id: str) -> str | None:
        peer = self.peers_by_player_id.get(player_id)
        return peer.channel_name if peer is not None else None

    def has_player(self, player_id: str) -> bool:
        return player_id in self.peers_by_player_id

    def should_send_interest_snapshots(
        self,
        now: float,
        min_interval: float = VOICE_INTEREST_SNAPSHOT_MIN_INTERVAL_SECONDS,
    ) -> bool:
        if now - self.last_interest_snapshot_sent_at < min_interval:
            return False

        self.last_interest_snapshot_sent_at = now
        return True

    def recompute_audible_peers(self) -> dict[str, list[dict]]:
        snapshots = {}
        for player_id in sorted(self.peers_by_player_id):
            snapshots[player_id] = self.audible_peers_for(player_id)
        return snapshots

    def audible_peers_for(self, listener_player_id: str) -> list[dict]:
        listener = self.peers_by_player_id.get(listener_player_id)
        if listener is None or not listener.is_ready:
            self.audible_peers_by_player_id[listener_player_id] = set()
            return []

        previously_audible = self.audible_peers_by_player_id.get(listener_player_id, set())
        next_audible_ids = set()
        audible_peers = []

        for speaker_player_id, speaker in sorted(self.peers_by_player_id.items()):
            if speaker_player_id == listener_player_id:
                continue
            if not speaker.is_ready:
                continue

            distance = self.distance_between(listener_player_id, speaker_player_id)
            if distance is None:
                continue

            can_start_hearing = distance <= self.max_voice_distance
            can_keep_hearing = speaker_player_id in previously_audible and distance <= self.keep_alive_distance
            if not can_start_hearing and not can_keep_hearing:
                continue

            next_audible_ids.add(speaker_player_id)
            audible_peers.append(
                {
                    "playerId": speaker_player_id,
                    "distance": distance,
                    "gain": voice_gain_for_distance(
                        distance,
                        self.min_voice_distance,
                        self.max_voice_distance,
                    ),
                    "isMuted": speaker.is_muted,
                }
            )

        self.audible_peers_by_player_id[listener_player_id] = next_audible_ids
        return audible_peers

    def as_interest_snapshot(self, listener_player_id: str, now: float | None = None) -> dict:
        return {
            "type": VOICE_INTEREST_SNAPSHOT,
            "lobbyId": self.lobby_id,
            "selfPlayerId": listener_player_id,
            "audiblePeers": self.audible_peers_for(listener_player_id),
            "serverTime": time() if now is None else now,
        }

    def distance_between(self, first_player_id: str, second_player_id: str) -> float | None:
        first = self.peers_by_player_id.get(first_player_id)
        second = self.peers_by_player_id.get(second_player_id)
        if first is None or second is None:
            return None
        return float(dist(first.position, second.position))
