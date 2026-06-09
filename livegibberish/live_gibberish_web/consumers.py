from __future__ import annotations

import asyncio
import json
import logging

from channels.generic.websocket import AsyncWebsocketConsumer

from live_gibberish.audio_io import AudioConfig

from .app_state import build_processor, get_config, update_config


logger = logging.getLogger(__name__)


class AudioConsumer(AsyncWebsocketConsumer):
    async def connect(self):
        self.config = AudioConfig()
        self.runtime_config = get_config()
        self.processor = None
        self.next_timestamp = 0.0
        self.not_ready_warned = False
        await self.accept()
        await self.send(
            text_data=json.dumps(
                {
                    "type": "ready",
                    "sample_rate": self.config.sample_rate,
                    "frame_ms": self.config.frame_ms,
                    "enabled": self.runtime_config.enabled,
                    "processor_ready": False,
                    "status": "waiting_for_config",
                    "error": None,
                }
            )
        )

    async def receive(self, text_data=None, bytes_data=None):
        if text_data is not None:
            await self._handle_text(text_data)
            return
        if bytes_data is None:
            return
        if self.processor is None:
            if not self.not_ready_warned:
                self.not_ready_warned = True
                await self.send(
                    text_data=json.dumps(
                        {
                            "type": "error",
                            "message": "Processor is not ready. Save/apply config and wait for processor ok before streaming.",
                        }
                    )
                )
            return
        if not self.runtime_config.enabled:
            return

        for offset in range(0, len(bytes_data), self.config.bytes_per_frame):
            chunk = bytes_data[offset : offset + self.config.bytes_per_frame]
            if len(chunk) < self.config.bytes_per_frame:
                break
            try:
                result = await asyncio.to_thread(self.processor.accept_frame, chunk, self.next_timestamp)
            except Exception as exc:
                logger.exception("Processor failed during audio processing")
                self.processor = None
                await self.send(
                    text_data=json.dumps(
                        {
                            "type": "error",
                            "message": f"Processor failed during audio processing: {type(exc).__name__}: {exc}",
                        }
                    )
                )
                return
            self.next_timestamp += self.config.frame_seconds
            if result is not None:
                await self._send_result(result)

    async def disconnect(self, close_code):
        if self.processor is None:
            return
        result = self.processor.flush()
        if result is not None:
            await self._send_result(result)
        close = getattr(self.processor, "close", None)
        if close:
            close()

    async def _handle_text(self, text_data: str) -> None:
        payload = json.loads(text_data)
        if payload.get("type") == "config":
            self.runtime_config = update_config(payload.get("config", {}))
            await self.send(
                text_data=json.dumps(
                    {
                        "type": "processor",
                        "ok": False,
                        "status": "initializing",
                        "backend": self.runtime_config.asr_backend,
                        "model": self.runtime_config.asr_model,
                    }
                )
            )
            error = await self._rebuild_processor()
            self.next_timestamp = 0.0
            self.not_ready_warned = False
            await self.send(
                text_data=json.dumps(
                    {
                        "type": "config",
                        "ok": error is None,
                        "processor_ready": error is None,
                        "error": error,
                    }
                )
            )

    async def _rebuild_processor(self) -> str | None:
        old_processor = self.processor
        try:
            new_processor = await asyncio.to_thread(build_processor, self.runtime_config)
            self.processor = new_processor
            error = None
        except Exception as exc:
            self.processor = None
            logger.exception("Processor initialization failed")
            error = f"{type(exc).__name__}: {exc}"

        close = getattr(old_processor, "close", None)
        if close:
            await asyncio.to_thread(close)
        return error

    async def _send_result(self, result) -> None:
        await self.send(
            text_data=json.dumps(
                {
                    "type": "segment",
                    "start": result.speech.start_timestamp,
                    "end": result.speech.end_timestamp,
                    "text": result.transcript.text,
                    "words": [
                        {
                            "word": item.decision.normalized_word,
                            "allowed": item.decision.allowed,
                            "reason": item.decision.reason,
                            "replacement": replacement.gibberish.text if replacement.gibberish else None,
                            "error": replacement.error,
                        }
                        for item, replacement in zip(result.filtered_words, result.replacements)
                    ],
                    "bytes": len(result.output_pcm),
                }
            )
        )
        if result.output_pcm:
            await self.send(bytes_data=result.output_pcm)
