from django.urls import path

from .views import CreateLobbyView, JoinLobbyView, LobbyDetailView, ReadyLobbyView, StartLobbyView


urlpatterns = [
    path("create/", CreateLobbyView.as_view(), name="lobby-create"),
    path("<str:code>/join/", JoinLobbyView.as_view(), name="lobby-join"),
    path("<int:lobby_id>/ready/", ReadyLobbyView.as_view(), name="lobby-ready"),
    path("<int:lobby_id>/start/", StartLobbyView.as_view(), name="lobby-start"),
    path("<int:lobby_id>/", LobbyDetailView.as_view(), name="lobby-detail"),
]
