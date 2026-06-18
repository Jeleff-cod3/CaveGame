from dataclasses import dataclass, field
from time import time


def vector3_from(value, fallback=None) -> list[float]:
    if fallback is None:
        fallback = [0, 0, 0]
    if not isinstance(value, list) or len(value) != 3:
        value = fallback
    return [float(value[0]), float(value[1]), float(value[2])]


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
    current_health: int = 100
    max_health: int = 100
    is_dead: bool = False

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
            "currentHealth": self.current_health,
            "maxHealth": self.max_health,
            "isDead": self.is_dead,
        }


@dataclass
class SetupPlacementRuntimeState:
    key_hider_user_id: int = 0
    teacher_placer_user_id: int = 0
    key_position: list[float] | None = None
    teacher_positions_x: list[float] | None = None
    teacher_positions_y: list[float] | None = None
    teacher_positions_z: list[float] | None = None
    finalized_at: float = 0

    @property
    def key_confirmed(self) -> bool:
        return self.key_position is not None

    @property
    def teachers_confirmed(self) -> bool:
        return (
            self.teacher_positions_x is not None
            and self.teacher_positions_y is not None
            and self.teacher_positions_z is not None
        )

    @property
    def is_finalized(self) -> bool:
        return self.key_confirmed and self.teachers_confirmed

    def apply_placement(self, user_id: int, data: dict) -> None:
        if data.get("isKeyHider"):
            self.key_hider_user_id = int(user_id)
            self.key_position = vector3_from(data.get("keyPosition"))
        else:
            self.teacher_placer_user_id = int(user_id)
            self.teacher_positions_x = [float(value) for value in data.get("teacherPositionsX", [])]
            self.teacher_positions_y = [float(value) for value in data.get("teacherPositionsY", [])]
            self.teacher_positions_z = [float(value) for value in data.get("teacherPositionsZ", [])]

        if self.is_finalized and self.finalized_at <= 0:
            self.finalized_at = time()

    def as_snapshot_payload(self, lobby_id: int) -> dict:
        return {
            "type": "setup_snapshot",
            "lobbyId": lobby_id,
            "isFinalized": self.is_finalized,
            "keyHiderUserId": self.key_hider_user_id,
            "teacherPlacerUserId": self.teacher_placer_user_id,
            "keyPosition": self.key_position,
            "teacherPositionsX": self.teacher_positions_x,
            "teacherPositionsY": self.teacher_positions_y,
            "teacherPositionsZ": self.teacher_positions_z,
            "serverTime": self.finalized_at or time(),
        }

    def as_finalized_payload(self, lobby_id: int) -> dict:
        payload = self.as_snapshot_payload(lobby_id)
        payload["type"] = "setup_finalized"
        return payload


@dataclass
class TeacherRuntimeState:
    teacher_id: str
    authoritative_user_id: int = 0
    seq: int = 0
    position: list[float] = field(default_factory=lambda: [0, 0, 0])
    rotation: list[float] = field(default_factory=lambda: [0, 0, 0])
    ai_state: str = "Wander"
    can_see_player: bool = False
    last_known_player_position: list[float] = field(default_factory=lambda: [0, 0, 0])
    last_updated: float = field(default_factory=time)

    def apply_update(self, user_id: int, data: dict) -> None:
        self.authoritative_user_id = int(user_id)
        self.seq = int(data["seq"])
        self.position = vector3_from(data.get("position"), self.position)
        self.rotation = vector3_from(data.get("rotation"), self.rotation)
        self.ai_state = str(data.get("aiState", self.ai_state))[:64]
        self.can_see_player = bool(data.get("canSeePlayer", self.can_see_player))
        self.last_known_player_position = vector3_from(
            data.get("lastKnownPlayerPosition"),
            self.last_known_player_position,
        )
        self.last_updated = time()

    def as_payload(self, lobby_id: int) -> dict:
        return {
            "type": "teacher_state",
            "lobbyId": lobby_id,
            "teacherId": self.teacher_id,
            "authoritativeUserId": self.authoritative_user_id,
            "seq": self.seq,
            "position": self.position,
            "rotation": self.rotation,
            "aiState": self.ai_state,
            "canSeePlayer": self.can_see_player,
            "lastKnownPlayerPosition": self.last_known_player_position,
            "serverTime": self.last_updated,
        }


@dataclass
class KeyRuntimeState:
    key_id: str = "objective_exit_key"
    authoritative_user_id: int = 0
    seq: int = 0
    position: list[float] = field(default_factory=lambda: [0, 0, 0])
    rotation: list[float] = field(default_factory=lambda: [0, 0, 0])
    is_held: bool = False
    holder_player_id: str = ""
    last_updated: float = field(default_factory=time)

    def apply_update(self, user_id: int, data: dict) -> None:
        self.key_id = str(data.get("keyId", self.key_id))[:64]
        self.authoritative_user_id = int(user_id)
        self.seq = int(data["seq"])
        self.position = vector3_from(data.get("position"), self.position)
        self.rotation = vector3_from(data.get("rotation"), self.rotation)
        self.is_held = bool(data.get("isHeld", self.is_held))
        self.holder_player_id = str(data.get("holderPlayerId", self.holder_player_id))[:64]
        self.last_updated = time()

    def as_payload(self, lobby_id: int) -> dict:
        return {
            "type": "key_state",
            "lobbyId": lobby_id,
            "keyId": self.key_id,
            "authoritativeUserId": self.authoritative_user_id,
            "seq": self.seq,
            "position": self.position,
            "rotation": self.rotation,
            "isHeld": self.is_held,
            "holderPlayerId": self.holder_player_id,
            "serverTime": self.last_updated,
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
    setup: SetupPlacementRuntimeState = field(default_factory=SetupPlacementRuntimeState)
    teachers: dict[str, TeacherRuntimeState] = field(default_factory=dict)
    key: KeyRuntimeState = field(default_factory=KeyRuntimeState)
    started: bool = False

    def teacher_for(self, teacher_id: str) -> TeacherRuntimeState:
        if teacher_id not in self.teachers:
            self.teachers[teacher_id] = TeacherRuntimeState(teacher_id=teacher_id)
        return self.teachers[teacher_id]


ROOMS: dict[int, RoomRuntimeState] = {}


def get_room(lobby_id: int) -> RoomRuntimeState:
    if lobby_id not in ROOMS:
        ROOMS[lobby_id] = RoomRuntimeState(lobby_id=lobby_id)
    return ROOMS[lobby_id]
