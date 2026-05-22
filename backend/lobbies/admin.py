from django.contrib import admin

from .models import Lobby, LobbyMember


@admin.register(Lobby)
class LobbyAdmin(admin.ModelAdmin):
    list_display = ["id", "code", "host", "max_players", "is_started", "created_at"]
    search_fields = ["code", "host__username"]


@admin.register(LobbyMember)
class LobbyMemberAdmin(admin.ModelAdmin):
    list_display = ["id", "lobby", "user", "player_slot", "is_ready", "joined_at"]
    list_filter = ["is_ready"]
    search_fields = ["lobby__code", "user__username"]
