from __future__ import annotations

import asyncio
import json
import logging
from dataclasses import replace
from datetime import datetime
from pathlib import Path

from channels.generic.websocket import AsyncWebsocketConsumer

from live_gibberish.audio_io import AudioConfig, WavFrameSource, WavSink
from live_gibberish.processor import ProcessedSegment
from live_gibberish.speaker import SpeakerEnrollment

from .app_state import RuntimeConfig, build_processor, get_config, update_config


logger = logging.getLogger(__name__)
LIVE_GIBBERISH_ROOT = Path(__file__).resolve().parent.parent


class AudioConsumer(AsyncWebsocketConsumer):
    async def connect(self):
        self.config = AudioConfig()
        self.runtime_config = get_config()
        self.processor = None
        self.next_timestamp = 0.0
        self.not_ready_warned = False
        self.session_sink = None
        self.session_input_path = None
        self.session_output_path = None
        self.session_bytes = 0
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
        should_process = self.processor is not None and self.runtime_config.enabled
        if self.processor is None and self.session_sink is None:
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
        if not should_process and self.session_sink is None:
            return

        for offset in range(0, len(bytes_data), self.config.bytes_per_frame):
            chunk = bytes_data[offset : offset + self.config.bytes_per_frame]
            if len(chunk) < self.config.bytes_per_frame:
                break
            if self.session_sink is not None:
                await asyncio.to_thread(self.session_sink.write, chunk)
                self.session_bytes += len(chunk)
            if not should_process:
                continue
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
        await self._close_session_sink()
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
        elif payload.get("type") == "start_session":
            self.runtime_config = update_config(payload.get("config", {}))
            await self._start_session()
        elif payload.get("type") == "stop_session":
            await self._stop_session()

    async def _start_session(self) -> None:
        await self._close_session_sink()
        directory = LIVE_GIBBERISH_ROOT / "runtime" / "sessions"
        directory.mkdir(parents=True, exist_ok=True)
        timestamp = datetime.now().strftime("%Y%m%d-%H%M%S-%f")
        self.session_input_path = directory / f"session-{timestamp}-input.wav"
        self.session_output_path = directory / f"session-{timestamp}-output.wav"
        self.session_sink = WavSink(self.session_input_path, config=self.config)
        self.session_bytes = 0
        _console_log(
            "SESSION RECORD START",
            [
                f"input_wav={str(self.session_input_path.resolve())!r}",
                f"output_wav={str(self.session_output_path.resolve())!r}",
                f"whitelist={list(self.runtime_config.whitelist)!r}",
                f"confidence={self.runtime_config.confidence}",
                f"buffer_seconds={self.runtime_config.buffer_seconds}",
                f"asr_backend={self.runtime_config.asr_backend!r}",
                f"asr_model={self.runtime_config.asr_model!r}",
                f"tts_backend={self.runtime_config.tts_backend!r}",
                f"seed={self.runtime_config.seed!r}",
            ],
        )
        await self.send(
            text_data=json.dumps(
                {
                    "type": "session",
                    "status": "recording",
                    "input_wav": str(self.session_input_path.resolve()),
                    "output_wav": str(self.session_output_path.resolve()),
                }
            )
        )

    async def _stop_session(self) -> None:
        input_path = self.session_input_path
        output_path = self.session_output_path
        await self._close_session_sink()
        if input_path is None or output_path is None:
            await self.send(text_data=json.dumps({"type": "session", "ok": False, "error": "Session is not recording."}))
            return

        _console_log(
            "SESSION RECORD STOP",
            [
                f"input_wav={str(Path(input_path).resolve())!r}",
                f"output_wav={str(Path(output_path).resolve())!r}",
                f"recorded_pcm_bytes={self.session_bytes}",
            ],
        )
        await self.send(text_data=json.dumps({"type": "session", "status": "processing"}))
        try:
            summary = await asyncio.to_thread(_process_recorded_session, self.runtime_config, input_path, output_path)
        except Exception as exc:
            logger.exception("Recorded session processing failed")
            await self.send(
                text_data=json.dumps(
                    {
                        "type": "session",
                        "ok": False,
                        "status": "error",
                        "error": f"{type(exc).__name__}: {exc}",
                    }
                )
            )
            return

        update_config({"enrollment_wav": str(Path(input_path).resolve())})
        _console_log(
            "SESSION SAVED",
            [
                f"input_wav={str(Path(input_path).resolve())!r}",
                f"output_wav={str(Path(output_path).resolve())!r}",
                f"segments={summary['segments']}",
                f"output_pcm_bytes={summary['bytes']}",
            ],
        )
        await self.send(
            text_data=json.dumps(
                {
                    "type": "session",
                    "ok": True,
                    "status": "saved",
                    "input_wav": str(Path(input_path).resolve()),
                    "output_wav": str(Path(output_path).resolve()),
                    "segments": summary["segments"],
                    "bytes": summary["bytes"],
                }
            )
        )

    async def _close_session_sink(self) -> None:
        sink = self.session_sink
        self.session_sink = None
        if sink is not None:
            await asyncio.to_thread(sink.close)

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
                            "original": item.decision.original.word,
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


def _process_recorded_session(config: RuntimeConfig, input_path: Path, output_path: Path) -> dict[str, int]:
    audio_config = AudioConfig()
    input_path = Path(input_path).resolve()
    output_path = Path(output_path).resolve()
    if not input_path.exists():
        raise FileNotFoundError(f"Recorded input WAV does not exist: {input_path}")
    SpeakerEnrollment(config=audio_config).from_wav(input_path)
    processor_config = replace(config, enrollment_wav=str(input_path.resolve()))
    processor = build_processor(processor_config)
    source = WavFrameSource(input_path, config=audio_config)
    sink = WavSink(output_path, config=audio_config)
    segments = 0
    byte_count = 0
    try:
        for frame in source.frames():
            result = processor.accept_frame(frame.pcm, frame.timestamp)
            if result is not None:
                segments += 1
                byte_count += _write_processed_segment(sink, result)
        result = processor.flush()
        if result is not None:
            segments += 1
            byte_count += _write_processed_segment(sink, result)
    finally:
        sink.close()
        close = getattr(processor, "close", None)
        if close:
            close()
    return {"segments": segments, "bytes": byte_count}


def _write_processed_segment(sink: WavSink, result: ProcessedSegment) -> int:
    sink.write(result.output_pcm)
    return len(result.output_pcm)


def _console_log(title: str, lines: list[str]) -> None:
    message = "\n".join(["", f"========== LIVE GIBBERISH: {title} ==========", *lines, "=" * 58])
    logger.info(message)
    print(message, flush=True)
