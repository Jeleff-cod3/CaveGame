from __future__ import annotations

import argparse
import json
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from live_gibberish.asr import create_asr
from live_gibberish.audio_io import AudioConfig, WavFrameSource
from live_gibberish.processor import LiveGibberishProcessor
from live_gibberish.speaker import SpeakerEnrollment
from live_gibberish.tts import create_tts_engine
from live_gibberish.vad import create_vad
from live_gibberish.wav_utils import write_wav


def main() -> None:
    parser = argparse.ArgumentParser(description="Benchmark live gibberish on a 16 kHz mono 16-bit WAV.")
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--asr", default="faster-whisper", choices=["faster-whisper"])
    parser.add_argument("--model", default="base.en")
    parser.add_argument("--whitelist", nargs="*", default=[])
    parser.add_argument("--tts", default="coqui-xtts", choices=["coqui-xtts"])
    parser.add_argument("--seed", default="cavegame-live-gibberish")
    parser.add_argument("--enrollment-wav", type=Path)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--report", type=Path, default=Path("benchmark-report.json"))
    args = parser.parse_args()

    config = AudioConfig()
    speaker_profile = (
        SpeakerEnrollment(config=config).from_wav(args.enrollment_wav)
        if args.enrollment_wav
        else None
    )
    processor = LiveGibberishProcessor(
        asr=create_asr(args.asr, model=args.model, whitelist=args.whitelist),
        vad=create_vad(),
        tts=create_tts_engine(args.tts),
        whitelist=args.whitelist,
        seed=args.seed,
        config=config,
        speaker_profile=speaker_profile,
    )

    segments = []
    output = bytearray()
    start = time.perf_counter()
    for frame in WavFrameSource(args.source, config=config).frames():
        segment_start = time.perf_counter()
        result = processor.accept_frame(frame.pcm, frame.timestamp)
        if result is None:
            continue
        elapsed_ms = (time.perf_counter() - segment_start) * 1000
        output.extend(result.output_pcm)
        segments.append(
            {
                "start": result.speech.start_timestamp,
                "end": result.speech.end_timestamp,
                "text": result.transcript.text,
                "words": len(result.filtered_words),
                "output_bytes": len(result.output_pcm),
                "processing_ms": elapsed_ms,
            }
        )

    result = processor.flush()
    if result is not None:
        output.extend(result.output_pcm)
        segments.append(
            {
                "start": result.speech.start_timestamp,
                "end": result.speech.end_timestamp,
                "text": result.transcript.text,
                "words": len(result.filtered_words),
                "output_bytes": len(result.output_pcm),
                "processing_ms": None,
            }
        )

    total_ms = (time.perf_counter() - start) * 1000
    report = {
        "source": str(args.source),
        "asr": args.asr,
        "tts": args.tts,
        "segments": segments,
        "total_ms": total_ms,
        "audio_output_bytes": len(output),
    }
    args.report.write_text(json.dumps(report, indent=2), encoding="utf-8")
    if args.output:
        write_wav(args.output, bytes(output), config)
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
