from __future__ import annotations

from collections import deque
from dataclasses import dataclass
from math import sqrt
from typing import Deque, Iterable, Optional, Protocol

from .audio_io import AudioConfig, AudioFrame


@dataclass(frozen=True)
class VadDecision:
    is_speech: bool
    score: float


@dataclass(frozen=True)
class SpeechSegment:
    pcm: bytes
    start_timestamp: float
    end_timestamp: float
    frame_count: int


class VoiceActivityDetector(Protocol):
    def is_speech(self, frame: AudioFrame) -> VadDecision:
        """Return whether a frame contains speech."""


class EnergyVad:
    def __init__(self, threshold: float = 0.02) -> None:
        self.threshold = threshold

    def is_speech(self, frame: AudioFrame) -> VadDecision:
        if not frame.pcm:
            return VadDecision(False, 0.0)

        rms = _pcm16_rms(frame.pcm)
        max_amplitude = float(2 ** (8 * frame.config.sample_width_bytes - 1))
        score = min(1.0, rms / max_amplitude)
        return VadDecision(score >= self.threshold, score)


class WebRtcVad:
    def __init__(self, aggressiveness: int = 2) -> None:
        try:
            import webrtcvad
        except ImportError as exc:
            raise RuntimeError("WebRtcVad requires the webrtcvad package.") from exc

        self._vad = webrtcvad.Vad(aggressiveness)

    def is_speech(self, frame: AudioFrame) -> VadDecision:
        result = self._vad.is_speech(frame.pcm, frame.config.sample_rate)
        return VadDecision(is_speech=result, score=1.0 if result else 0.0)


class SpeechSegmenter:
    def __init__(
        self,
        vad: VoiceActivityDetector,
        config: AudioConfig = AudioConfig(),
        min_silence_ms: int = 300,
        speech_pad_ms: int = 50,
    ) -> None:
        self.vad = vad
        self.config = config
        self.min_silence_frames = max(1, min_silence_ms // config.frame_ms)
        self.pad_frames = max(0, speech_pad_ms // config.frame_ms)
        self._pre_roll: Deque[AudioFrame] = deque(maxlen=self.pad_frames)
        self._active_frames: list[AudioFrame] = []
        self._silence_frames = 0

    def process(self, frame: AudioFrame) -> Optional[SpeechSegment]:
        decision = self.vad.is_speech(frame)
        if decision.is_speech:
            if not self._active_frames:
                self._active_frames.extend(self._pre_roll)
            self._active_frames.append(frame)
            self._silence_frames = 0
            return None

        if not self._active_frames:
            self._pre_roll.append(frame)
            return None

        self._active_frames.append(frame)
        self._silence_frames += 1
        if self._silence_frames < self.min_silence_frames:
            return None

        return self._flush()

    def flush(self) -> Optional[SpeechSegment]:
        if not self._active_frames:
            return None
        return self._flush()

    def _flush(self) -> SpeechSegment:
        frames = self._active_frames
        self._active_frames = []
        self._silence_frames = 0
        self._pre_roll.clear()

        start = frames[0].timestamp
        end = frames[-1].timestamp + frames[-1].duration_seconds
        pcm = b"".join(frame.pcm for frame in frames)
        return SpeechSegment(pcm=pcm, start_timestamp=start, end_timestamp=end, frame_count=len(frames))


def create_vad(prefer_webrtc: bool = True) -> VoiceActivityDetector:
    if prefer_webrtc:
        try:
            return WebRtcVad()
        except RuntimeError:
            pass
    return EnergyVad()


def segment_frames(
    frames: Iterable[AudioFrame],
    vad: VoiceActivityDetector,
    config: AudioConfig = AudioConfig(),
) -> Iterable[SpeechSegment]:
    segmenter = SpeechSegmenter(vad=vad, config=config)
    for frame in frames:
        segment = segmenter.process(frame)
        if segment is not None:
            yield segment
    final_segment = segmenter.flush()
    if final_segment is not None:
        yield final_segment


def _pcm16_rms(pcm: bytes) -> float:
    if len(pcm) < 2:
        return 0.0

    sample_count = len(pcm) // 2
    total = 0
    for index in range(0, sample_count * 2, 2):
        sample = int.from_bytes(pcm[index : index + 2], byteorder="little", signed=True)
        total += sample * sample
    return sqrt(total / sample_count)
