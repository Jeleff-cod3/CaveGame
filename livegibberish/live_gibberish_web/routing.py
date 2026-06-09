from django.urls import path

from .consumers import AudioConsumer


websocket_urlpatterns = [
    path("ws/audio/", AudioConsumer.as_asgi()),
]

