from __future__ import annotations

import re


_WORD_EDGE_PATTERN = re.compile(r"(^[^\w]+|[^\w]+$)")


def normalize_word(word: str) -> str:
    lowered = word.strip().casefold()
    return _WORD_EDGE_PATTERN.sub("", lowered)
