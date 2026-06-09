from __future__ import annotations

import queue
import time
import wave
from dataclasses import dataclass
from pathlib import Path
from typing import Iterator, Optional, Protocol


@dataclass(frozen=True)
class AudioConfig:
    sample_rate: int = 16_000
    channels: int = 1
    frame_ms: int = 20
    sample_width_bytes: int = 2

    @property
    def samples_per_frame(self) -> int:
        return int(self.sample_rate * self.frame_ms / 1000)

    @property
    def bytes_per_frame(self) -> int:
        return self.samples_per_frame * self.channels * self.sample_width_bytes

    @property
    def frame_seconds(self) -> float:
        return self.frame_ms / 1000.0


@dataclass(frozen=True)
class AudioFrame:
    pcm: bytes
    timestamp: float
    config: AudioConfig

    @property
    def duration_seconds(self) -> float:
        samples = len(self.pcm) / (self.config.sample_width_bytes * self.config.channels)
        return samples / self.config.sample_rate


class AudioFrameSource(Protocol):
    def frames(self) -> Iterator[AudioFrame]:
        """Yield raw 16-bit PCM frames."""


class AudioSink(Protocol):
    def write(self, pcm: bytes) -> None:
        """Write raw 16-bit PCM audio."""

    def close(self) -> None:
        """Release output resources."""


class MicrophoneFrameSource:
    def __init__(
        self,
        config: AudioConfig = AudioConfig(),
        device: Optional[int | str] = None,
        queue_size: int = 100,
    ) -> None:
        self.config = config
        self.device = device
        self._frames: queue.Queue[AudioFrame] = queue.Queue(maxsize=queue_size)

    def frames(self) -> Iterator[AudioFrame]:
        try:
            import sounddevice as sd
        except ImportError as exc:
            raise RuntimeError("Microphone capture requires sounddevice.") from exc

        def callback(indata: bytes, frame_count: int, time_info, status) -> None:
            if status:
                # Keep the callback non-blocking; callers can inspect gaps by timestamp.
                pass
            frame = AudioFrame(bytes(indata), time_info.inputBufferAdcTime, self.config)
            try:
                self._frames.put_nowait(frame)
            except queue.Full:
                _ = self._frames.get_nowait()
                self._frames.put_nowait(frame)

        with sd.RawInputStream(
            samplerate=self.config.sample_rate,
            blocksize=self.config.samples_per_frame,
            channels=self.config.channels,
            dtype="int16",
            device=self.device,
            callback=callback,
        ):
            while True:
                yield self._frames.get()


class WavFrameSource:
    def __init__(
        self,
        path: str | Path,
        config: AudioConfig = AudioConfig(),
        realtime: bool = False,
    ) -> None:
        self.path = Path(path)
        self.config = config
        self.realtime = realtime

    def frames(self) -> Iterator[AudioFrame]:
        with wave.open(str(self.path), "rb") as wav:
            _validate_wav(wav, self.config)
            timestamp = 0.0
            while True:
                pcm = wav.readframes(self.config.samples_per_frame)
                if not pcm:
                    break
                yield AudioFrame(pcm=pcm, timestamp=timestamp, config=self.config)
                timestamp += len(pcm) / (
                    self.config.sample_rate
                    * self.config.channels
                    * self.config.sample_width_bytes
                )
                if self.realtime:
                    time.sleep(self.config.frame_seconds)


class SpeakerSink:
    def __init__(self, config: AudioConfig = AudioConfig(), device: Optional[int | str] = None) -> None:
        self.config = config
        self.device = device
        self._stream = None

    def _ensure_stream(self):
        if self._stream is None:
            try:
                import sounddevice as sd
            except ImportError as exc:
                raise RuntimeError("Speaker output requires sounddevice.") from exc

            self._stream = sd.RawOutputStream(
                samplerate=self.config.sample_rate,
                channels=self.config.channels,
                dtype="int16",
                device=self.device,
            )
            self._stream.start()
        return self._stream

    def write(self, pcm: bytes) -> None:
        self._ensure_stream().write(pcm)

    def close(self) -> None:
        if self._stream is not None:
            self._stream.stop()
            self._stream.close()
            self._stream = None


class WavSink:
    def __init__(self, path: str | Path, config: AudioConfig = AudioConfig()) -> None:
        self.path = Path(path)
        self.config = config
        self._wav = wave.open(str(self.path), "wb")
        self._wav.setnchannels(config.channels)
        self._wav.setsampwidth(config.sample_width_bytes)
        self._wav.setframerate(config.sample_rate)

    def write(self, pcm: bytes) -> None:
        self._wav.writeframes(pcm)

    def close(self) -> None:
        self._wav.close()


def _validate_wav(wav: wave.Wave_read, config: AudioConfig) -> None:
    if wav.getframerate() != config.sample_rate:
        raise ValueError(f"Expected {config.sample_rate} Hz WAV, got {wav.getframerate()} Hz.")
    if wav.getnchannels() != config.channels:
        raise ValueError(f"Expected {config.channels} channel WAV, got {wav.getnchannels()}.")
    if wav.getsampwidth() != config.sample_width_bytes:
        raise ValueError(
            f"Expected {config.sample_width_bytes * 8}-bit WAV, got {wav.getsampwidth() * 8}-bit."
        )

