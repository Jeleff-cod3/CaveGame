from __future__ import annotations

import wave
from pathlib import Path

import numpy as np

from .audio_io import AudioConfig


def read_wav_as_config(path: str | Path, config: AudioConfig = AudioConfig()) -> bytes:
    with wave.open(str(path), "rb") as wav:
        channels = wav.getnchannels()
        sample_width = wav.getsampwidth()
        sample_rate = wav.getframerate()
        pcm = wav.readframes(wav.getnframes())

    if sample_width != 2:
        raise ValueError(f"Expected 16-bit WAV, got {sample_width * 8}-bit.")

    samples = np.frombuffer(pcm, dtype=np.int16)
    if channels > 1:
        samples = samples.reshape(-1, channels).mean(axis=1).astype(np.int16)
    if sample_rate != config.sample_rate:
        samples = resample_pcm16(samples, sample_rate, config.sample_rate)
    return samples.astype(np.int16).tobytes()


def write_wav(path: str | Path, pcm: bytes, config: AudioConfig = AudioConfig()) -> None:
    with wave.open(str(path), "wb") as wav:
        wav.setnchannels(config.channels)
        wav.setsampwidth(config.sample_width_bytes)
        wav.setframerate(config.sample_rate)
        wav.writeframes(pcm)


def resample_pcm16(samples: np.ndarray, source_rate: int, target_rate: int) -> np.ndarray:
    if source_rate == target_rate or len(samples) == 0:
        return samples.astype(np.int16)

    duration = len(samples) / source_rate
    target_count = max(1, round(duration * target_rate))
    source_positions = np.linspace(0.0, duration, num=len(samples), endpoint=False)
    target_positions = np.linspace(0.0, duration, num=target_count, endpoint=False)
    resampled = np.interp(target_positions, source_positions, samples.astype(np.float32))
    return np.clip(resampled, -32768, 32767).astype(np.int16)

