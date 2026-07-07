from math import isfinite
import re

from .message_types import (
    KEY_STATE,
    MAMMOTH_HEALTH,
    MAMMOTH_STATE,
    PLAYER_STATE,
    SETUP_PLACEMENT,
    TEACHER_STATE,
    VOICE_PRESENCE,
    WEBRTC_ANSWER,
    WEBRTC_ICE,
    WEBRTC_OFFER,
)

PLAYER_ID_PATTERN = re.compile(r"^player_[1-9][0-9]*$")
MAX_SDP_LENGTH = 128_000
MAX_ICE_CANDIDATE_LENGTH = 8_192
MAX_SDP_MID_LENGTH = 64


def is_valid_player_id(value) -> bool:
    return isinstance(value, str) and PLAYER_ID_PATTERN.fullmatch(value) is not None


def is_non_empty_limited_string(value, max_length: int) -> bool:
    return isinstance(value, str) and 0 < len(value) <= max_length


def is_non_negative_int(value) -> bool:
    return isinstance(value, int) and not isinstance(value, bool) and value >= 0


def is_vec3(value) -> bool:
    if not isinstance(value, list):
        return False
    if len(value) != 3:
        return False
    return all(isinstance(component, (int, float)) and isfinite(component) for component in value)


def is_valid_player_state(data) -> bool:
    if data.get("type") != PLAYER_STATE:
        return False

    if not isinstance(data.get("seq"), int):
        return False

    if not is_vec3(data.get("position")):
        return False

    if not is_vec3(data.get("rotation")):
        return False

    if not is_vec3(data.get("velocity")):
        return False

    animation_state = data.get("animationState", "idle")
    if not isinstance(animation_state, str) or len(animation_state) > 64:
        return False

    current_health = data.get("currentHealth", 100)
    max_health = data.get("maxHealth", 100)
    is_dead = data.get("isDead", False)
    if not isinstance(current_health, int) or current_health < 0:
        return False
    if not isinstance(max_health, int) or max_health <= 0:
        return False
    if current_health > max_health:
        return False
    if not isinstance(is_dead, bool):
        return False

    return True


def is_float_list(value, expected_length: int | None = None) -> bool:
    if not isinstance(value, list):
        return False
    if expected_length is not None and len(value) != expected_length:
        return False
    return all(isinstance(component, (int, float)) and isfinite(component) for component in value)


def is_valid_setup_placement(data) -> bool:
    if data.get("type") != SETUP_PLACEMENT:
        return False

    player_id = data.get("playerId", "")
    if not isinstance(player_id, str) or len(player_id) > 64:
        return False

    is_key_hider = data.get("isKeyHider")
    if not isinstance(is_key_hider, bool):
        return False

    if is_key_hider:
        return is_vec3(data.get("keyPosition"))

    teacher_x = data.get("teacherPositionsX")
    teacher_y = data.get("teacherPositionsY")
    teacher_z = data.get("teacherPositionsZ")
    if not is_float_list(teacher_x) or not is_float_list(teacher_y) or not is_float_list(teacher_z):
        return False

    return len(teacher_x) > 0 and len(teacher_x) == len(teacher_y) == len(teacher_z)


def is_valid_teacher_state(data) -> bool:
    if data.get("type") != TEACHER_STATE:
        return False

    teacher_id = data.get("teacherId")
    if not isinstance(teacher_id, str) or not teacher_id or len(teacher_id) > 64:
        return False

    if not isinstance(data.get("seq"), int):
        return False

    if not is_vec3(data.get("position")):
        return False

    if not is_vec3(data.get("rotation")):
        return False

    ai_state = data.get("aiState", "Wander")
    if not isinstance(ai_state, str) or len(ai_state) > 64:
        return False

    can_see_player = data.get("canSeePlayer", False)
    if not isinstance(can_see_player, bool):
        return False

    if not is_vec3(data.get("lastKnownPlayerPosition", [0, 0, 0])):
        return False

    return True


def is_valid_key_state(data) -> bool:
    if data.get("type") != KEY_STATE:
        return False

    key_id = data.get("keyId", "objective_exit_key")
    if not isinstance(key_id, str) or len(key_id) > 64:
        return False

    holder_player_id = data.get("holderPlayerId", "")
    if not isinstance(holder_player_id, str) or len(holder_player_id) > 64:
        return False

    if not isinstance(data.get("seq"), int):
        return False

    if not is_vec3(data.get("position")):
        return False

    if not is_vec3(data.get("rotation")):
        return False

    if not isinstance(data.get("isHeld", False), bool):
        return False

    return True


def is_valid_mammoth_health(data) -> bool:
    if data.get("type") != MAMMOTH_HEALTH:
        return False

    enemy_id = data.get("enemyId", "mammoth")
    if not isinstance(enemy_id, str) or len(enemy_id) > 64:
        return False

    current_health = data.get("currentHealth")
    max_health = data.get("maxHealth")
    damage = data.get("damage", 0)

    if not isinstance(current_health, int) or current_health < 0:
        return False

    if not isinstance(max_health, int) or max_health <= 0:
        return False

    if not isinstance(damage, int) or damage < 0:
        return False

    return current_health <= max_health


def is_valid_mammoth_state(data) -> bool:
    if data.get("type") != MAMMOTH_STATE:
        return False

    if not is_valid_mammoth_health(
        {
            "type": MAMMOTH_HEALTH,
            "enemyId": data.get("enemyId", "mammoth"),
            "currentHealth": data.get("currentHealth"),
            "maxHealth": data.get("maxHealth"),
            "damage": data.get("damage", 0),
        }
    ):
        return False

    if not is_vec3(data.get("position")):
        return False

    if not is_vec3(data.get("rotation")):
        return False

    authoritative_user_id = data.get("authoritativeUserId", 0)
    if not isinstance(authoritative_user_id, int) or authoritative_user_id < 0:
        return False

    return True


def is_valid_webrtc_offer(data) -> bool:
    return is_valid_webrtc_sdp_message(data, WEBRTC_OFFER, "offer")


def is_valid_webrtc_answer(data) -> bool:
    return is_valid_webrtc_sdp_message(data, WEBRTC_ANSWER, "answer")


def is_valid_webrtc_sdp_message(data, expected_type: str, expected_sdp_type: str) -> bool:
    if data.get("type") != expected_type:
        return False

    if not is_valid_player_id(data.get("targetPlayerId")):
        return False

    if data.get("sdpType") != expected_sdp_type:
        return False

    if not is_non_empty_limited_string(data.get("sdp"), MAX_SDP_LENGTH):
        return False

    return True


def is_valid_webrtc_ice(data) -> bool:
    if data.get("type") != WEBRTC_ICE:
        return False

    if not is_valid_player_id(data.get("targetPlayerId")):
        return False

    if not is_non_empty_limited_string(data.get("candidate"), MAX_ICE_CANDIDATE_LENGTH):
        return False

    if not is_non_empty_limited_string(data.get("sdpMid"), MAX_SDP_MID_LENGTH):
        return False

    if not is_non_negative_int(data.get("sdpMLineIndex")):
        return False

    return True


def is_valid_voice_presence(data) -> bool:
    if data.get("type") != VOICE_PRESENCE:
        return False

    if not isinstance(data.get("isReady"), bool):
        return False

    if not isinstance(data.get("isMuted", False), bool):
        return False

    return True
