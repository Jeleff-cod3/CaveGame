from __future__ import annotations

from collections import deque
from dataclasses import dataclass
from typing import Deque

from .audio_io import AudioConfig, AudioFrame


@dataclass(frozen=True)
class AudioSlice:
    pcm: bytes
    start: float
    end: float


class TimedAudioBuffer:
    def __init__(self, config: AudioConfig = AudioConfig(), max_duration_seconds: float = 5.0) -> None:
        self.config = config
        self.max_duration_seconds = max_duration_seconds
        self._frames: Deque[AudioFrame] = deque()

    def append(self, frame: AudioFrame) -> None:
        self._frames.append(frame)
        self.drop_before(frame.timestamp + frame.duration_seconds - self.max_duration_seconds)

    def extend(self, frames: list[AudioFrame] | tuple[AudioFrame, ...]) -> None:
        for frame in frames:
            self.append(frame)

    def extract(self, start: float, end: float) -> AudioSlice:
        if end <= start:
            return AudioSlice(pcm=b"", start=start, end=end)

        pieces: list[bytes] = []
        for frame in self._frames:
            frame_start = frame.timestamp
            frame_end = frame.timestamp + frame.duration_seconds
            overlap_start = max(start, frame_start)
            overlap_end = min(end, frame_end)
            if overlap_end <= overlap_start:
                continue

            byte_start = self._seconds_to_byte_offset(overlap_start - frame_start)
            byte_end = self._seconds_to_byte_offset(overlap_end - frame_start)
            pieces.append(frame.pcm[byte_start:byte_end])

        return AudioSlice(pcm=b"".join(pieces), start=start, end=end)

    def drop_before(self, timestamp: float) -> None:
        while self._frames:
            frame = self._frames[0]
            frame_end = frame.timestamp + frame.duration_seconds
            if frame_end - timestamp > 1e-9:
                break
            self._frames.popleft()

    def _seconds_to_byte_offset(self, seconds: float) -> int:
        samples = round(seconds * self.config.sample_rate)
        raw_offset = samples * self.config.channels * self.config.sample_width_bytes
        alignment = self.config.channels * self.config.sample_width_bytes
        return max(0, raw_offset - (raw_offset % alignment))
