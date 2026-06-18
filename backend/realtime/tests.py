from django.test import SimpleTestCase

from .room_state import MammothRuntimeState, RoomRuntimeState
from .validators import (
    is_valid_key_state,
    is_valid_mammoth_health,
    is_valid_mammoth_state,
    is_valid_player_state,
    is_valid_setup_placement,
    is_valid_teacher_state,
    is_vec3,
)


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
                    "currentHealth": 70,
                    "maxHealth": 100,
                    "isDead": False,
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


class EscapeBlock9SetupStateTests(SimpleTestCase):
    def test_valid_setup_placement_accepts_key_hider_position(self):
        self.assertTrue(
            is_valid_setup_placement(
                {
                    "type": "setup_placement",
                    "playerId": "player_1",
                    "isKeyHider": True,
                    "keyPosition": [1, 2, 3],
                }
            )
        )

    def test_valid_setup_placement_accepts_teacher_arrays(self):
        self.assertTrue(
            is_valid_setup_placement(
                {
                    "type": "setup_placement",
                    "playerId": "player_2",
                    "isKeyHider": False,
                    "teacherPositionsX": [1, 2],
                    "teacherPositionsY": [0, 0],
                    "teacherPositionsZ": [3, 4],
                }
            )
        )

    def test_room_setup_finalizes_after_both_roles_confirm(self):
        room = RoomRuntimeState(lobby_id=7)
        room.setup.apply_placement(
            10,
            {
                "isKeyHider": True,
                "keyPosition": [1, 0, 2],
            },
        )
        self.assertFalse(room.setup.is_finalized)

        room.setup.apply_placement(
            11,
            {
                "isKeyHider": False,
                "teacherPositionsX": [3],
                "teacherPositionsY": [0],
                "teacherPositionsZ": [4],
            },
        )

        payload = room.setup.as_finalized_payload(7)
        self.assertTrue(room.setup.is_finalized)
        self.assertEqual(payload["type"], "setup_finalized")
        self.assertEqual(payload["keyPosition"], [1.0, 0.0, 2.0])
        self.assertEqual(payload["teacherPositionsX"], [3.0])


class EscapeBlock9RuntimeStateTests(SimpleTestCase):
    def test_valid_teacher_state_shape(self):
        self.assertTrue(
            is_valid_teacher_state(
                {
                    "type": "teacher_state",
                    "teacherId": "frenski",
                    "seq": 4,
                    "position": [1, 0, 2],
                    "rotation": [0, 90, 0],
                    "aiState": "Chase",
                    "canSeePlayer": True,
                    "lastKnownPlayerPosition": [2, 0, 2],
                }
            )
        )

    def test_valid_key_state_shape(self):
        self.assertTrue(
            is_valid_key_state(
                {
                    "type": "key_state",
                    "keyId": "objective_exit_key",
                    "seq": 3,
                    "position": [4, 0, 8],
                    "rotation": [0, 180, 0],
                    "isHeld": True,
                    "holderPlayerId": "player_5",
                }
            )
        )

    def test_teacher_state_payload_preserves_authority_and_state(self):
        room = RoomRuntimeState(lobby_id=9)
        teacher = room.teacher_for("frenski")
        teacher.apply_update(
            12,
            {
                "seq": 5,
                "position": [1, 0, 2],
                "rotation": [0, 45, 0],
                "aiState": "Investigate",
                "canSeePlayer": False,
                "lastKnownPlayerPosition": [5, 0, 6],
            },
        )

        payload = teacher.as_payload(9)
        self.assertEqual(payload["authoritativeUserId"], 12)
        self.assertEqual(payload["teacherId"], "frenski")
        self.assertEqual(payload["aiState"], "Investigate")
        self.assertEqual(payload["position"], [1.0, 0.0, 2.0])


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
