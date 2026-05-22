from random import choices
from string import ascii_uppercase, digits

from asgiref.sync import async_to_sync
from channels.layers import get_channel_layer
from django.db import IntegrityError, transaction
from django.shortcuts import get_object_or_404
from rest_framework import status
from rest_framework.permissions import IsAuthenticated
from rest_framework.response import Response
from rest_framework.views import APIView

from .models import Lobby, LobbyMember
from .serializers import CreateLobbySerializer, LobbyMemberSerializer, LobbySerializer, ReadySerializer


CODE_ALPHABET = ascii_uppercase + digits


def generate_lobby_code() -> str:
    while True:
        code = "".join(choices(CODE_ALPHABET, k=6))
        if not Lobby.objects.filter(code=code).exists():
            return code


def next_available_slot(lobby: Lobby) -> int | None:
    used_slots = set(lobby.members.values_list("player_slot", flat=True))
    for slot in range(lobby.max_players):
        if slot not in used_slots:
            return slot
    return None


def broadcast_lobby_event(lobby_id: int, payload: dict) -> None:
    channel_layer = get_channel_layer()
    async_to_sync(channel_layer.group_send)(
        f"lobby_{lobby_id}",
        {
            "type": "broadcast_event",
            "payload": payload,
        },
    )


class CreateLobbyView(APIView):
    permission_classes = [IsAuthenticated]

    def post(self, request):
        serializer = CreateLobbySerializer(data=request.data)
        serializer.is_valid(raise_exception=True)

        with transaction.atomic():
            lobby = Lobby.objects.create(
                code=generate_lobby_code(),
                host=request.user,
                max_players=serializer.validated_data["maxPlayers"],
            )
            LobbyMember.objects.create(lobby=lobby, user=request.user, player_slot=0)

        lobby = Lobby.objects.prefetch_related("members__user").get(pk=lobby.pk)
        return Response(LobbySerializer(lobby).data, status=status.HTTP_201_CREATED)


class JoinLobbyView(APIView):
    permission_classes = [IsAuthenticated]

    def post(self, request, code: str):
        lobby = get_object_or_404(Lobby.objects.prefetch_related("members__user"), code=code.upper())

        if lobby.is_started:
            return Response({"detail": "Lobby has already started."}, status=status.HTTP_400_BAD_REQUEST)

        existing_member = lobby.members.filter(user=request.user).first()
        if existing_member is not None:
            return Response(
                {
                    "lobby": LobbySerializer(lobby).data,
                    "member": LobbyMemberSerializer(existing_member).data,
                }
            )

        with transaction.atomic():
            lobby = Lobby.objects.select_for_update().get(pk=lobby.pk)
            slot = next_available_slot(lobby)
            if slot is None:
                return Response({"detail": "Lobby is full."}, status=status.HTTP_400_BAD_REQUEST)

            try:
                member = LobbyMember.objects.create(lobby=lobby, user=request.user, player_slot=slot)
            except IntegrityError:
                return Response({"detail": "Could not join lobby."}, status=status.HTTP_409_CONFLICT)

        lobby = Lobby.objects.prefetch_related("members__user").get(pk=lobby.pk)
        return Response(
            {
                "lobby": LobbySerializer(lobby).data,
                "member": LobbyMemberSerializer(member).data,
            },
            status=status.HTTP_201_CREATED,
        )


class ReadyLobbyView(APIView):
    permission_classes = [IsAuthenticated]

    def post(self, request, lobby_id: int):
        lobby = get_object_or_404(Lobby, pk=lobby_id)
        member = get_object_or_404(LobbyMember, lobby=lobby, user=request.user)

        serializer = ReadySerializer(data=request.data)
        serializer.is_valid(raise_exception=True)

        member.is_ready = serializer.validated_data["isReady"]
        member.save(update_fields=["is_ready"])

        payload = {
            "type": "player_ready_changed",
            "lobbyId": lobby.id,
            "playerId": member.player_id,
            "userId": member.user_id,
            "slot": member.player_slot,
            "isReady": member.is_ready,
        }
        broadcast_lobby_event(lobby.id, payload)

        return Response(payload)


class StartLobbyView(APIView):
    permission_classes = [IsAuthenticated]

    def post(self, request, lobby_id: int):
        lobby = get_object_or_404(Lobby.objects.prefetch_related("members__user"), pk=lobby_id)

        if lobby.host_id != request.user.id:
            return Response({"detail": "Only the host can start this lobby."}, status=status.HTTP_403_FORBIDDEN)

        members = list(lobby.members.all())
        if not members:
            return Response({"detail": "Cannot start an empty lobby."}, status=status.HTTP_400_BAD_REQUEST)

        if not all(member.is_ready for member in members):
            return Response({"detail": "All lobby members must be ready before start."}, status=status.HTTP_400_BAD_REQUEST)

        lobby.is_started = True
        lobby.save(update_fields=["is_started"])

        payload = {
            "type": "game_started",
            "lobbyId": lobby.id,
            "mapId": request.data.get("mapId", "test_map"),
            "players": [
                {
                    "playerId": member.player_id,
                    "userId": member.user_id,
                    "slot": member.player_slot,
                }
                for member in members
            ],
        }
        broadcast_lobby_event(lobby.id, payload)

        return Response(payload)


class LobbyDetailView(APIView):
    permission_classes = [IsAuthenticated]

    def get(self, request, lobby_id: int):
        lobby = get_object_or_404(Lobby.objects.prefetch_related("members__user"), pk=lobby_id)

        if not lobby.members.filter(user=request.user).exists():
            return Response({"detail": "You are not a member of this lobby."}, status=status.HTTP_403_FORBIDDEN)

        return Response(LobbySerializer(lobby).data)
