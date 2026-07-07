GAME_STARTED = "game_started"
HEARTBEAT = "heartbeat"
KEY_STATE = "key_state"
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
SETUP_FINALIZED = "setup_finalized"
SETUP_PLACEMENT = "setup_placement"
SETUP_SNAPSHOT = "setup_snapshot"
TEACHER_STATE = "teacher_state"
VOICE_INTEREST_SNAPSHOT = "voice_interest_snapshot"
VOICE_PEER_LEFT = "voice_peer_left"
VOICE_PRESENCE = "voice_presence"
WEBRTC_ANSWER = "webrtc_answer"
WEBRTC_ICE = "webrtc_ice"
WEBRTC_OFFER = "webrtc_offer"

ALLOWED_CLIENT_GAME_TYPES = {
    PLAYER_STATE,
    MAMMOTH_HEALTH,
    MAMMOTH_STATE,
    SETUP_PLACEMENT,
    TEACHER_STATE,
    VOICE_PRESENCE,
    WEBRTC_ANSWER,
    WEBRTC_ICE,
    WEBRTC_OFFER,
    KEY_STATE,
    PING,
    HEARTBEAT,
}
ALLOWED_CLIENT_LOBBY_TYPES = {PING, HEARTBEAT}
