from __future__ import annotations

import hashlib
import hmac
from dataclasses import dataclass

from .filtering import normalize_word


CONSONANTS = (
    "b",
    "d",
    "f",
    "g",
    "h",
    "j",
    "k",
    "l",
    "m",
    "n",
    "p",
    "r",
    "s",
    "t",
    "v",
    "z",
    "sh",
    "th",
    "ch",
)
VOWELS = ("a", "e", "i", "o", "u", "ai", "oo")
CODAS = ("", "", "", "m", "n", "r", "s", "l", "th")


@dataclass(frozen=True)
class GibberishToken:
    source_word: str
    normalized_word: str
    text: str
    syllable_count: int


class GibberishMapper:
    def __init__(self, seed: str = "cavegame-live-gibberish") -> None:
        self.seed = seed

    def map_word(self, word: str) -> GibberishToken:
        normalized = normalize_word(word)
        syllable_count = estimate_syllable_count(normalized)
        digest = hmac.new(
            self.seed.encode("utf-8"),
            normalized.encode("utf-8"),
            hashlib.sha256,
        ).digest()
        parts = [
            _syllable_from_digest(digest, syllable_index)
            for syllable_index in range(syllable_count)
        ]
        text = _smooth("".join(parts))
        return GibberishToken(
            source_word=word,
            normalized_word=normalized,
            text=text,
            syllable_count=syllable_count,
        )


def estimate_syllable_count(word: str) -> int:
    if not word:
        return 1
    groups = 0
    in_vowel_group = False
    for character in word:
        is_vowel = character in "aeiouy"
        if is_vowel and not in_vowel_group:
            groups += 1
        in_vowel_group = is_vowel
    if word.endswith("e") and groups > 1:
        groups -= 1
    return max(1, min(4, groups or round(len(word) / 4)))


def _syllable_from_digest(digest: bytes, syllable_index: int) -> str:
    offset = (syllable_index * 3) % len(digest)
    onset = CONSONANTS[digest[offset] % len(CONSONANTS)]
    vowel = VOWELS[digest[offset + 1] % len(VOWELS)]
    coda = CODAS[digest[offset + 2] % len(CODAS)]
    return onset + vowel + coda


def _smooth(text: str) -> str:
    replacements = {
        "ooo": "oo",
        "aii": "ai",
        "ii": "i",
        "uu": "u",
    }
    for before, after in replacements.items():
        text = text.replace(before, after)
    return text

