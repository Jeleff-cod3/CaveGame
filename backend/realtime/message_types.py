GAME_STARTED = "game_started"
HEARTBEAT = "heartbeat"
LOBBY_SNAPSHOT = "lobby_snapshot"
MAMMOTH_HEALTH = "mammoth_health"
MAMMOTH_STATE = "mammoth_state"
PING = "ping"
PLAYER_JOINED = "player_joined"
PLAYER_LEFT = "player_left"
PLAYER_READY_CHANGED = "player_ready_changed"
PLAYER_STATE = "player_state"
PONG = "pong"
ROOM_SNAPSHOT = "room_snapshot"

ALLOWED_CLIENT_GAME_TYPES = {PLAYER_STATE, MAMMOTH_HEALTH, MAMMOTH_STATE, PING, HEARTBEAT}
ALLOWED_CLIENT_LOBBY_TYPES = {PING, HEARTBEAT}
