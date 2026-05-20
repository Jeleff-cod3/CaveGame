from django.conf import settings
from django.db import models


class Lobby(models.Model):
    code = models.CharField(max_length=8, unique=True)
    host = models.ForeignKey(
        settings.AUTH_USER_MODEL,
        on_delete=models.CASCADE,
        related_name="hosted_lobbies",
    )
    max_players = models.PositiveSmallIntegerField(default=4)
    is_started = models.BooleanField(default=False)
    created_at = models.DateTimeField(auto_now_add=True)

    def __str__(self) -> str:
        return f"Lobby {self.code}"


class LobbyMember(models.Model):
    lobby = models.ForeignKey(
        Lobby,
        on_delete=models.CASCADE,
        related_name="members",
    )
    user = models.ForeignKey(
        settings.AUTH_USER_MODEL,
        on_delete=models.CASCADE,
    )
    player_slot = models.PositiveSmallIntegerField()
    is_ready = models.BooleanField(default=False)
    joined_at = models.DateTimeField(auto_now_add=True)

    class Meta:
        unique_together = [
            ("lobby", "user"),
            ("lobby", "player_slot"),
        ]
        ordering = ["player_slot"]

    @property
    def player_id(self) -> str:
        return f"player_{self.user_id}"

    def __str__(self) -> str:
        return f"{self.user} in {self.lobby}"
