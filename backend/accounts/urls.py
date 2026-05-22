from django.urls import path

from .views import GuestTokenView, MeView


urlpatterns = [
    path("guest/", GuestTokenView.as_view(), name="guest-token"),
    path("me/", MeView.as_view(), name="me"),
]
