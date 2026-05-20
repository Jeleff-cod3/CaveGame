from django.urls import path

from .consumers import GameConsumer, LobbyConsumer


websocket_urlpatterns = [
    path("ws/lobby/<int:lobby_id>/", LobbyConsumer.as_asgi()),
    path("ws/game/<int:lobby_id>/", GameConsumer.as_asgi()),
]
