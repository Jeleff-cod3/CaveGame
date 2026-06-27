from __future__ import annotations

import json
import logging
import re
from dataclasses import dataclass
from typing import Iterable, Optional, Protocol


logger = logging.getLogger(__name__)


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


class OpenAiWhisperAsr:
    def __init__(
        self,
        model_name: str = "base.en",
        device: str = "cuda",
        language: str = "en",
        whitelist: Optional[Iterable[str]] = None,
    ) -> None:
        if device != "cuda":
            raise ValueError("OpenAiWhisperAsr is configured for GPU-only execution; device must be cuda.")
        try:
            import torch
            import whisper
        except ImportError as exc:
            raise RuntimeError("OpenAiWhisperAsr requires openai-whisper and torch.") from exc

        if not torch.cuda.is_available():
            raise RuntimeError("OpenAiWhisperAsr requires CUDA torch; torch.cuda.is_available() is false.")

        self.model_name = model_name
        self.language = language
        self.device = "cuda"
        self.initial_prompt = _whitelist_prompt(whitelist or ())
        self._model = whisper.load_model(model_name, device="cuda")

    def transcribe(self, pcm16: bytes, sample_rate: int) -> Transcript:
        try:
            import numpy as np
        except ImportError as exc:
            raise RuntimeError("OpenAiWhisperAsr requires numpy.") from exc

        if sample_rate != 16000:
            raise ValueError("OpenAiWhisperAsr expects 16 kHz PCM input.")
        audio = np.frombuffer(pcm16, dtype=np.int16).astype(np.float32) / 32768.0
        kwargs = {
            "language": self.language,
            "fp16": True,
            "verbose": False,
            "word_timestamps": True,
        }
        if self.initial_prompt:
            kwargs["initial_prompt"] = self.initial_prompt
        _console_log(
            "OPENAI WHISPER REQUEST",
            [
                f"model={self.model_name!r}",
                f"device={self.device!r}",
                f"language={self.language!r}",
                f"audio_samples={len(audio)}",
                f"audio_seconds={len(audio) / sample_rate if sample_rate else 0:.3f}",
                f"initial_prompt={self.initial_prompt!r}",
            ],
        )
        result = self._model.transcribe(audio, **kwargs)
        _console_log("OPENAI WHISPER RAW RESULT", _format_whisper_result(result))
        return self._result_to_transcript(result)

    def _result_to_transcript(self, result) -> Transcript:
        words: list[WordResult] = []
        text_parts: list[str] = []
        for segment in result.get("segments", []):
            text = str(segment.get("text", "")).strip()
            if not text:
                continue
            text_parts.append(text)
            segment_words = _words_from_segment(segment)
            if segment_words:
                words.extend(segment_words)
            else:
                words.extend(_estimate_words(text, float(segment.get("start", 0.0)), float(segment.get("end", 0.0))))
        return Transcript(text=" ".join(text_parts), words=tuple(words))


def create_asr(
    backend: str,
    model: Optional[str] = None,
    whitelist: Optional[Iterable[str]] = None,
) -> AsrEngine:
    normalized = backend.lower().strip()
    if normalized in {"openai-whisper", "openai_whisper", "whisper"}:
        return OpenAiWhisperAsr(model_name=model or "base.en", whitelist=whitelist)
    if normalized in {"faster-whisper", "faster_whisper"}:
        return FasterWhisperAsr(model_name=model or "base.en")
    raise ValueError(f"Unsupported ASR backend: {backend}. This app is GPU-only; use openai-whisper.")


def _estimate_words(text: str, start: float, end: float) -> tuple[WordResult, ...]:
    raw_words = [match.group(0) for match in re.finditer(r"\b[\w']+\b", text)]
    if not raw_words:
        return ()
    duration = max(0.01, end - start)
    step = duration / len(raw_words)
    return tuple(
        WordResult(
            word=word,
            start=start + index * step,
            end=start + (index + 1) * step,
            confidence=1.0,
        )
        for index, word in enumerate(raw_words)
    )


def _words_from_segment(segment) -> tuple[WordResult, ...]:
    words = []
    for item in segment.get("words", []) or []:
        word = str(_read_whisper_field(item, "word", "")).strip()
        if not word:
            continue
        start = _read_whisper_float(item, "start", float(segment.get("start", 0.0)))
        end = _read_whisper_float(item, "end", start)
        confidence = _read_whisper_float(item, "probability", 1.0)
        if end <= start:
            end = start + 0.01
        words.append(WordResult(word=word, start=start, end=end, confidence=confidence))
    return tuple(words)


def _read_whisper_field(item, name: str, default):
    if isinstance(item, dict):
        return item.get(name, default)
    return getattr(item, name, default)


def _read_whisper_float(item, name: str, default: float) -> float:
    try:
        return float(_read_whisper_field(item, name, default))
    except (TypeError, ValueError):
        return default


def _whitelist_prompt(whitelist: Iterable[str]) -> str:
    words = []
    seen = set()
    for item in whitelist:
        word = str(item).strip()
        normalized = word.casefold()
        if not word or normalized in seen:
            continue
        seen.add(normalized)
        words.append(word)
    if not words:
        return ""
    return "The following words may be spoken and should be transcribed exactly: " + ", ".join(words) + "."


def _format_whisper_result(result) -> list[str]:
    lines = [f"text={str(result.get('text', '')).strip()!r}"]
    segments = result.get("segments", [])
    if not segments:
        lines.append("segments=[]")
        return lines
    lines.append("segments:")
    for index, segment in enumerate(segments):
        data = {
            "index": index,
            "start": segment.get("start"),
            "end": segment.get("end"),
            "text": str(segment.get("text", "")).strip(),
            "words": [
                {
                    "word": str(_read_whisper_field(word, "word", "")).strip(),
                    "start": _read_whisper_field(word, "start", None),
                    "end": _read_whisper_field(word, "end", None),
                    "probability": _read_whisper_field(word, "probability", None),
                }
                for word in segment.get("words", []) or []
            ],
            "avg_logprob": segment.get("avg_logprob"),
            "no_speech_prob": segment.get("no_speech_prob"),
            "compression_ratio": segment.get("compression_ratio"),
        }
        lines.append("  " + json.dumps(data, ensure_ascii=False))
    return lines


def _console_log(title: str, lines: list[str]) -> None:
    message = "\n".join(["", f"========== LIVE GIBBERISH: {title} ==========", *lines, "=" * 58])
    logger.info(message)
    print(message, flush=True)


def _pcm_duration_seconds(pcm16: bytes, sample_rate: int) -> float:
    if sample_rate <= 0:
        return 0.0
    return (len(pcm16) / 2) / sample_rate
