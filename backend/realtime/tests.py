from django.test import SimpleTestCase

from .validators import is_valid_player_state, is_vec3


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
