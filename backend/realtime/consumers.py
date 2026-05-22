import json
from time import time

from channels.db import database_sync_to_async
from channels.generic.websocket import AsyncWebsocketConsumer

from lobbies.models import Lobby, LobbyMember

from .message_types import (
    HEARTBEAT,
    LOBBY_SNAPSHOT,
    PING,
    PLAYER_JOINED,
    PLAYER_LEFT,
    PLAYER_STATE,
    PONG,
    ROOM_SNAPSHOT,
)
from .room_state import PlayerRuntimeState, get_room
from .validators import is_valid_player_state

JSON_SEPARATORS = (",", ":")


@database_sync_to_async
def get_lobby_snapshot(lobby_id: int) -> dict | None:
    try:
        lobby = Lobby.objects.prefetch_related("members__user").get(pk=lobby_id)
    except Lobby.DoesNotExist:
        return None

    return {
        "type": LOBBY_SNAPSHOT,
        "lobbyId": lobby.id,
        "code": lobby.code,
        "hostId": lobby.host_id,
        "isStarted": lobby.is_started,
        "players": [
            {
                "playerId": member.player_id,
                "userId": member.user_id,
                "username": member.user.username,
                "slot": member.player_slot,
                "isReady": member.is_ready,
            }
            for member in lobby.members.all()
        ],
    }


@database_sync_to_async
def get_member_details(lobby_id: int, user_id: int) -> dict | None:
    try:
        member = LobbyMember.objects.select_related("lobby", "user").get(lobby_id=lobby_id, user_id=user_id)
    except LobbyMember.DoesNotExist:
        return None

    return {
        "lobbyId": lobby_id,
        "userId": user_id,
        "username": member.user.username,
        "playerId": member.player_id,
        "slot": member.player_slot,
        "isStarted": member.lobby.is_started,
    }


class LobbyConsumer(AsyncWebsocketConsumer):
    async def connect(self):
        self.lobby_id = int(self.scope["url_route"]["kwargs"]["lobby_id"])
        self.room_group_name = f"lobby_{self.lobby_id}"
        self.user = self.scope["user"]

        if not self.user.is_authenticated:
            await self.close()
            return

        self.member = await get_member_details(self.lobby_id, self.user.id)
        if self.member is None:
            await self.close()
            return

        await self.channel_layer.group_add(self.room_group_name, self.channel_name)
        await self.accept()

        snapshot = await get_lobby_snapshot(self.lobby_id)
        if snapshot is not None:
            await self.send_json(snapshot)

        await self.channel_layer.group_send(
            self.room_group_name,
            {
                "type": "broadcast_event",
                "payload": {
                    "type": PLAYER_JOINED,
                    "lobbyId": self.lobby_id,
                    "playerId": self.member["playerId"],
                    "userId": self.user.id,
                    "slot": self.member["slot"],
                },
            },
        )

    async def disconnect(self, close_code):
        if not hasattr(self, "room_group_name"):
            return

        await self.channel_layer.group_discard(self.room_group_name, self.channel_name)

        if hasattr(self, "member"):
            await self.channel_layer.group_send(
                self.room_group_name,
                {
                    "type": "broadcast_event",
                    "payload": {
                        "type": PLAYER_LEFT,
                        "lobbyId": self.lobby_id,
                        "playerId": self.member["playerId"],
                        "userId": self.user.id,
                    },
                },
            )

    async def receive(self, text_data=None, bytes_data=None):
        if text_data is None:
            return

        try:
            data = json.loads(text_data)
        except json.JSONDecodeError:
            return

        message_type = data.get("type")
        if message_type == PING:
            await self.send_json(
                {
                    "type": PONG,
                    "clientTime": data.get("clientTime"),
                    "serverTime": time(),
                }
            )
        elif message_type == HEARTBEAT:
            await self.send_json({"type": HEARTBEAT, "serverTime": time()})

    async def broadcast_event(self, event):
        await self.send_json(event["payload"])

    async def send_json(self, payload: dict):
        await self.send(text_data=json.dumps(payload, separators=JSON_SEPARATORS))


class GameConsumer(AsyncWebsocketConsumer):
    async def connect(self):
        self.lobby_id = int(self.scope["url_route"]["kwargs"]["lobby_id"])
        self.room_group_name = f"game_{self.lobby_id}"
        self.user = self.scope["user"]

        if not self.user.is_authenticated:
            await self.close()
            return

        self.member = await get_member_details(self.lobby_id, self.user.id)
        if self.member is None or not self.member["isStarted"]:
            await self.close()
            return

        self.player_id = self.member["playerId"]

        await self.channel_layer.group_add(self.room_group_name, self.channel_name)

        room = get_room(self.lobby_id)
        room.started = True
        room.players[self.user.id] = PlayerRuntimeState(
            user_id=self.user.id,
            player_id=self.player_id,
            channel_name=self.channel_name,
        )

        await self.accept()
        await self.send_room_snapshot()

        await self.channel_layer.group_send(
            self.room_group_name,
            {
                "type": "broadcast_event",
                "payload": {
                    "type": PLAYER_JOINED,
                    "lobbyId": self.lobby_id,
                    "playerId": self.player_id,
                    "userId": self.user.id,
                    "slot": self.member["slot"],
                },
                "sender_channel_name": self.channel_name,
            },
        )

    async def disconnect(self, close_code):
        if not hasattr(self, "room_group_name"):
            return

        room = get_room(self.lobby_id)
        if hasattr(self, "user") and self.user.id in room.players:
            del room.players[self.user.id]

        await self.channel_layer.group_discard(self.room_group_name, self.channel_name)

        if hasattr(self, "player_id"):
            await self.channel_layer.group_send(
                self.room_group_name,
                {
                    "type": "broadcast_event",
                    "payload": {
                        "type": PLAYER_LEFT,
                        "lobbyId": self.lobby_id,
                        "playerId": self.player_id,
                        "userId": self.user.id,
                    },
                },
            )

    async def receive(self, text_data=None, bytes_data=None):
        if text_data is None:
            return

        try:
            data = json.loads(text_data)
        except json.JSONDecodeError:
            return

        message_type = data.get("type")

        if message_type == PLAYER_STATE:
            await self.handle_player_state(data)
        elif message_type == PING:
            await self.send_json(
                {
                    "type": PONG,
                    "clientTime": data.get("clientTime"),
                    "serverTime": time(),
                }
            )
        elif message_type == HEARTBEAT:
            await self.send_json({"type": HEARTBEAT, "serverTime": time()})

    async def handle_player_state(self, data):
        if not is_valid_player_state(data):
            return

        room = get_room(self.lobby_id)
        player = room.players.get(self.user.id)
        if player is None:
            return

        client_player_id = data.get("playerId")
        if client_player_id is not None and client_player_id != player.player_id:
            return

        seq = data["seq"]
        if seq <= player.last_seq:
            return

        now = time()
        if not player.can_accept_state_message(now):
            return

        player.last_seq = seq
        player.last_seen = now
        player.position = data["position"]
        player.rotation = data["rotation"]
        player.velocity = data["velocity"]
        player.animation_state = data.get("animationState", player.animation_state)

        payload = {
            "type": PLAYER_STATE,
            **player.as_payload(),
        }

        await self.channel_layer.group_send(
            self.room_group_name,
            {
                "type": "broadcast_event",
                "payload": payload,
                "sender_channel_name": self.channel_name,
            },
        )

    async def send_room_snapshot(self):
        room = get_room(self.lobby_id)
        await self.send_json(
            {
                "type": ROOM_SNAPSHOT,
                "lobbyId": self.lobby_id,
                "players": [
                    {
                        "type": PLAYER_STATE,
                        **player.as_payload(),
                    }
                    for player in room.players.values()
                ],
            }
        )

    async def broadcast_event(self, event):
        if event.get("sender_channel_name") == self.channel_name:
            return

        await self.send_json(event["payload"])

    async def send_json(self, payload: dict):
        await self.send(text_data=json.dumps(payload, separators=JSON_SEPARATORS))
