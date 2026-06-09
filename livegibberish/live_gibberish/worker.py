from __future__ import annotations

import multiprocessing as mp
from dataclasses import dataclass
from typing import Optional

from .asr import create_asr
from .audio_io import AudioConfig
from .processor import LiveGibberishProcessor, ProcessedSegment
from .tts import create_tts_engine
from .vad import create_vad


@dataclass(frozen=True)
class ProcessorWorkerConfig:
    asr_backend: str = "faster-whisper"
    asr_model: str = "base.en"
    whitelist: tuple[str, ...] = ()
    seed: str = "cavegame-live-gibberish"
    confidence: float = 0.70
    buffer_seconds: float = 5.0
    tts_backend: str = "coqui-xtts"


class WorkerBackedProcessor:
    def __init__(self, config: ProcessorWorkerConfig) -> None:
        self.config = config
        self._requests: mp.Queue = mp.Queue()
        self._responses: mp.Queue = mp.Queue()
        self._process = mp.Process(
            target=_worker_main,
            args=(config, self._requests, self._responses),
            daemon=True,
        )
        self._process.start()

    def accept_frame(self, pcm: bytes, timestamp: float) -> Optional[ProcessedSegment]:
        self._requests.put(("frame", pcm, timestamp))
        kind, payload = self._responses.get()
        if kind == "error":
            raise RuntimeError(payload)
        return payload

    def flush(self) -> Optional[ProcessedSegment]:
        self._requests.put(("flush", None, None))
        kind, payload = self._responses.get()
        if kind == "error":
            raise RuntimeError(payload)
        return payload

    def close(self) -> None:
        if self._process.is_alive():
            self._requests.put(("stop", None, None))
            self._process.join(timeout=2.0)
        if self._process.is_alive():
            self._process.terminate()
            self._process.join(timeout=1.0)


def _worker_main(config: ProcessorWorkerConfig, requests: mp.Queue, responses: mp.Queue) -> None:
    try:
        audio_config = AudioConfig()
        processor = LiveGibberishProcessor(
            asr=create_asr(config.asr_backend, model=config.asr_model, whitelist=config.whitelist),
            vad=create_vad(),
            tts=create_tts_engine(config.tts_backend),
            whitelist=list(config.whitelist),
            seed=config.seed,
            config=audio_config,
            confidence_threshold=config.confidence,
            buffer_seconds=config.buffer_seconds,
        )
        while True:
            command, pcm, timestamp = requests.get()
            if command == "stop":
                break
            if command == "flush":
                responses.put(("ok", processor.flush()))
                continue
            if command == "frame":
                responses.put(("ok", processor.accept_frame(pcm, timestamp)))
                continue
            responses.put(("error", f"Unknown worker command: {command}"))
    except Exception as exc:
        responses.put(("error", f"{type(exc).__name__}: {exc}"))
