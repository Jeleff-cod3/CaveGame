from __future__ import annotations

import math
import wave
from pathlib import Path
from contextlib import contextmanager
from dataclasses import dataclass
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
        with _allow_trusted_coqui_checkpoint_load():
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


@contextmanager
def _allow_trusted_coqui_checkpoint_load():
    import torch

    original_load = torch.load

    def load_with_legacy_checkpoint_support(*args, **kwargs):
        kwargs.setdefault("weights_only", False)
        return original_load(*args, **kwargs)

    torch.load = load_with_legacy_checkpoint_support
    try:
        yield
    finally:
        torch.load = original_load


def match_duration(pcm: bytes, target_seconds: float, config: AudioConfig) -> bytes:
    return apply_fade(_match_duration_raw(pcm, target_seconds, config), config)


def match_source_character(
    pcm: bytes,
    source_pcm: bytes,
    target_seconds: float,
    config: AudioConfig,
) -> bytes:
    duration_matched = _match_duration_raw(pcm, target_seconds, config)
    pitch_matched = _match_pitch_to_source(duration_matched, source_pcm, config)
    duration_matched = _match_duration_raw(pitch_matched, target_seconds, config)
    leveled = _match_loudness_to_source(duration_matched, source_pcm)
    return apply_fade(leveled, config)


def _match_duration_raw(pcm: bytes, target_seconds: float, config: AudioConfig) -> bytes:
    target_bytes = _duration_to_bytes(target_seconds, config)
    if target_bytes <= 0:
        return b""
    if len(pcm) >= target_bytes:
        return pcm[:target_bytes]
    return pcm + (b"\x00" * (target_bytes - len(pcm)))


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


def _match_pitch_to_source(pcm: bytes, source_pcm: bytes, config: AudioConfig) -> bytes:
    if not pcm or not source_pcm:
        return pcm

    try:
        import numpy as np
    except ImportError:
        return pcm

    source_samples = _pcm16_to_float(source_pcm, np)
    output_samples = _pcm16_to_float(pcm, np)
    source_pitch = _estimate_pitch_hz(source_samples, config.sample_rate, np)
    output_pitch = _estimate_pitch_hz(output_samples, config.sample_rate, np)
    if source_pitch is None or output_pitch is None:
        return pcm

    ratio = source_pitch / output_pitch
    if not math.isfinite(ratio):
        return pcm

    ratio = float(np.clip(ratio, 0.70, 1.40))
    if 0.96 <= ratio <= 1.04:
        return pcm

    shifted = _resample_for_pitch(output_samples, ratio, np)
    return _float_to_pcm16(shifted, np)


def _match_loudness_to_source(pcm: bytes, source_pcm: bytes) -> bytes:
    if not pcm or not source_pcm:
        return pcm

    try:
        import numpy as np
    except ImportError:
        return pcm

    source_samples = _pcm16_to_float(source_pcm, np)
    output_samples = _pcm16_to_float(pcm, np)
    source_rms = _rms(source_samples, np)
    output_rms = _rms(output_samples, np)
    if source_rms <= 1.0 or output_rms <= 1.0:
        return pcm

    gain = float(np.clip(source_rms / output_rms, 0.05, 8.0))
    source_peak = float(np.max(np.abs(source_samples))) if source_samples.size else 0.0
    output_peak = float(np.max(np.abs(output_samples))) if output_samples.size else 0.0
    if output_peak > 0.0:
        peak_ceiling = max(source_peak * 1.15, source_rms * 2.5, 128.0)
        peak_ceiling = min(32767.0, peak_ceiling)
        gain = min(gain, peak_ceiling / output_peak)

    return _float_to_pcm16(output_samples * gain, np)


def _estimate_pitch_hz(samples, sample_rate: int, np) -> Optional[float]:
    if sample_rate <= 0:
        return None

    window = _pitch_analysis_window(samples, np)
    minimum_samples = max(1, sample_rate // 70)
    if window.size < minimum_samples:
        return None

    window = window.astype(np.float32)
    window -= float(np.mean(window))
    energy = float(np.dot(window, window))
    if energy <= 1.0:
        return None

    min_lag = max(1, round(sample_rate / 350.0))
    max_lag = min(window.size - 1, round(sample_rate / 70.0))
    if max_lag <= min_lag:
        return None

    autocorrelation = np.correlate(window, window, mode="full")[window.size - 1 :]
    candidates = autocorrelation[min_lag : max_lag + 1]
    if candidates.size == 0:
        return None

    best_offset = int(np.argmax(candidates))
    best_score = float(candidates[best_offset])
    if best_score / energy < 0.18:
        return None

    best_lag = min_lag + best_offset
    return sample_rate / best_lag


def _pitch_analysis_window(samples, np):
    if samples.size == 0:
        return samples

    absolute = np.abs(samples)
    peak = float(np.max(absolute))
    if peak <= 64.0:
        return samples[:0]

    voiced_indices = np.flatnonzero(absolute >= peak * 0.12)
    if voiced_indices.size:
        start = int(voiced_indices[0])
        end = int(voiced_indices[-1]) + 1
        samples = samples[start:end]

    max_samples = 4096
    if samples.size > max_samples:
        start = (samples.size - max_samples) // 2
        samples = samples[start : start + max_samples]
    return samples


def _resample_for_pitch(samples, ratio: float, np):
    if samples.size < 2:
        return samples

    output_count = max(1, round(samples.size / ratio))
    source_positions = np.arange(output_count, dtype=np.float32) * ratio
    source_positions = np.clip(source_positions, 0.0, float(samples.size - 1))
    source_indices = np.arange(samples.size, dtype=np.float32)
    return np.interp(source_positions, source_indices, samples).astype(np.float32)


def _pcm16_to_float(pcm: bytes, np):
    usable_bytes = len(pcm) - (len(pcm) % 2)
    if usable_bytes <= 0:
        return np.array([], dtype=np.float32)
    return np.frombuffer(pcm[:usable_bytes], dtype=np.int16).astype(np.float32)


def _float_to_pcm16(samples, np) -> bytes:
    return np.clip(samples, -32768, 32767).astype(np.int16).tobytes()


def _rms(samples, np) -> float:
    if samples.size == 0:
        return 0.0
    return float(np.sqrt(np.mean(samples.astype(np.float32) ** 2)))


def _scale_sample(pcm: bytearray, sample_index: int, scale: float) -> None:
    byte_index = sample_index * 2
    sample = int.from_bytes(pcm[byte_index : byte_index + 2], byteorder="little", signed=True)
    scaled = round(sample * scale)
    pcm[byte_index : byte_index + 2] = int(scaled).to_bytes(2, byteorder="little", signed=True)
