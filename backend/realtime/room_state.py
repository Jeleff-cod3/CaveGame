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
class MammothRuntimeState:
    enemy_id: str = "mammoth"
    current_health: int = 100
    max_health: int = 100
    authoritative_user_id: int = 0
    position: list[float] = field(default_factory=lambda: [0, 0, 0])
    rotation: list[float] = field(default_factory=lambda: [0, 0, 0])
    health_initialized: bool = False
    last_updated: float = field(default_factory=time)

    def apply_health_update(self, reported_current_health: int, reported_max_health: int, damage: int = 0) -> None:
        self.max_health = max(1, int(reported_max_health))

        if damage > 0:
            self.current_health = max(0, self.current_health - int(damage))
        else:
            self.current_health = max(0, min(int(reported_current_health), self.max_health))

        self.health_initialized = True
        self.last_updated = time()

    def apply_state_update(
        self,
        authoritative_user_id: int,
        position: list[float],
        rotation: list[float],
        reported_current_health: int,
        reported_max_health: int,
    ) -> None:
        self.authoritative_user_id = max(0, int(authoritative_user_id))
        self.position = [float(position[0]), float(position[1]), float(position[2])]
        self.rotation = [float(rotation[0]), float(rotation[1]), float(rotation[2])]
        self.max_health = max(1, int(reported_max_health))

        if not self.health_initialized:
            self.current_health = max(0, min(int(reported_current_health), self.max_health))

        self.last_updated = time()

    def as_health_payload(self, lobby_id: int) -> dict:
        return {
            "type": "mammoth_health",
            "lobbyId": lobby_id,
            "enemyId": self.enemy_id,
            "currentHealth": self.current_health,
            "maxHealth": self.max_health,
            "damage": 0,
            "serverTime": self.last_updated,
        }

    def as_state_payload(self, lobby_id: int) -> dict:
        return {
            "type": "mammoth_state",
            "lobbyId": lobby_id,
            "enemyId": self.enemy_id,
            "authoritativeUserId": self.authoritative_user_id,
            "currentHealth": self.current_health,
            "maxHealth": self.max_health,
            "damage": 0,
            "position": self.position,
            "rotation": self.rotation,
            "serverTime": self.last_updated,
        }


@dataclass
class RoomRuntimeState:
    lobby_id: int
    players: dict[int, PlayerRuntimeState] = field(default_factory=dict)
    connections: dict[str, object] = field(default_factory=dict)
    mammoth: MammothRuntimeState = field(default_factory=MammothRuntimeState)
    started: bool = False


ROOMS: dict[int, RoomRuntimeState] = {}


def get_room(lobby_id: int) -> RoomRuntimeState:
    if lobby_id not in ROOMS:
        ROOMS[lobby_id] = RoomRuntimeState(lobby_id=lobby_id)
    return ROOMS[lobby_id]
