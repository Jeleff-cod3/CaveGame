import asyncio
import json
import logging
import os
from time import time

from channels.db import database_sync_to_async
from channels.generic.websocket import AsyncWebsocketConsumer

from lobbies.models import Lobby, LobbyMember

from .message_types import (
    HEARTBEAT,
    LOBBY_SNAPSHOT,
    MAMMOTH_HEALTH,
    MAMMOTH_STATE,
    PING,
    PLAYER_JOINED,
    PLAYER_LEFT,
    PLAYER_STATE,
    PONG,
    ROOM_SNAPSHOT,
)
from .room_state import PlayerRuntimeState, get_room
from .validators import is_valid_mammoth_health, is_valid_mammoth_state, is_valid_player_state

JSON_SEPARATORS = (",", ":")
HEARTBEAT_INTERVAL = int(os.environ.get("WS_HEARTBEAT_INTERVAL", "5"))
logger = logging.getLogger(__name__)


def is_closed_transport_error(exception: Exception) -> bool:
    text = str(exception).lower()
    return "closed protocol" in text or "socket is already closed" in text


async def send_to_game_room(room, payload: dict, sender_channel_name: str | None = None) -> None:
    stale_channels = []
    for channel_name, consumer in list(room.connections.items()):
        if channel_name == sender_channel_name:
            continue

        try:
            await consumer.send_json(payload)
        except Exception:
            stale_channels.append(channel_name)

    for channel_name in stale_channels:
        room.connections.pop(channel_name, None)


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
    heartbeat_task: asyncio.Task | None = None

    async def connect(self):
        try:
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

            self.heartbeat_task = asyncio.ensure_future(self._heartbeat_loop())

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
        except Exception:
            logger.exception(
                "LobbyConsumer.connect failed (lobby=%s user=%s)",
                getattr(self, "lobby_id", None),
                getattr(getattr(self, "user", None), "id", None),
            )
            await self.close()

    async def disconnect(self, close_code):
        if self.heartbeat_task is not None:
            self.heartbeat_task.cancel()

        if not hasattr(self, "room_group_name"):
            return

        try:
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
        except Exception:
            logger.exception(
                "LobbyConsumer.disconnect failed (lobby=%s user=%s close_code=%s)",
                getattr(self, "lobby_id", None),
                getattr(getattr(self, "user", None), "id", None),
                close_code,
            )

    async def receive(self, text_data=None, bytes_data=None):
        if text_data is None:
            return

        try:
            data = json.loads(text_data)
        except json.JSONDecodeError:
            return

        try:
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
        except Exception:
            logger.exception(
                "LobbyConsumer.receive failed (lobby=%s user=%s type=%s)",
                getattr(self, "lobby_id", None),
                getattr(getattr(self, "user", None), "id", None),
                data.get("type"),
            )

    async def broadcast_event(self, event):
        try:
            await self.send_json(event["payload"])
        except Exception:
            logger.exception(
                "LobbyConsumer.broadcast_event failed (lobby=%s user=%s event_type=%s)",
                getattr(self, "lobby_id", None),
                getattr(getattr(self, "user", None), "id", None),
                event.get("payload", {}).get("type"),
            )

    async def _heartbeat_loop(self):
        try:
            while True:
                await asyncio.sleep(HEARTBEAT_INTERVAL)
                await self.send(text_data=json.dumps({"type": HEARTBEAT, "serverTime": time()}, separators=JSON_SEPARATORS))
        except asyncio.CancelledError:
            pass
        except Exception:
            logger.exception(
                "LobbyConsumer._heartbeat_loop failed (lobby=%s user=%s)",
                getattr(self, "lobby_id", None),
                getattr(getattr(self, "user", None), "id", None),
            )

    async def send_json(self, payload: dict):
        try:
            await self.send(text_data=json.dumps(payload, separators=JSON_SEPARATORS))
        except Exception as exception:
            if is_closed_transport_error(exception):
                logger.info(
                    "LobbyConsumer.send_json skipped closed transport (lobby=%s user=%s payload_type=%s)",
                    getattr(self, "lobby_id", None),
                    getattr(getattr(self, "user", None), "id", None),
                    payload.get("type") if isinstance(payload, dict) else None,
                )
                return
            logger.exception(
                "LobbyConsumer.send_json failed (lobby=%s user=%s payload_type=%s)",
                getattr(self, "lobby_id", None),
                getattr(getattr(self, "user", None), "id", None),
                payload.get("type") if isinstance(payload, dict) else None,
            )
            return


class GameConsumer(AsyncWebsocketConsumer):
    heartbeat_task: asyncio.Task | None = None

    async def connect(self):
        try:
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

            room = get_room(self.lobby_id)
            room.started = True
            room.players[self.user.id] = PlayerRuntimeState(
                user_id=self.user.id,
                player_id=self.player_id,
                channel_name=self.channel_name,
            )
            room.connections[self.channel_name] = self

            await self.accept()
            self.heartbeat_task = asyncio.ensure_future(self._heartbeat_loop())
            await self.send_room_snapshot()

            await send_to_game_room(
                room,
                {
                    "type": PLAYER_JOINED,
                    "lobbyId": self.lobby_id,
                    "playerId": self.player_id,
                    "userId": self.user.id,
                    "slot": self.member["slot"],
                },
                self.channel_name,
            )
        except Exception:
            logger.exception(
                "GameConsumer.connect failed (lobby=%s user=%s)",
                getattr(self, "lobby_id", None),
                getattr(getattr(self, "user", None), "id", None),
            )
            await self.close()

    async def disconnect(self, close_code):
        if self.heartbeat_task is not None:
            self.heartbeat_task.cancel()

        if not hasattr(self, "room_group_name"):
            return

        try:
            room = get_room(self.lobby_id)
            if hasattr(self, "user") and self.user.id in room.players:
                del room.players[self.user.id]

            room.connections.pop(self.channel_name, None)

            if hasattr(self, "player_id"):
                await send_to_game_room(
                    room,
                    {
                        "type": PLAYER_LEFT,
                        "lobbyId": self.lobby_id,
                        "playerId": self.player_id,
                        "userId": self.user.id,
                    },
                )
        except Exception:
            logger.exception(
                "GameConsumer.disconnect failed (lobby=%s user=%s close_code=%s)",
                getattr(self, "lobby_id", None),
                getattr(getattr(self, "user", None), "id", None),
                close_code,
            )

    async def receive(self, text_data=None, bytes_data=None):
        if text_data is None:
            return

        try:
            data = json.loads(text_data)
        except json.JSONDecodeError:
            return

        try:
            message_type = data.get("type")

            if message_type == PLAYER_STATE:
                await self.handle_player_state(data)
            elif message_type == MAMMOTH_HEALTH:
                await self.handle_mammoth_health(data)
            elif message_type == MAMMOTH_STATE:
                await self.handle_mammoth_state(data)
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
        except Exception:
            logger.exception(
                "GameConsumer.receive failed (lobby=%s user=%s type=%s)",
                getattr(self, "lobby_id", None),
                getattr(getattr(self, "user", None), "id", None),
                data.get("type"),
            )

    async def handle_player_state(self, data):
        try:
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

            await send_to_game_room(room, payload, self.channel_name)
        except Exception:
            logger.exception(
                "GameConsumer.handle_player_state failed (lobby=%s user=%s)",
                getattr(self, "lobby_id", None),
                getattr(getattr(self, "user", None), "id", None),
            )

    async def handle_mammoth_health(self, data):
        try:
            if not is_valid_mammoth_health(data):
                return

            room = get_room(self.lobby_id)
            room.mammoth.apply_health_update(
                reported_current_health=data["currentHealth"],
                reported_max_health=data["maxHealth"],
                damage=data.get("damage", 0),
            )

            await send_to_game_room(room, room.mammoth.as_state_payload(self.lobby_id))
        except Exception:
            logger.exception(
                "GameConsumer.handle_mammoth_health failed (lobby=%s user=%s)",
                getattr(self, "lobby_id", None),
                getattr(getattr(self, "user", None), "id", None),
            )

    async def handle_mammoth_state(self, data):
        try:
            if not is_valid_mammoth_state(data):
                return

            if self.member.get("slot") != 0:
                return

            room = get_room(self.lobby_id)
            room.mammoth.apply_state_update(
                authoritative_user_id=self.user.id,
                position=data["position"],
                rotation=data["rotation"],
                reported_current_health=data["currentHealth"],
                reported_max_health=data["maxHealth"],
            )

            await send_to_game_room(room, room.mammoth.as_state_payload(self.lobby_id))
        except Exception:
            logger.exception(
                "GameConsumer.handle_mammoth_state failed (lobby=%s user=%s)",
                getattr(self, "lobby_id", None),
                getattr(getattr(self, "user", None), "id", None),
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
                "mammothState": room.mammoth.as_state_payload(self.lobby_id),
                "mammothHealth": room.mammoth.as_health_payload(self.lobby_id),
            }
        )

    async def broadcast_event(self, event):
        if event.get("sender_channel_name") == self.channel_name:
            return

        await self.send_json(event["payload"])

    async def _heartbeat_loop(self):
        try:
            while True:
                await asyncio.sleep(HEARTBEAT_INTERVAL)
                await self.send(text_data=json.dumps({"type": HEARTBEAT, "serverTime": time()}, separators=JSON_SEPARATORS))
        except asyncio.CancelledError:
            pass
        except Exception:
            logger.exception(
                "GameConsumer._heartbeat_loop failed (lobby=%s user=%s)",
                getattr(self, "lobby_id", None),
                getattr(getattr(self, "user", None), "id", None),
            )

    async def send_json(self, payload: dict):
        try:
            await self.send(text_data=json.dumps(payload, separators=JSON_SEPARATORS))
        except Exception as exception:
            if is_closed_transport_error(exception):
                logger.info(
                    "GameConsumer.send_json skipped closed transport (lobby=%s user=%s payload_type=%s)",
                    getattr(self, "lobby_id", None),
                    getattr(getattr(self, "user", None), "id", None),
                    payload.get("type") if isinstance(payload, dict) else None,
                )
                return
            logger.exception(
                "GameConsumer.send_json failed (lobby=%s user=%s payload_type=%s)",
                getattr(self, "lobby_id", None),
                getattr(getattr(self, "user", None), "id", None),
                payload.get("type") if isinstance(payload, dict) else None,
            )
            return
