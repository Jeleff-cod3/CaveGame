from __future__ import annotations

from dataclasses import dataclass
from typing import Iterable, Optional, Protocol


@dataclass(frozen=True)
class WordResult:
    word: str
    start: float
    end: float
    confidence: float

    def shifted(self, offset_seconds: float) -> "WordResult":
        return WordResult(
            word=self.word,
            start=self.start + offset_seconds,
            end=self.end + offset_seconds,
            confidence=self.confidence,
        )


@dataclass(frozen=True)
class Transcript:
    text: str
    words: tuple[WordResult, ...]

    def with_offset(self, offset_seconds: float) -> "Transcript":
        return Transcript(
            text=self.text,
            words=tuple(word.shifted(offset_seconds) for word in self.words),
        )


class AsrEngine(Protocol):
    def transcribe(self, pcm16: bytes, sample_rate: int) -> Transcript:
        """Transcribe one buffered speech segment."""


class FasterWhisperAsr:
    def __init__(
        self,
        model_name: str = "base.en",
        device: str = "cuda",
        compute_type: str = "float16",
        language: str = "en",
    ) -> None:
        try:
            from faster_whisper import WhisperModel
        except ImportError as exc:
            raise RuntimeError("FasterWhisperAsr requires faster-whisper.") from exc

        self._model_type = WhisperModel
        self.model_name = model_name
        self.language = language
        if device != "cuda":
            raise ValueError("FasterWhisperAsr is configured for GPU-only execution; device must be cuda.")
        self._model = self._model_type(model_name, device="cuda", compute_type=compute_type)
        self.device = "cuda"
        self.compute_type = compute_type

    def transcribe(self, pcm16: bytes, sample_rate: int) -> Transcript:
        try:
            import numpy as np
        except ImportError as exc:
            raise RuntimeError("FasterWhisperAsr requires numpy.") from exc

        audio = np.frombuffer(pcm16, dtype=np.int16).astype(np.float32) / 32768.0
        segments, _info = self._transcribe_audio(audio)
        return self._segments_to_transcript(segments)

    def _transcribe_audio(self, audio):
        return self._model.transcribe(
            audio,
            language=self.language,
            word_timestamps=True,
            vad_filter=False,
        )

    def _segments_to_transcript(self, segments) -> Transcript:
        words: list[WordResult] = []
        text_parts: list[str] = []
        for segment in segments:
            text_parts.append(segment.text.strip())
            for word in segment.words or []:
                words.append(
                    WordResult(
                        word=word.word.strip(),
                        start=float(word.start),
                        end=float(word.end),
                        confidence=float(getattr(word, "probability", 0.0) or 0.0),
                    )
                )

        return Transcript(text=" ".join(part for part in text_parts if part), words=tuple(words))


def create_asr(
    backend: str,
    model: Optional[str] = None,
    whitelist: Optional[Iterable[str]] = None,
) -> AsrEngine:
    normalized = backend.lower().strip()
    if normalized in {"faster-whisper", "faster_whisper", "whisper"}:
        return FasterWhisperAsr(model_name=model or "base.en")
    raise ValueError(f"Unsupported ASR backend: {backend}. This app is GPU-only; use faster-whisper.")


def _pcm_duration_seconds(pcm16: bytes, sample_rate: int) -> float:
    if sample_rate <= 0:
        return 0.0
    return (len(pcm16) / 2) / sample_rate
