from math import isfinite

from .message_types import MAMMOTH_HEALTH, MAMMOTH_STATE, PLAYER_STATE


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
