from __future__ import annotations

import wave
from dataclasses import dataclass
from pathlib import Path
from typing import Optional, Protocol

from .audio_io import AudioConfig
from .wav_utils import read_wav_as_config


@dataclass(frozen=True)
class SynthesizedSpeech:
    text: str
    pcm: bytes
    sample_rate: int
    voice_id: Optional[str] = None


class TtsEngine(Protocol):
    def synthesize(self, text: str, config: AudioConfig, voice_id: Optional[str] = None) -> SynthesizedSpeech:
        """Generate speech PCM for text at runtime."""


class CoquiXttsEngine:
    def __init__(
        self,
        model_name: str = "tts_models/multilingual/multi-dataset/xtts_v2",
        language: str = "en",
        device: str = "cuda",
    ) -> None:
        try:
            from TTS.api import TTS
        except ImportError as exc:
            raise RuntimeError("CoquiXttsEngine requires the optional TTS package.") from exc

        self.language = language
        self._model = TTS(model_name)
        if hasattr(self._model, "to"):
            self._model.to(device)

    def synthesize(self, text: str, config: AudioConfig, voice_id: Optional[str] = None) -> SynthesizedSpeech:
        if not voice_id:
            raise ValueError("Coqui XTTS requires voice_id to point to a speaker reference WAV.")

        import tempfile

        with tempfile.TemporaryDirectory() as temp_dir:
            path = Path(temp_dir) / "xtts.wav"
            self._model.tts_to_file(
                text=text,
                speaker_wav=str(voice_id),
                language=self.language,
                file_path=str(path),
            )
            pcm = read_wav_as_config(path, config)
        return SynthesizedSpeech(text=text, pcm=pcm, sample_rate=config.sample_rate, voice_id=voice_id)


def create_tts_engine(backend: str) -> TtsEngine:
    normalized = backend.lower().strip()
    if normalized in {"coqui", "coqui-xtts", "xtts"}:
        return CoquiXttsEngine()
    raise ValueError(f"Unsupported TTS backend: {backend}. This app is GPU-only; use coqui-xtts.")


def match_duration(pcm: bytes, target_seconds: float, config: AudioConfig) -> bytes:
    target_bytes = _duration_to_bytes(target_seconds, config)
    if target_bytes <= 0:
        return b""
    if len(pcm) >= target_bytes:
        return apply_fade(pcm[:target_bytes], config)
    return apply_fade(pcm + (b"\x00" * (target_bytes - len(pcm))), config)


def apply_fade(pcm: bytes, config: AudioConfig, fade_ms: int = 10) -> bytes:
    sample_width = config.sample_width_bytes * config.channels
    sample_count = len(pcm) // sample_width
    fade_samples = min(sample_count // 2, round(config.sample_rate * fade_ms / 1000))
    if fade_samples <= 0:
        return pcm

    output = bytearray(pcm)
    for index in range(fade_samples):
        scale = index / fade_samples
        _scale_sample(output, index, scale)
        _scale_sample(output, sample_count - index - 1, scale)
    return bytes(output)


def _read_wav_pcm(path: Path, config: AudioConfig) -> bytes:
    with wave.open(str(path), "rb") as wav:
        if wav.getnchannels() != config.channels:
            raise ValueError("TTS WAV channel count did not match AudioConfig.")
        if wav.getsampwidth() != config.sample_width_bytes:
            raise ValueError("TTS WAV sample width did not match AudioConfig.")
        if wav.getframerate() != config.sample_rate:
            raise ValueError("TTS WAV sample rate did not match AudioConfig.")
        return wav.readframes(wav.getnframes())


def _duration_to_bytes(seconds: float, config: AudioConfig) -> int:
    samples = max(0, round(seconds * config.sample_rate))
    return samples * config.channels * config.sample_width_bytes


def _scale_sample(pcm: bytearray, sample_index: int, scale: float) -> None:
    byte_index = sample_index * 2
    sample = int.from_bytes(pcm[byte_index : byte_index + 2], byteorder="little", signed=True)
    scaled = round(sample * scale)
    pcm[byte_index : byte_index + 2] = int(scaled).to_bytes(2, byteorder="little", signed=True)
