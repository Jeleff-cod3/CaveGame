from __future__ import annotations

import hashlib
import wave
from dataclasses import dataclass
from pathlib import Path
from typing import Optional

from .audio_io import AudioConfig


@dataclass(frozen=True)
class SpeakerProfile:
    profile_id: str
    enrollment_path: Optional[Path] = None
    voice_id: Optional[str] = None
    embedding: Optional[tuple[float, ...]] = None

    @property
    def has_embedding(self) -> bool:
        return self.embedding is not None


class SpeakerEnrollment:
    def __init__(self, config: AudioConfig = AudioConfig(), min_seconds: float = 5.0) -> None:
        self.config = config
        self.min_seconds = min_seconds

    def from_wav(self, path: str | Path, voice_id: Optional[str] = None) -> SpeakerProfile:
        enrollment_path = Path(path)
        duration = _validate_enrollment_wav(enrollment_path, self.config)
        if duration < self.min_seconds:
            raise ValueError(
                f"Enrollment audio is {duration:0.2f}s; expected at least {self.min_seconds:0.2f}s."
            )
        profile_id = _profile_id(enrollment_path)
        return SpeakerProfile(
            profile_id=profile_id,
            enrollment_path=enrollment_path,
            voice_id=voice_id or str(enrollment_path),
        )


def _validate_enrollment_wav(path: Path, config: AudioConfig) -> float:
    with wave.open(str(path), "rb") as wav:
        if wav.getframerate() != config.sample_rate:
            raise ValueError(f"Expected {config.sample_rate} Hz enrollment WAV.")
        if wav.getnchannels() != config.channels:
            raise ValueError(f"Expected {config.channels} channel enrollment WAV.")
        if wav.getsampwidth() != config.sample_width_bytes:
            raise ValueError(f"Expected {config.sample_width_bytes * 8}-bit enrollment WAV.")
        return wav.getnframes() / wav.getframerate()


def _profile_id(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as file:
        while chunk := file.read(1024 * 64):
            digest.update(chunk)
    return digest.hexdigest()[:16]
