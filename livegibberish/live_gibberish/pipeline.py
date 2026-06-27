from __future__ import annotations

from dataclasses import dataclass

from .asr import Transcript, WordResult
from .buffer import AudioSlice, TimedAudioBuffer
from .filtering import WhitelistChecker, WordDecision
from .vad import SpeechSegment


ALLOWED_WORD_LEAD_IN_SECONDS = 0.15
ALLOWED_WORD_TAIL_SECONDS = 0.06


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
                audio=_extract_from_segment(segment, start, end, self.audio_buffer.config),
            )
            for decision, start, end in _word_audio_bounds(segment, decisions)
        )
        if filtered:
            self.audio_buffer.drop_before(max(item.audio.end for item in filtered))
        return filtered


def _word_audio_bounds(
    segment: SpeechSegment,
    decisions: tuple[WordDecision, ...],
) -> tuple[tuple[WordDecision, float, float], ...]:
    bounds: list[tuple[WordDecision, float, float]] = []
    for decision in decisions:
        start = decision.original.start
        end = decision.original.end
        if decision.allowed:
            start -= ALLOWED_WORD_LEAD_IN_SECONDS
            end += ALLOWED_WORD_TAIL_SECONDS
        bounds.append(
            (
                decision,
                max(segment.start_timestamp, start),
                min(segment.end_timestamp, end),
            )
        )
    return tuple(bounds)


def _extract_from_segment(segment: SpeechSegment, start: float, end: float, config) -> AudioSlice:
    if end <= start:
        return AudioSlice(pcm=b"", start=start, end=end)

    overlap_start = max(start, segment.start_timestamp)
    overlap_end = min(end, segment.end_timestamp)
    if overlap_end <= overlap_start:
        return AudioSlice(pcm=b"", start=start, end=end)

    byte_start = _seconds_to_byte_offset(overlap_start - segment.start_timestamp, config)
    byte_end = _seconds_to_byte_offset(overlap_end - segment.start_timestamp, config)
    return AudioSlice(pcm=segment.pcm[byte_start:byte_end], start=start, end=end)


def _seconds_to_byte_offset(seconds: float, config) -> int:
    samples = round(seconds * config.sample_rate)
    raw_offset = samples * config.channels * config.sample_width_bytes
    alignment = config.channels * config.sample_width_bytes
    return max(0, raw_offset - (raw_offset % alignment))


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

