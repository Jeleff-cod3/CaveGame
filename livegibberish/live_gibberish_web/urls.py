from django.urls import path

from .views import (
    audio_bank_recording_view,
    audio_bank_status_view,
    config_view,
    control_view,
    index_view,
    status_view,
)


urlpatterns = [
    path("", index_view),
    path("api/config/whitelist/", config_view),
    path("api/status/", status_view),
    path("api/control/", control_view),
    path("api/audio-bank/status/", audio_bank_status_view),
    path("api/audio-bank/recording/", audio_bank_recording_view),
]
