from math import isfinite

from .message_types import PLAYER_STATE


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
