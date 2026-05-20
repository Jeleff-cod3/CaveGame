from django.contrib.auth import get_user_model
from django.test import TestCase, override_settings
from rest_framework.authtoken.models import Token
from rest_framework.test import APIClient


@override_settings(CHANNEL_LAYERS={"default": {"BACKEND": "channels.layers.InMemoryChannelLayer"}})
class LobbyApiTests(TestCase):
    def setUp(self):
        self.user = get_user_model().objects.create_user(username="host")
        self.token = Token.objects.create(user=self.user)
        self.client = APIClient()
        self.client.credentials(HTTP_AUTHORIZATION=f"Token {self.token.key}")

    def test_create_lobby_adds_host_as_slot_zero(self):
        response = self.client.post("/api/lobbies/create/", {"maxPlayers": 4}, format="json")

        self.assertEqual(response.status_code, 201)
        self.assertEqual(response.data["maxPlayers"], 4)
        self.assertEqual(response.data["members"][0]["slot"], 0)
        self.assertEqual(response.data["members"][0]["playerId"], f"player_{self.user.id}")

    def test_join_lobby_assigns_next_slot(self):
        lobby_response = self.client.post("/api/lobbies/create/", {"maxPlayers": 4}, format="json")
        code = lobby_response.data["code"]

        guest = get_user_model().objects.create_user(username="guest")
        guest_token = Token.objects.create(user=guest)
        guest_client = APIClient()
        guest_client.credentials(HTTP_AUTHORIZATION=f"Token {guest_token.key}")

        response = guest_client.post(f"/api/lobbies/{code}/join/", {}, format="json")

        self.assertEqual(response.status_code, 201)
        self.assertEqual(response.data["member"]["slot"], 1)
