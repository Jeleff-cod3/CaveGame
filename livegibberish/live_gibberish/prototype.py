from __future__ import annotations

import argparse
import time
from pathlib import Path
from typing import Optional

from .asr import create_asr
from .audio_io import AudioConfig, AudioFrameSource, MicrophoneFrameSource, WavFrameSource, WavSink
from .processor import LiveGibberishProcessor, ProcessedSegment
from .speaker import SpeakerEnrollment
from .tts import create_tts_engine
from .vad import create_vad


def run(
    source: AudioFrameSource,
    asr_backend: str,
    model: Optional[str],
    whitelist: list[str],
    seconds: Optional[float],
    output: Optional[Path],
    replacement_output: Optional[Path],
    confidence_threshold: float,
    buffer_seconds: float,
    gibberish_seed: str,
    tts_backend: str,
    enrollment_wav: Optional[Path],
    voice_id: Optional[str],
) -> int:
    config = AudioConfig()
    speaker_profile = (
        SpeakerEnrollment(config=config).from_wav(enrollment_wav, voice_id=voice_id)
        if enrollment_wav
        else None
    )
    processor = LiveGibberishProcessor(
        asr=create_asr(asr_backend, model=model, whitelist=whitelist),
        vad=create_vad(),
        tts=create_tts_engine(tts_backend),
        whitelist=whitelist,
        seed=gibberish_seed,
        config=config,
        confidence_threshold=confidence_threshold,
        buffer_seconds=buffer_seconds,
        speaker_profile=speaker_profile,
    )
    sink = WavSink(output, config=config) if output else None
    replacement_sink = WavSink(replacement_output, config=config) if replacement_output else None

    start_time = time.monotonic()
    segment_count = 0
    try:
        for frame in source.frames():
            if sink:
                sink.write(frame.pcm)

            result = processor.accept_frame(frame.pcm, frame.timestamp)
            if result is not None:
                segment_count += 1
                if replacement_sink:
                    replacement_sink.write(result.output_pcm)
                print_segment(segment_count, result)

            if seconds is not None and time.monotonic() - start_time >= seconds:
                break

        result = processor.flush()
        if result is not None:
            segment_count += 1
            if replacement_sink:
                replacement_sink.write(result.output_pcm)
            print_segment(segment_count, result)
    finally:
        if sink:
            sink.close()
        if replacement_sink:
            replacement_sink.close()

    return segment_count


def print_segment(
    index: int,
    result: ProcessedSegment,
) -> None:
    label = result.transcript.text if result.transcript.text else "<no transcript>"
    print(f"[segment {index:03d}] {result.speech.start_timestamp:0.2f}s-{result.speech.end_timestamp:0.2f}s {label}")
    replacement_by_source = {id(replacement.source): replacement for replacement in result.replacements}
    for item in result.filtered_words:
        state = "allowed" if item.decision.allowed else "blocked"
        byte_count = len(item.audio.pcm)
        word = item.decision.normalized_word or item.decision.original.word
        replacement = replacement_by_source.get(id(item))
        replacement_text = ""
        if replacement and replacement.gibberish:
            replacement_text = f" -> {replacement.gibberish.text}"
        print(
            f"  - {state:<7} {word:<16} "
            f"{item.audio.start:0.2f}s-{item.audio.end:0.2f}s "
            f"{byte_count} bytes ({item.decision.reason}){replacement_text}"
        )


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Run the live gibberish steps 1-12 prototype.")
    parser.add_argument("--source", type=Path, help="16 kHz mono 16-bit WAV file. Uses microphone if omitted.")
    parser.add_argument("--output", type=Path, help="Optional WAV file to write captured input for loopback checks.")
    parser.add_argument("--replacement-output", type=Path, help="Optional WAV file for allowed/replaced word output.")
    parser.add_argument("--seconds", type=float, help="Stop after this many seconds.")
    parser.add_argument("--asr", default="faster-whisper", choices=["faster-whisper"])
    parser.add_argument("--model", default="base.en", help="faster-whisper GPU model name.")
    parser.add_argument("--whitelist", nargs="*", default=[], help="Words to pass to grammar-aware ASR backends.")
    parser.add_argument("--confidence", type=float, default=0.70, help="Minimum ASR word confidence to allow.")
    parser.add_argument("--buffer-seconds", type=float, default=5.0, help="Timed PCM buffer duration.")
    parser.add_argument("--seed", default="cavegame-live-gibberish", help="Secret seed for deterministic gibberish.")
    parser.add_argument("--tts", default="coqui-xtts", choices=["coqui-xtts"], help="GPU TTS backend.")
    parser.add_argument("--enrollment-wav", type=Path, help="Optional 5s+ enrollment WAV for future voice cloning.")
    parser.add_argument("--voice-id", help="Optional TTS voice ID, passed to engines that support it.")
    parser.add_argument("--realtime", action="store_true", help="Sleep between WAV frames to mimic live capture.")
    return parser


def main() -> None:
    args = build_parser().parse_args()
    config = AudioConfig()
    source: AudioFrameSource
    if args.source:
        source = WavFrameSource(args.source, config=config, realtime=args.realtime)
    else:
        source = MicrophoneFrameSource(config=config)

    count = run(
        source=source,
        asr_backend=args.asr,
        model=args.model,
        whitelist=args.whitelist,
        seconds=args.seconds,
        output=args.output,
        replacement_output=args.replacement_output,
        confidence_threshold=args.confidence,
        buffer_seconds=args.buffer_seconds,
        gibberish_seed=args.seed,
        tts_backend=args.tts,
        enrollment_wav=args.enrollment_wav,
        voice_id=args.voice_id,
    )
    print(f"Processed {count} speech segment(s).")


if __name__ == "__main__":
    main()
