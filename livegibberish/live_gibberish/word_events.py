from __future__ import annotations

from dataclasses import dataclass

from .asr import Transcript
from .text_normalization import normalize_word
from .vad import SpeechSegment


@dataclass(frozen=True)
class WordEvent:
    raw_text: str
    normalized_text: str
    start_ms: int
    end_ms: int
    confidence: float
    is_final: bool

    @property
    def duration_seconds(self) -> float:
        return max(0.0, (self.end_ms - self.start_ms) / 1000.0)

    @property
    def start_seconds(self) -> float:
        return self.start_ms / 1000.0

    @property
    def end_seconds(self) -> float:
        return self.end_ms / 1000.0


def transcript_to_final_word_events(segment: SpeechSegment, transcript: Transcript) -> tuple[WordEvent, ...]:
    events = []
    for word in transcript.words:
        raw_text = word.word.strip()
        normalized = normalize_word(raw_text)
        if not normalized:
            continue
        start_seconds = segment.start_timestamp + word.start
        end_seconds = segment.start_timestamp + word.end
        if end_seconds <= start_seconds:
            end_seconds = start_seconds + 0.01
        events.append(
            WordEvent(
                raw_text=raw_text,
                normalized_text=normalized,
                start_ms=round(start_seconds * 1000),
                end_ms=round(end_seconds * 1000),
                confidence=word.confidence,
                is_final=True,
            )
        )
    return tuple(events)
