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
        if word.confidence < self.confidence_threshold:
            return WordDecision(word, normalized, allowed=False, reason="low-confidence")
        if normalized in self.allowed_words:
            return WordDecision(word, normalized, allowed=True, reason="whitelist")
        return WordDecision(word, normalized, allowed=False, reason="not-whitelisted")

    def check_all(self, words: Iterable[WordResult]) -> tuple[WordDecision, ...]:
        return tuple(self.check(word) for word in words)


def normalize_word(word: str) -> str:
    lowered = word.strip().casefold()
    return _WORD_EDGE_PATTERN.sub("", lowered)
