from __future__ import annotations

import logging
from dataclasses import dataclass

from .audio_io import AudioConfig
from .tts import apply_fade


logger = logging.getLogger(__name__)


@dataclass
class AudioOutputSchedulerMetrics:
    clips_enqueued: int = 0
    frames_emitted: int = 0
    underruns: int = 0
    queued_bytes: int = 0


class AudioOutputScheduler:
    def __init__(self, config: AudioConfig = AudioConfig(), fade_ms: int = 5) -> None:
        self.config = config
        self.fade_ms = fade_ms
        self._queue = bytearray()
        self.metrics = AudioOutputSchedulerMetrics()

    def enqueue_clip(self, pcm: bytes) -> None:
        if not pcm:
            return

        clip = apply_fade(_align_pcm(pcm, self.config), self.config, fade_ms=self.fade_ms)
        self._queue.extend(clip)
        self.metrics.clips_enqueued += 1
        self.metrics.queued_bytes = len(self._queue)

    def enqueue_silence(self, duration_seconds: float) -> None:
        sample_count = max(0, round(duration_seconds * self.config.sample_rate))
        if sample_count <= 0:
            return
        self._queue.extend(b"\x00" * sample_count * self.config.channels * self.config.sample_width_bytes)
        self.metrics.queued_bytes = len(self._queue)

    def emit_frame(self) -> bytes:
        frame_size = self.config.bytes_per_frame
        if len(self._queue) < frame_size:
            frame = bytes(self._queue)
            missing = frame_size - len(frame)
            self._queue.clear()
            if missing > 0:
                self.metrics.underruns += 1
                logger.warning("AudioOutputScheduler underrun: missing_bytes=%s", missing)
                frame += b"\x00" * missing
        else:
            frame = bytes(self._queue[:frame_size])
            del self._queue[:frame_size]

        self.metrics.frames_emitted += 1
        self.metrics.queued_bytes = len(self._queue)
        return frame

    def emit_available(self) -> tuple[bytes, ...]:
        frames = []
        while self._queue:
            frames.append(self.emit_frame())
        return tuple(frames)

    def emit_ready_frames(self) -> tuple[bytes, ...]:
        frames = []
        while len(self._queue) >= self.config.bytes_per_frame:
            frames.append(self.emit_frame())
        return tuple(frames)

    def reset(self) -> None:
        self._queue.clear()
        self.metrics.queued_bytes = 0


def _align_pcm(pcm: bytes, config: AudioConfig) -> bytes:
    alignment = config.channels * config.sample_width_bytes
    usable = len(pcm) - (len(pcm) % alignment)
    return pcm[:usable]
