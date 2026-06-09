from __future__ import annotations

import json
import os
from dataclasses import asdict, dataclass, replace
from pathlib import Path
from threading import Lock
from typing import Any

from live_gibberish.asr import create_asr
from live_gibberish.audio_io import AudioConfig
from live_gibberish.processor import LiveGibberishProcessor
from live_gibberish.tts import create_tts_engine
from live_gibberish.vad import create_vad
from live_gibberish.worker import ProcessorWorkerConfig, WorkerBackedProcessor


FASTER_WHISPER_MODELS = {
    "tiny.en",
    "tiny",
    "base.en",
    "base",
    "small.en",
    "small",
    "medium.en",
    "medium",
    "large-v1",
    "large-v2",
    "large-v3",
    "large",
    "distil-large-v2",
    "distil-medium.en",
    "distil-small.en",
    "distil-large-v3",
    "distil-large-v3.5",
    "large-v3-turbo",
    "turbo",
}
DEFAULT_FASTER_WHISPER_MODEL = "base.en"


@dataclass(frozen=True)
class RuntimeConfig:
    enabled: bool = True
    whitelist: tuple[str, ...] = ()
    seed: str = "cavegame-live-gibberish"
    confidence: float = 0.70
    buffer_seconds: float = 5.0
    asr_backend: str = "faster-whisper"
    asr_model: str = DEFAULT_FASTER_WHISPER_MODEL
    tts_backend: str = "coqui-xtts"
    use_worker: bool = False


CONFIG_PATH = Path(os.environ.get("LIVE_GIBBERISH_CONFIG", "runtime/live-gibberish-config.json"))


def _load_config() -> RuntimeConfig:
    if not CONFIG_PATH.exists():
        return RuntimeConfig()
    try:
        data = json.loads(CONFIG_PATH.read_text(encoding="utf-8"))
        if "whitelist" in data:
            data["whitelist"] = tuple(data["whitelist"])
        allowed = {field.name for field in RuntimeConfig.__dataclass_fields__.values()}
        return normalize_config(RuntimeConfig(**{key: value for key, value in data.items() if key in allowed}))
    except Exception:
        return RuntimeConfig()


def _save_config(config: RuntimeConfig) -> None:
    CONFIG_PATH.parent.mkdir(parents=True, exist_ok=True)
    CONFIG_PATH.write_text(json.dumps(asdict(config), indent=2), encoding="utf-8")


_config = _load_config()
_lock = Lock()


def get_config() -> RuntimeConfig:
    with _lock:
        return _config


def get_status() -> dict[str, Any]:
    config = get_config()
    payload = asdict(config)
    payload["sample_rate"] = AudioConfig().sample_rate
    payload["frame_ms"] = AudioConfig().frame_ms
    payload["model_hint"] = model_hint(config.asr_backend)
    payload["tts_hint"] = tts_hint(config.tts_backend)
    return payload


def update_config(data: dict[str, Any]) -> RuntimeConfig:
    global _config
    allowed_fields = {
        "enabled",
        "whitelist",
        "seed",
        "confidence",
        "buffer_seconds",
        "asr_backend",
        "asr_model",
        "tts_backend",
        "use_worker",
    }
    updates = {key: value for key, value in data.items() if key in allowed_fields}
    if "whitelist" in updates:
        updates["whitelist"] = tuple(str(word) for word in updates["whitelist"])
    if "confidence" in updates:
        updates["confidence"] = float(updates["confidence"])
    if "buffer_seconds" in updates:
        updates["buffer_seconds"] = float(updates["buffer_seconds"])

    with _lock:
        _config = normalize_config(replace(_config, **updates))
        _save_config(_config)
        return _config


def set_enabled(enabled: bool) -> RuntimeConfig:
    return update_config({"enabled": enabled})


def build_processor(config: RuntimeConfig | None = None):
    config = normalize_config(config or get_config())
    if config.use_worker:
        return WorkerBackedProcessor(
            ProcessorWorkerConfig(
                asr_backend=config.asr_backend,
                asr_model=config.asr_model,
                whitelist=config.whitelist,
                seed=config.seed,
                confidence=config.confidence,
                buffer_seconds=config.buffer_seconds,
                tts_backend=config.tts_backend,
            )
        )
    return LiveGibberishProcessor(
        asr=create_asr(config.asr_backend, model=config.asr_model, whitelist=config.whitelist),
        vad=create_vad(),
        tts=create_tts_engine(config.tts_backend),
        whitelist=list(config.whitelist),
        seed=config.seed,
        config=AudioConfig(),
        confidence_threshold=config.confidence,
        buffer_seconds=config.buffer_seconds,
    )


def normalize_config(config: RuntimeConfig) -> RuntimeConfig:
    backend = str(config.asr_backend or "faster-whisper").strip().lower()
    model = str(config.asr_model or "").strip()
    tts_backend = str(config.tts_backend or "coqui-xtts").strip().lower()
    if tts_backend in {"coqui", "xtts"}:
        tts_backend = "coqui-xtts"
    if tts_backend != "coqui-xtts":
        tts_backend = "coqui-xtts"

    if backend in {"faster_whisper", "whisper"}:
        backend = "faster-whisper"

    if backend == "faster-whisper":
        if model not in FASTER_WHISPER_MODELS:
            model = DEFAULT_FASTER_WHISPER_MODEL
        return replace(config, asr_backend=backend, asr_model=model, tts_backend=tts_backend)

    return replace(config, asr_backend="faster-whisper", asr_model=DEFAULT_FASTER_WHISPER_MODEL, tts_backend=tts_backend)


def model_hint(asr_backend: str) -> str:
    backend = str(asr_backend or "faster-whisper").strip().lower()
    if backend in {"faster-whisper", "faster_whisper", "whisper"}:
        return f"GPU-only faster-whisper. Use one of: {', '.join(sorted(FASTER_WHISPER_MODELS))}."
    return "GPU-only ASR is enforced; unsupported ASR backends are reset to faster-whisper."


def tts_hint(tts_backend: str) -> str:
    backend = str(tts_backend or "coqui-xtts").strip().lower()
    if backend in {"coqui", "coqui-xtts", "xtts"}:
        return "GPU-only Coqui XTTS. Requires: python scripts\\setup_real_backends.py --coqui-xtts and an enrollment WAV."
    return "GPU-only TTS is enforced; unsupported TTS backends are reset to coqui-xtts."
