from __future__ import annotations

import logging
from time import perf_counter
from dataclasses import dataclass
from typing import Iterable

from .asr import WordResult
from .audio_bank import SampleBank, duration_bucket
from .audio_io import AudioConfig
from .buffer import AudioSlice
from .filtering import WordDecision
from .gibberish import GibberishToken
from .pipeline import FilteredWordSegment
from .replacement import ReplacementSegment
from .text_normalization import normalize_word
from .word_events import WordEvent


logger = logging.getLogger(__name__)
FALLBACK_POLICIES = ("strict", "safe", "debug")


@dataclass(frozen=True)
class SampleSubstitution:
    filtered_word: FilteredWordSegment
    replacement: ReplacementSegment


@dataclass
class SampleSubstitutionMetrics:
    sample_lookups: int = 0
    total_lookup_ms: float = 0.0
    missing_sample_fallbacks: int = 0


class SampleSubstitutionEngine:
    def __init__(
        self,
        sample_bank: SampleBank,
        whitelist: Iterable[str],
        fallback_policy: str = "strict",
        config: AudioConfig = AudioConfig(),
    ) -> None:
        self.sample_bank = sample_bank
        self.whitelist = {normalize_word(word) for word in whitelist if normalize_word(word)}
        self.fallback_policy = normalize_fallback_policy(fallback_policy)
        self.config = config
        self.metrics = SampleSubstitutionMetrics()

    def substitute(self, word_event: WordEvent) -> SampleSubstitution:
        if not word_event.is_final:
            raise ValueError("Sample substitution only accepts finalized word events.")

        target_seconds = word_event.duration_seconds
        normalized = word_event.normalized_text

        lookup_started = perf_counter()
        has_whitelist_word = self.sample_bank.has_whitelist_word(normalized)
        self._record_lookup_time(lookup_started, normalized)

        if has_whitelist_word:
            filtered_word = _filtered_word_from_event(word_event, allowed=True, reason="sample-bank-whitelist")
            output_pcm = self.sample_bank.render_whitelist_word(normalized, target_seconds)
            return SampleSubstitution(
                filtered_word=filtered_word,
                replacement=ReplacementSegment(
                    source=filtered_word,
                    output_pcm=output_pcm,
                    is_original_audio=True,
                ),
            )

        if normalized in self.whitelist:
            filtered_word = _filtered_word_from_event(word_event, allowed=False, reason="missing-whitelist-sample")
            return self._handle_missing_whitelist_word(filtered_word, word_event)

        filtered_word = _filtered_word_from_event(word_event, allowed=False, reason="not-whitelisted")
        return self._gibberish_substitution(filtered_word, word_event)

    def _handle_missing_whitelist_word(
        self,
        filtered_word: FilteredWordSegment,
        word_event: WordEvent,
    ) -> SampleSubstitution:
        self.metrics.missing_sample_fallbacks += 1
        logger.warning(
            "Missing whitelist sample fallback: word=%r policy=%s",
            word_event.normalized_text,
            self.fallback_policy,
        )
        if self.fallback_policy == "strict":
            raise ValueError(f"Missing whitelist sample for recognized word: {word_event.normalized_text}")
        if self.fallback_policy == "debug":
            logger.warning("Missing whitelist sample for %r; outputting silence.", word_event.normalized_text)
            output_pcm = _silence(word_event.duration_seconds, self.config)
            return SampleSubstitution(
                filtered_word=filtered_word,
                replacement=ReplacementSegment(
                    source=filtered_word,
                    output_pcm=output_pcm,
                    is_original_audio=True,
                    error="missing-whitelist-sample-debug-silence",
                ),
            )
        return self._gibberish_substitution(filtered_word, word_event)

    def _record_lookup_time(self, started: float, normalized_word: str) -> None:
        elapsed_ms = (perf_counter() - started) * 1000.0
        self.metrics.sample_lookups += 1
        self.metrics.total_lookup_ms += elapsed_ms
        logger.info(
            "Sample lookup: word=%r elapsed_ms=%.3f",
            normalized_word,
            elapsed_ms,
        )

    def _gibberish_substitution(
        self,
        filtered_word: FilteredWordSegment,
        word_event: WordEvent,
    ) -> SampleSubstitution:
        bucket = duration_bucket(word_event.duration_seconds)
        output_pcm = self.sample_bank.render_gibberish(
            f"{word_event.normalized_text}:{word_event.start_ms}:{word_event.end_ms}",
            word_event.duration_seconds,
        )
        return SampleSubstitution(
            filtered_word=filtered_word,
            replacement=ReplacementSegment(
                source=filtered_word,
                output_pcm=output_pcm,
                is_original_audio=False,
                gibberish=GibberishToken(
                    source_word=word_event.raw_text,
                    normalized_word=word_event.normalized_text,
                    text=f"bank-{bucket}",
                    syllable_count=0,
                ),
            ),
        )


def normalize_fallback_policy(value: str) -> str:
    normalized = str(value or "strict").strip().lower()
    if normalized not in FALLBACK_POLICIES:
        return "strict"
    return normalized


def _filtered_word_from_event(word_event: WordEvent, allowed: bool, reason: str) -> FilteredWordSegment:
    original = WordResult(
        word=word_event.raw_text,
        start=word_event.start_seconds,
        end=word_event.end_seconds,
        confidence=word_event.confidence,
    )
    decision = WordDecision(
        original=original,
        normalized_word=word_event.normalized_text,
        allowed=allowed,
        reason=reason,
    )
    return FilteredWordSegment(
        decision=decision,
        audio=AudioSlice(
            pcm=b"",
            start=word_event.start_seconds,
            end=word_event.end_seconds,
        ),
    )


def _silence(duration_seconds: float, config: AudioConfig) -> bytes:
    sample_count = max(0, round(duration_seconds * config.sample_rate))
    return b"\x00" * sample_count * config.channels * config.sample_width_bytes
