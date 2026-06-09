from django.urls import path

from .views import config_view, control_view, index_view, status_view


urlpatterns = [
    path("", index_view),
    path("api/config/whitelist/", config_view),
    path("api/status/", status_view),
    path("api/control/", control_view),
]
