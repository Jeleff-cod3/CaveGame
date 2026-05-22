from dataclasses import dataclass, field
from time import time


@dataclass
class PlayerRuntimeState:
    user_id: int
    player_id: str
    channel_name: str
    last_seq: int = 0
    last_seen: float = field(default_factory=time)

    position: list[float] = field(default_factory=lambda: [0, 0, 0])
    rotation: list[float] = field(default_factory=lambda: [0, 0, 0])
    velocity: list[float] = field(default_factory=lambda: [0, 0, 0])
    animation_state: str = "idle"

    rate_window_started: float = field(default_factory=time)
    state_messages_in_window: int = 0

    def can_accept_state_message(self, now: float, max_messages_per_second: int = 60) -> bool:
        if now - self.rate_window_started >= 1:
            self.rate_window_started = now
            self.state_messages_in_window = 0

        if self.state_messages_in_window >= max_messages_per_second:
            return False

        self.state_messages_in_window += 1
        return True

    def as_payload(self) -> dict:
        return {
            "playerId": self.player_id,
            "userId": self.user_id,
            "seq": self.last_seq,
            "serverTime": self.last_seen,
            "position": self.position,
            "rotation": self.rotation,
            "velocity": self.velocity,
            "animationState": self.animation_state,
        }


@dataclass
class RoomRuntimeState:
    lobby_id: int
    players: dict[int, PlayerRuntimeState] = field(default_factory=dict)
    connections: dict[str, object] = field(default_factory=dict)
    started: bool = False


ROOMS: dict[int, RoomRuntimeState] = {}


def get_room(lobby_id: int) -> RoomRuntimeState:
    if lobby_id not in ROOMS:
        ROOMS[lobby_id] = RoomRuntimeState(lobby_id=lobby_id)
    return ROOMS[lobby_id]
