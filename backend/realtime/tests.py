import asyncio

from django.test import SimpleTestCase
from django.test import TransactionTestCase, override_settings

from asgiref.sync import async_to_sync
from channels.routing import URLRouter
from channels.testing import WebsocketCommunicator
from django.contrib.auth import get_user_model
from rest_framework.authtoken.models import Token

from .auth import TokenAuthMiddlewareStack
from .room_state import ROOMS
from .room_state import MammothRuntimeState, RoomRuntimeState
from .routing import websocket_urlpatterns
from .validators import (
    is_valid_key_state,
    is_valid_mammoth_health,
    is_valid_mammoth_state,
    is_valid_player_state,
    is_valid_setup_placement,
    is_valid_teacher_state,
    is_valid_voice_presence,
    is_valid_webrtc_answer,
    is_valid_webrtc_ice,
    is_valid_webrtc_offer,
    is_vec3,
)
from .voice_runtime import VoiceRoomRuntimeState, voice_gain_for_distance


TEST_CHANNEL_LAYERS = {
    "default": {
        "BACKEND": "channels.layers.InMemoryChannelLayer",
    }
}


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


class VoiceSignalingValidatorTests(SimpleTestCase):
    def test_valid_webrtc_offer_shape(self):
        self.assertTrue(
            is_valid_webrtc_offer(
                {
                    "type": "webrtc_offer",
                    "targetPlayerId": "player_12",
                    "sdpType": "offer",
                    "sdp": "v=0\r\na=sendrecv",
                }
            )
        )

    def test_valid_webrtc_answer_shape(self):
        self.assertTrue(
            is_valid_webrtc_answer(
                {
                    "type": "webrtc_answer",
                    "targetPlayerId": "player_7",
                    "sdpType": "answer",
                    "sdp": "v=0\r\na=sendrecv",
                }
            )
        )

    def test_valid_webrtc_ice_shape(self):
        self.assertTrue(
            is_valid_webrtc_ice(
                {
                    "type": "webrtc_ice",
                    "targetPlayerId": "player_12",
                    "candidate": "candidate:842163049 1 udp 1677729535 192.0.2.1 54321 typ srflx",
                    "sdpMid": "0",
                    "sdpMLineIndex": 0,
                }
            )
        )

    def test_rejects_malformed_target_player_id(self):
        self.assertFalse(
            is_valid_webrtc_offer(
                {
                    "type": "webrtc_offer",
                    "targetPlayerId": "not-a-player-id",
                    "sdpType": "offer",
                    "sdp": "v=0",
                }
            )
        )

    def test_rejects_missing_sdp(self):
        self.assertFalse(
            is_valid_webrtc_answer(
                {
                    "type": "webrtc_answer",
                    "targetPlayerId": "player_7",
                    "sdpType": "answer",
                }
            )
        )

    def test_rejects_missing_candidate(self):
        self.assertFalse(
            is_valid_webrtc_ice(
                {
                    "type": "webrtc_ice",
                    "targetPlayerId": "player_12",
                    "sdpMid": "0",
                    "sdpMLineIndex": 0,
                }
            )
        )

    def test_valid_voice_presence_shape(self):
        self.assertTrue(
            is_valid_voice_presence(
                {
                    "type": "voice_presence",
                    "isReady": True,
                    "isMuted": False,
                }
            )
        )


class VoiceRuntimeStateTests(SimpleTestCase):
    def test_audibility_includes_nearby_players(self):
        voice = VoiceRoomRuntimeState(lobby_id=3)
        voice.connect_player("player_1", 1, "channel-a", [0, 0, 0])
        voice.connect_player("player_2", 2, "channel-b", [4, 0, 0])

        audible = voice.audible_peers_for("player_1")

        self.assertEqual(len(audible), 1)
        self.assertEqual(audible[0]["playerId"], "player_2")
        self.assertAlmostEqual(audible[0]["distance"], 4.0)
        self.assertGreater(audible[0]["gain"], 0)

    def test_audibility_excludes_far_players(self):
        voice = VoiceRoomRuntimeState(lobby_id=3)
        voice.connect_player("player_1", 1, "channel-a", [0, 0, 0])
        voice.connect_player("player_2", 2, "channel-b", [30, 0, 0])

        self.assertEqual(voice.audible_peers_for("player_1"), [])

    def test_hysteresis_keeps_existing_peer_until_keep_alive_distance(self):
        voice = VoiceRoomRuntimeState(lobby_id=3)
        voice.connect_player("player_1", 1, "channel-a", [0, 0, 0])
        voice.connect_player("player_2", 2, "channel-b", [17, 0, 0])
        self.assertEqual(voice.audible_peers_for("player_1")[0]["playerId"], "player_2")

        voice.update_position("player_2", [20, 0, 0])
        audible = voice.audible_peers_for("player_1")

        self.assertEqual(len(audible), 1)
        self.assertEqual(audible[0]["playerId"], "player_2")
        self.assertEqual(audible[0]["gain"], 0.0)

    def test_hysteresis_drops_peer_after_keep_alive_distance(self):
        voice = VoiceRoomRuntimeState(lobby_id=3)
        voice.connect_player("player_1", 1, "channel-a", [0, 0, 0])
        voice.connect_player("player_2", 2, "channel-b", [17, 0, 0])
        voice.audible_peers_for("player_1")

        voice.update_position("player_2", [23, 0, 0])

        self.assertEqual(voice.audible_peers_for("player_1"), [])

    def test_cleanup_removes_disconnected_player(self):
        voice = VoiceRoomRuntimeState(lobby_id=3)
        voice.connect_player("player_1", 1, "channel-a", [0, 0, 0])
        voice.connect_player("player_2", 2, "channel-b", [4, 0, 0])
        voice.audible_peers_for("player_1")

        removed_player_id = voice.disconnect_player(channel_name="channel-b")

        self.assertEqual(removed_player_id, "player_2")
        self.assertNotIn("player_2", voice.peers_by_player_id)
        self.assertNotIn("channel-b", voice.player_id_by_channel_name)
        self.assertNotIn("player_2", voice.audible_peers_by_player_id["player_1"])

    def test_room_state_has_voice_runtime_state(self):
        room = RoomRuntimeState(lobby_id=8)

        self.assertEqual(room.voice.lobby_id, 8)

    def test_voice_gain_fades_between_min_and_max_distance(self):
        self.assertEqual(voice_gain_for_distance(1.0), 1.0)
        self.assertEqual(voice_gain_for_distance(18.0), 0.0)
        self.assertAlmostEqual(voice_gain_for_distance(10.0), 0.5)

    def test_voice_interest_snapshot_cadence_limits_updates(self):
        voice = VoiceRoomRuntimeState(lobby_id=3)

        self.assertTrue(voice.should_send_interest_snapshots(10.0))
        self.assertFalse(voice.should_send_interest_snapshots(10.1))
        self.assertTrue(voice.should_send_interest_snapshots(10.21))


@override_settings(CHANNEL_LAYERS=TEST_CHANNEL_LAYERS)
class GameVoiceWebSocketTests(TransactionTestCase):
    def setUp(self):
        ROOMS.clear()
        user_model = get_user_model()
        self.host = user_model.objects.create_user(username="voice_host")
        self.guest = user_model.objects.create_user(username="voice_guest")
        self.third = user_model.objects.create_user(username="voice_third")
        self.host_token = Token.objects.create(user=self.host)
        self.guest_token = Token.objects.create(user=self.guest)
        self.third_token = Token.objects.create(user=self.third)
        self.lobby = self.create_started_lobby("VOICE1", self.host, self.guest)
        self.other_lobby = self.create_started_lobby("VOICE2", self.third)
        self.application = TokenAuthMiddlewareStack(URLRouter(websocket_urlpatterns))

    def tearDown(self):
        ROOMS.clear()

    def create_started_lobby(self, code, *users):
        from lobbies.models import Lobby, LobbyMember

        lobby = Lobby.objects.create(
            code=code,
            host=users[0],
            max_players=max(4, len(users)),
            is_started=True,
        )
        for slot, user in enumerate(users):
            LobbyMember.objects.create(
                lobby=lobby,
                user=user,
                player_slot=slot,
                is_ready=True,
            )
        return lobby

    def test_same_lobby_players_can_relay_mock_offer_answer_and_ice(self):
        async_to_sync(self.run_same_lobby_relay_flow)()

    async def run_same_lobby_relay_flow(self):
        host_socket = await self.open_game_socket(self.lobby.id, self.host_token.key)
        guest_socket = await self.open_game_socket(self.lobby.id, self.guest_token.key)

        await host_socket.send_json_to(
            {
                "type": "webrtc_offer",
                "targetPlayerId": f"player_{self.guest.id}",
                "fromPlayerId": "player_999",
                "sdpType": "offer",
                "sdp": "v=0\r\na=sendrecv",
            }
        )
        offer = await self.receive_until_type(guest_socket, "webrtc_offer")
        self.assertEqual(offer["fromPlayerId"], f"player_{self.host.id}")
        self.assertEqual(offer["targetPlayerId"], f"player_{self.guest.id}")
        self.assertEqual(offer["sdpType"], "offer")

        await guest_socket.send_json_to(
            {
                "type": "webrtc_answer",
                "targetPlayerId": f"player_{self.host.id}",
                "sdpType": "answer",
                "sdp": "v=0\r\na=sendrecv",
            }
        )
        answer = await self.receive_until_type(host_socket, "webrtc_answer")
        self.assertEqual(answer["fromPlayerId"], f"player_{self.guest.id}")

        await host_socket.send_json_to(
            {
                "type": "webrtc_ice",
                "targetPlayerId": f"player_{self.guest.id}",
                "candidate": "candidate:842163049 1 udp 1677729535 192.0.2.1 54321 typ srflx",
                "sdpMid": "0",
                "sdpMLineIndex": 0,
            }
        )
        ice = await self.receive_until_type(guest_socket, "webrtc_ice")
        self.assertEqual(ice["fromPlayerId"], f"player_{self.host.id}")
        self.assertEqual(ice["candidate"].split(" ", 1)[0], "candidate:842163049")

        await self.disconnect_socket(host_socket)
        await self.disconnect_socket(guest_socket)

    def test_unauthenticated_websocket_cannot_use_voice_messages(self):
        async_to_sync(self.run_unauthenticated_socket_rejected)()

    async def run_unauthenticated_socket_rejected(self):
        socket = WebsocketCommunicator(self.application, f"/ws/game/{self.lobby.id}/")
        connected, _ = await socket.connect()

        self.assertFalse(connected)

    def test_cross_lobby_signaling_is_ignored(self):
        async_to_sync(self.run_cross_lobby_signal_flow)()

    async def run_cross_lobby_signal_flow(self):
        host_socket = await self.open_game_socket(self.lobby.id, self.host_token.key)
        third_socket = await self.open_game_socket(self.other_lobby.id, self.third_token.key)

        await host_socket.send_json_to(
            {
                "type": "webrtc_offer",
                "targetPlayerId": f"player_{self.third.id}",
                "sdpType": "offer",
                "sdp": "v=0",
            }
        )

        self.assertIsNone(await self.receive_optional_json(third_socket))
        await self.disconnect_socket(host_socket)
        await self.disconnect_socket(third_socket)

    def test_target_not_in_room_is_ignored_safely(self):
        async_to_sync(self.run_missing_target_signal_flow)()

    async def run_missing_target_signal_flow(self):
        host_socket = await self.open_game_socket(self.lobby.id, self.host_token.key)

        await host_socket.send_json_to(
            {
                "type": "webrtc_offer",
                "targetPlayerId": "player_99999",
                "sdpType": "offer",
                "sdp": "v=0",
            }
        )

        self.assertIsNone(await self.receive_optional_json(host_socket))
        await self.disconnect_socket(host_socket)

    def test_voice_interest_snapshot_changes_when_players_move_near_and_far(self):
        async_to_sync(self.run_voice_interest_snapshot_flow)()

    async def run_voice_interest_snapshot_flow(self):
        host_socket = await self.open_game_socket(self.lobby.id, self.host_token.key)
        guest_socket = await self.open_game_socket(self.lobby.id, self.guest_token.key)

        await host_socket.send_json_to(self.player_state_for(self.host, 1, [0, 0, 0]))
        await guest_socket.send_json_to(self.player_state_for(self.guest, 1, [4, 0, 0]))
        near_snapshot = await self.receive_until_type(
            host_socket,
            "voice_interest_snapshot",
            predicate=lambda message: any(
                peer.get("playerId") == f"player_{self.guest.id}"
                for peer in message.get("audiblePeers", [])
            ),
        )
        self.assertEqual(near_snapshot["selfPlayerId"], f"player_{self.host.id}")
        self.assertEqual(near_snapshot["audiblePeers"][0]["playerId"], f"player_{self.guest.id}")

        await guest_socket.send_json_to(self.player_state_for(self.guest, 2, [40, 0, 0]))
        await self.receive_until_type(
            host_socket,
            "player_state",
            predicate=lambda message: message.get("playerId") == f"player_{self.guest.id}"
            and message.get("position") == [40, 0, 0],
        )

        room = ROOMS[self.lobby.id]
        room.voice.last_interest_snapshot_sent_at = 0
        await host_socket.send_json_to(self.player_state_for(self.host, 2, [0, 0, 0]))
        far_snapshot = await self.receive_until_type(
            host_socket,
            "voice_interest_snapshot",
            predicate=lambda message: message.get("audiblePeers") == [],
        )
        self.assertEqual(far_snapshot["audiblePeers"], [])

        await self.disconnect_socket(host_socket)
        await self.disconnect_socket(guest_socket)

    def test_disconnect_triggers_voice_cleanup_and_peer_left(self):
        async_to_sync(self.run_disconnect_cleanup_flow)()

    async def run_disconnect_cleanup_flow(self):
        host_socket = await self.open_game_socket(self.lobby.id, self.host_token.key)
        guest_socket = await self.open_game_socket(self.lobby.id, self.guest_token.key)

        await self.disconnect_socket(guest_socket)
        peer_left = await self.receive_until_type(host_socket, "voice_peer_left")

        self.assertEqual(peer_left["playerId"], f"player_{self.guest.id}")
        self.assertNotIn(f"player_{self.guest.id}", ROOMS[self.lobby.id].voice.peers_by_player_id)
        await self.disconnect_socket(host_socket)

    async def open_game_socket(self, lobby_id: int, token: str):
        socket = WebsocketCommunicator(self.application, f"/ws/game/{lobby_id}/?token={token}")
        connected, _ = await socket.connect()
        self.assertTrue(connected)
        await self.receive_until_type(socket, "room_snapshot")
        return socket

    async def receive_until_type(self, socket, expected_type: str, attempts: int = 8, predicate=None):
        for _ in range(attempts):
            message = await socket.receive_json_from(timeout=1)
            if message.get("type") == expected_type and (predicate is None or predicate(message)):
                return message
        self.fail(f"Did not receive {expected_type}")

    async def receive_optional_json(self, socket, timeout: float = 0.1):
        try:
            return await socket.receive_json_from(timeout=timeout)
        except TimeoutError:
            return None

    async def disconnect_socket(self, socket):
        try:
            await socket.disconnect()
        except asyncio.CancelledError:
            pass

    @staticmethod
    def player_state_for(user, seq: int, position: list[float]) -> dict:
        return {
            "type": "player_state",
            "playerId": f"player_{user.id}",
            "userId": user.id,
            "seq": seq,
            "position": position,
            "rotation": [0, 0, 0],
            "velocity": [0, 0, 0],
        }
