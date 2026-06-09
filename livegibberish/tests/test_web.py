import json
import os
import unittest

os.environ.setdefault("DJANGO_SETTINGS_MODULE", "live_gibberish_web.settings")

import django
from channels.testing import WebsocketCommunicator
from django.test import Client

django.setup()

from live_gibberish_web.asgi import application
from live_gibberish_web.app_state import update_config
import live_gibberish_web.consumers as consumers


class WebViewTests(unittest.TestCase):
    def setUp(self):
        self.client = Client()

    def test_index_page_renders_frontend(self):
        response = self.client.get("/")

        self.assertEqual(response.status_code, 200)
        content = response.content.decode("utf-8")
        self.assertIn("Live Gibberish Tester", content)
        self.assertIn("connectSocket", content)
        self.assertIn("/ws/audio/", content)

    def test_config_endpoint_updates_runtime_config(self):
        response = self.client.post(
            "/api/config/whitelist/",
            data=json.dumps(
                {
                    "whitelist": ["hello", "cave"],
                    "seed": "test-seed",
                    "asr_backend": "faster-whisper",
                    "asr_model": "base.en",
                }
            ),
            content_type="application/json",
        )

        self.assertEqual(response.status_code, 200)
        payload = response.json()
        self.assertTrue(payload["ok"])
        self.assertEqual(payload["config"]["whitelist"], ["hello", "cave"])
        self.assertEqual(payload["config"]["seed"], "test-seed")

    def test_faster_whisper_config_sanitizes_invalid_model(self):
        response = self.client.post(
            "/api/config/whitelist/",
            data=json.dumps(
                {
                    "asr_backend": "faster-whisper",
                    "asr_model": "hello danger",
                }
            ),
            content_type="application/json",
        )

        self.assertEqual(response.status_code, 200)
        payload = response.json()
        self.assertEqual(payload["config"]["asr_backend"], "faster-whisper")
        self.assertEqual(payload["config"]["asr_model"], "base.en")

    def test_status_and_control_endpoints(self):
        stop = self.client.post("/api/control/", data=json.dumps({"action": "stop"}), content_type="application/json")
        status = self.client.get("/api/status/")
        start = self.client.post("/api/control/", data=json.dumps({"action": "start"}), content_type="application/json")

        self.assertEqual(stop.status_code, 200)
        self.assertEqual(status.status_code, 200)
        self.assertFalse(status.json()["status"]["enabled"])
        self.assertEqual(start.status_code, 200)
        self.assertTrue(start.json()["status"]["enabled"])


class WebSocketTests(unittest.IsolatedAsyncioTestCase):
    async def test_audio_websocket_accepts_runtime_config(self):
        update_config({"asr_backend": "faster-whisper", "asr_model": "base.en", "tts_backend": "coqui-xtts"})
        original_builder = consumers.build_processor
        consumers.build_processor = lambda config: (_ for _ in ()).throw(RuntimeError("gpu model unavailable"))
        communicator = WebsocketCommunicator(application, "/ws/audio/")
        try:
            connected, _subprotocol = await communicator.connect()
            self.assertTrue(connected)

            ready = json.loads(await communicator.receive_from())
            self.assertEqual(ready["type"], "ready")
            self.assertFalse(ready["processor_ready"])

            await communicator.send_to(
                text_data=json.dumps(
                    {
                        "type": "config",
                        "config": {
                            "whitelist": ["hello"],
                            "seed": "socket-seed",
                            "asr_backend": "faster-whisper",
                            "asr_model": "base.en",
                        },
                    }
                )
            )
            initializing = json.loads(await communicator.receive_from())
            self.assertEqual(initializing["type"], "processor")
            self.assertFalse(initializing["ok"])
            self.assertEqual(initializing["status"], "initializing")

            response = json.loads(await communicator.receive_from())

            self.assertFalse(response["ok"])
            self.assertFalse(response["processor_ready"])
            self.assertIn("gpu model unavailable", response["error"])
            await communicator.disconnect()
        finally:
            consumers.build_processor = original_builder


if __name__ == "__main__":
    unittest.main()
