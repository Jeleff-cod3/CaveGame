from __future__ import annotations

import re
from dataclasses import dataclass
from typing import Iterable

from .asr import WordResult


_WORD_EDGE_PATTERN = re.compile(r"(^[^\w]+|[^\w]+$)")


@dataclass(frozen=True)
class WordDecision:
    original: WordResult
    normalized_word: str
    allowed: bool
    reason: str


class WhitelistChecker:
    def __init__(
        self,
        whitelist: Iterable[str],
        confidence_threshold: float = 0.70,
        filler_words: Iterable[str] = ("um", "uh", "erm", "ah"),
    ) -> None:
        self.allowed_words = {normalize_word(word) for word in whitelist if normalize_word(word)}
        self.confidence_threshold = confidence_threshold
        self.filler_words = {normalize_word(word) for word in filler_words if normalize_word(word)}

    def check(self, word: WordResult) -> WordDecision:
        normalized = normalize_word(word.word)
        if not normalized:
            return WordDecision(word, normalized, allowed=False, reason="empty")
        if normalized in self.filler_words:
            return WordDecision(word, normalized, allowed=False, reason="filler")
        if normalized in self.allowed_words:
            return WordDecision(word, normalized, allowed=True, reason="whitelist")
        fuzzy_match = _closest_whitelist_match(normalized, self.allowed_words)
        if fuzzy_match and word.confidence >= self.confidence_threshold:
            return WordDecision(word, normalized, allowed=True, reason=f"whitelist-fuzzy:{fuzzy_match}")
        if word.confidence < self.confidence_threshold:
            return WordDecision(word, normalized, allowed=False, reason="low-confidence")
        return WordDecision(word, normalized, allowed=False, reason="not-whitelisted")

    def check_all(self, words: Iterable[WordResult]) -> tuple[WordDecision, ...]:
        return tuple(self.check(word) for word in words)


def normalize_word(word: str) -> str:
    lowered = word.strip().casefold()
    return _WORD_EDGE_PATTERN.sub("", lowered)


def _closest_whitelist_match(word: str, allowed_words: set[str]) -> str | None:
    if len(word) < 4:
        return None
    for allowed in allowed_words:
        if len(allowed) < 4:
            continue
        max_distance = 1 if max(len(word), len(allowed)) <= 6 else 2
        if abs(len(word) - len(allowed)) > max_distance:
            continue
        if _levenshtein_at_most(word, allowed, max_distance):
            return allowed
    return None


def _levenshtein_at_most(left: str, right: str, limit: int) -> bool:
    if abs(len(left) - len(right)) > limit:
        return False
    previous = list(range(len(right) + 1))
    for left_index, left_char in enumerate(left, start=1):
        current = [left_index]
        row_min = current[0]
        for right_index, right_char in enumerate(right, start=1):
            cost = 0 if left_char == right_char else 1
            value = min(
                previous[right_index] + 1,
                current[right_index - 1] + 1,
                previous[right_index - 1] + cost,
            )
            current.append(value)
            row_min = min(row_min, value)
        if row_min > limit:
            return False
        previous = current
    return previous[-1] <= limit
