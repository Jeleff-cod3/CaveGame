import json
import os
import unittest
from pathlib import Path

os.environ.setdefault("DJANGO_SETTINGS_MODULE", "live_gibberish_web.settings")

import django
from channels.testing import WebsocketCommunicator
from django.test import Client

django.setup()

from live_gibberish_web.asgi import application
from live_gibberish_web.app_state import update_config
import live_gibberish_web.consumers as consumers
from live_gibberish.audio_io import AudioConfig, WavSink


class WebViewTests(unittest.TestCase):
    def setUp(self):
        self.client = Client()

    def test_index_page_renders_frontend(self):
        response = self.client.get("/")

        self.assertEqual(response.status_code, 200)
        content = response.content.decode("utf-8")
        self.assertIn("Live Gibberish Tester", content)
        self.assertIn("Record", content)
        self.assertIn("start_session", content)
        self.assertIn("/ws/audio/", content)

    def test_config_endpoint_updates_runtime_config(self):
        response = self.client.post(
            "/api/config/whitelist/",
            data=json.dumps(
                {
                    "whitelist": ["hello", "cave"],
                    "seed": "test-seed",
                    "asr_backend": "openai-whisper",
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

    def test_openai_whisper_config_sanitizes_invalid_model(self):
        response = self.client.post(
            "/api/config/whitelist/",
            data=json.dumps(
                {
                    "asr_backend": "openai-whisper",
                    "asr_model": "hello danger",
                }
            ),
            content_type="application/json",
        )

        self.assertEqual(response.status_code, 200)
        payload = response.json()
        self.assertEqual(payload["config"]["asr_backend"], "openai-whisper")
        self.assertEqual(payload["config"]["asr_model"], "base.en")

    def test_config_endpoint_splits_whitelist_text_on_spaces_commas_and_newlines(self):
        response = self.client.post(
            "/api/config/whitelist/",
            data=json.dumps({"whitelist": "hello, cave\nlantern torch"}),
            content_type="application/json",
        )

        self.assertEqual(response.status_code, 200)
        payload = response.json()
        self.assertEqual(payload["config"]["whitelist"], ["hello", "cave", "lantern", "torch"])

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
    async def test_audio_websocket_records_session_and_saves_output_wav(self):
        config = AudioConfig()
        original_processor = consumers._process_recorded_session

        def fake_process(_runtime_config, input_path, output_path):
            if not Path(input_path).exists():
                raise AssertionError("session input WAV was not created")
            sink = WavSink(output_path, config=config)
            try:
                sink.write(b"\x00\x00" * config.samples_per_frame)
            finally:
                sink.close()
            return {"segments": 1, "bytes": config.bytes_per_frame}

        consumers._process_recorded_session = fake_process
        communicator = WebsocketCommunicator(application, "/ws/audio/")
        saved = None
        try:
            connected, _subprotocol = await communicator.connect()
            self.assertTrue(connected)
            ready = json.loads(await communicator.receive_from())
            self.assertEqual(ready["type"], "ready")

            await communicator.send_to(
                text_data=json.dumps(
                    {
                        "type": "start_session",
                        "config": {
                            "whitelist": ["hello"],
                            "confidence": 0.7,
                            "buffer_seconds": 5.0,
                            "asr_model": "base.en",
                        },
                    }
                )
            )
            recording = json.loads(await communicator.receive_from())
            self.assertEqual(recording["type"], "session")
            self.assertEqual(recording["status"], "recording")

            await communicator.send_to(bytes_data=b"\x00\x00" * config.sample_rate)
            await communicator.send_to(text_data=json.dumps({"type": "stop_session"}))
            processing = json.loads(await communicator.receive_from())
            saved = json.loads(await communicator.receive_from())

            self.assertEqual(processing["status"], "processing")
            self.assertTrue(saved["ok"])
            self.assertEqual(saved["status"], "saved")
            self.assertTrue(Path(saved["input_wav"]).exists())
            self.assertTrue(Path(saved["output_wav"]).exists())
            self.assertEqual(saved["segments"], 1)
            await communicator.disconnect()
        finally:
            consumers._process_recorded_session = original_processor
            if saved:
                Path(saved["input_wav"]).unlink(missing_ok=True)
                Path(saved["output_wav"]).unlink(missing_ok=True)

    async def test_audio_websocket_accepts_runtime_config(self):
        update_config({"asr_backend": "openai-whisper", "asr_model": "base.en", "tts_backend": "coqui-xtts"})
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
                            "asr_backend": "openai-whisper",
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
