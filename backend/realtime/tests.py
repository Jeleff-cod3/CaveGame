from django.test import SimpleTestCase

from .room_state import MammothRuntimeState
from .validators import is_valid_mammoth_health, is_valid_mammoth_state, is_valid_player_state, is_vec3


class PlayerStateValidatorTests(SimpleTestCase):
    def test_vec3_requires_three_finite_numbers(self):
        self.assertTrue(is_vec3([1, 2.5, 3]))
        self.assertFalse(is_vec3([1, 2]))
        self.assertFalse(is_vec3([1, 2, "3"]))
        self.assertFalse(is_vec3([1, 2, float("inf")]))

    def test_valid_player_state_shape(self):
        self.assertTrue(
            is_valid_player_state(
                {
                    "type": "player_state",
                    "seq": 1,
                    "position": [0, 0, 0],
                    "rotation": [0, 90, 0],
                    "velocity": [0, 0, 4.5],
                    "animationState": "run",
                }
            )
        )

    def test_rejects_non_integer_sequence(self):
        self.assertFalse(
            is_valid_player_state(
                {
                    "type": "player_state",
                    "seq": "1",
                    "position": [0, 0, 0],
                    "rotation": [0, 90, 0],
                    "velocity": [0, 0, 4.5],
                }
            )
        )


class MammothHealthTests(SimpleTestCase):
    def test_valid_mammoth_health_shape(self):
        self.assertTrue(
            is_valid_mammoth_health(
                {
                    "type": "mammoth_health",
                    "enemyId": "mammoth",
                    "currentHealth": 75,
                    "maxHealth": 100,
                    "damage": 25,
                }
            )
        )

    def test_rejects_invalid_mammoth_health_shape(self):
        self.assertFalse(
            is_valid_mammoth_health(
                {
                    "type": "mammoth_health",
                    "enemyId": "mammoth",
                    "currentHealth": 125,
                    "maxHealth": 100,
                    "damage": -5,
                }
            )
        )

    def test_damage_update_uses_server_side_canonical_health(self):
        mammoth = MammothRuntimeState(current_health=100, max_health=100)
        mammoth.apply_health_update(reported_current_health=75, reported_max_health=100, damage=25)
        mammoth.apply_health_update(reported_current_health=75, reported_max_health=100, damage=25)

        self.assertEqual(mammoth.current_health, 50)

    def test_valid_mammoth_state_shape(self):
        self.assertTrue(
            is_valid_mammoth_state(
                {
                    "type": "mammoth_state",
                    "enemyId": "mammoth",
                    "authoritativeUserId": 1,
                    "currentHealth": 75,
                    "maxHealth": 100,
                    "damage": 0,
                    "position": [4, 0, 8],
                    "rotation": [0, 180, 0],
                }
            )
        )

    def test_state_update_preserves_canonical_health(self):
        mammoth = MammothRuntimeState(current_health=100, max_health=100)
        mammoth.apply_health_update(reported_current_health=75, reported_max_health=100, damage=25)
        mammoth.apply_state_update(
            authoritative_user_id=1,
            position=[4, 0, 8],
            rotation=[0, 180, 0],
            reported_current_health=100,
            reported_max_health=100,
        )

        self.assertEqual(mammoth.current_health, 75)
        self.assertEqual(mammoth.position, [4.0, 0.0, 8.0])
        self.assertEqual(mammoth.rotation, [0.0, 180.0, 0.0])
        self.assertEqual(mammoth.authoritative_user_id, 1)
