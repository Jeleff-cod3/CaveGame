from __future__ import annotations

from dataclasses import dataclass

from .asr import Transcript, WordResult
from .buffer import AudioSlice, TimedAudioBuffer
from .filtering import WhitelistChecker, WordDecision
from .vad import SpeechSegment


@dataclass(frozen=True)
class FilteredWordSegment:
    decision: WordDecision
    audio: AudioSlice

    @property
    def needs_replacement(self) -> bool:
        return not self.decision.allowed


class WordFilterPipeline:
    def __init__(self, checker: WhitelistChecker, audio_buffer: TimedAudioBuffer) -> None:
        self.checker = checker
        self.audio_buffer = audio_buffer

    def process(self, segment: SpeechSegment, transcript: Transcript) -> tuple[FilteredWordSegment, ...]:
        absolute_words = _absolute_words(segment, transcript)
        decisions = self.checker.check_all(absolute_words)
        filtered = tuple(
            FilteredWordSegment(
                decision=decision,
                audio=self.audio_buffer.extract(decision.original.start, decision.original.end),
            )
            for decision in decisions
        )
        if filtered:
            self.audio_buffer.drop_before(max(item.audio.end for item in filtered))
        return filtered


def _absolute_words(segment: SpeechSegment, transcript: Transcript) -> tuple[WordResult, ...]:
    if transcript.words:
        return tuple(word.shifted(segment.start_timestamp) for word in transcript.words)
    if not transcript.text.strip():
        return ()
    return (
        WordResult(
            word=transcript.text.strip(),
            start=segment.start_timestamp,
            end=segment.end_timestamp,
            confidence=0.0,
        ),
    )

