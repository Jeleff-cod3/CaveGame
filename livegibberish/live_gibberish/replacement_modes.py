from __future__ import annotations


ORIGINAL_GIBBERISH_MODE = "original_gibberish"
PRERECORDED_SAMPLE_SUBSTITUTION_MODE = "prerecorded_sample_substitution"
REPLACEMENT_MODES = (ORIGINAL_GIBBERISH_MODE, PRERECORDED_SAMPLE_SUBSTITUTION_MODE)


def normalize_audio_replacement_mode(value: str) -> str:
    normalized = str(value or ORIGINAL_GIBBERISH_MODE).strip().lower()
    if normalized not in REPLACEMENT_MODES:
        return ORIGINAL_GIBBERISH_MODE
    return normalized
