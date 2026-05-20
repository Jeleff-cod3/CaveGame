from rest_framework import serializers

from .models import Lobby, LobbyMember


class LobbyMemberSerializer(serializers.ModelSerializer):
    userId = serializers.IntegerField(source="user_id", read_only=True)
    username = serializers.CharField(source="user.username", read_only=True)
    playerId = serializers.CharField(source="player_id", read_only=True)
    slot = serializers.IntegerField(source="player_slot", read_only=True)
    isReady = serializers.BooleanField(source="is_ready", read_only=True)

    class Meta:
        model = LobbyMember
        fields = ["userId", "username", "playerId", "slot", "isReady", "joined_at"]


class LobbySerializer(serializers.ModelSerializer):
    hostId = serializers.IntegerField(source="host_id", read_only=True)
    isStarted = serializers.BooleanField(source="is_started", read_only=True)
    maxPlayers = serializers.IntegerField(source="max_players", read_only=True)
    members = LobbyMemberSerializer(many=True, read_only=True)

    class Meta:
        model = Lobby
        fields = ["id", "code", "hostId", "maxPlayers", "isStarted", "created_at", "members"]


class CreateLobbySerializer(serializers.Serializer):
    maxPlayers = serializers.IntegerField(min_value=1, max_value=16, required=False, default=4)


class ReadySerializer(serializers.Serializer):
    isReady = serializers.BooleanField(required=False, default=True)
