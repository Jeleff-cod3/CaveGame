from __future__ import annotations

import logging
from dataclasses import dataclass
from typing import Optional

from .asr import AsrEngine, Transcript
from .audio_bank import SampleBank
from .audio_output_scheduler import AudioOutputScheduler
from .audio_io import AudioConfig, AudioFrame
from .buffer import TimedAudioBuffer
from .filtering import WhitelistChecker
from .gibberish import GibberishMapper
from .pipeline import FilteredWordSegment, WordFilterPipeline
from .replacement import ReplacementAssembler, ReplacementEngine, ReplacementSegment
from .sample_substitution import SampleSubstitutionEngine
from .speaker import SpeakerProfile
from .tts import TtsEngine
from .vad import SpeechSegment, SpeechSegmenter, VoiceActivityDetector
from .word_events import transcript_to_final_word_events


logger = logging.getLogger(__name__)


@dataclass(frozen=True)
class ProcessedSegment:
    speech: SpeechSegment
    transcript: Transcript
    filtered_words: tuple[FilteredWordSegment, ...]
    replacements: tuple[ReplacementSegment, ...]
    output_pcm: bytes


@dataclass
class ProcessorMetrics:
    sample_lookup_ms: float = 0.0
    scheduler_underruns: int = 0
    asr_to_output_latency_ms: float = 0.0
    missing_sample_fallbacks: int = 0


class LiveGibberishProcessor:
    def __init__(
        self,
        asr: AsrEngine,
        vad: VoiceActivityDetector,
        tts: Optional[TtsEngine],
        whitelist: list[str],
        seed: str,
        config: AudioConfig = AudioConfig(),
        confidence_threshold: float = 0.70,
        buffer_seconds: float = 5.0,
        speaker_profile: Optional[SpeakerProfile] = None,
        sample_bank: Optional[SampleBank] = None,
        sample_fallback_policy: str = "strict",
    ) -> None:
        self.config = config
        self.metrics = ProcessorMetrics()
        self.whitelist = tuple(whitelist)
        self.seed = seed
        self.confidence_threshold = confidence_threshold
        self.buffer_seconds = buffer_seconds
        self.asr = asr
        self.segmenter = SpeechSegmenter(vad=vad, config=config)
        self.audio_buffer = TimedAudioBuffer(config=config, max_duration_seconds=buffer_seconds)
        checker = WhitelistChecker(whitelist=whitelist, confidence_threshold=confidence_threshold)
        self.word_pipeline = WordFilterPipeline(checker=checker, audio_buffer=self.audio_buffer)
        self.sample_bank = sample_bank
        self.sample_substitution_engine = (
            SampleSubstitutionEngine(
                sample_bank=sample_bank,
                whitelist=whitelist,
                fallback_policy=sample_fallback_policy,
                config=config,
            )
            if sample_bank
            else None
        )
        self.replacement_engine = ReplacementEngine(
            mapper=GibberishMapper(seed=seed),
            tts=tts,
            config=config,
            speaker_profile=speaker_profile,
            sample_bank=sample_bank,
        )
        self.assembler = ReplacementAssembler(config=config)
        self.output_scheduler = AudioOutputScheduler(config=config)

    def accept_frame(self, pcm: bytes, timestamp: float) -> Optional[ProcessedSegment]:
        frame = AudioFrame(pcm=pcm, timestamp=timestamp, config=self.config)
        self.audio_buffer.append(frame)
        segment = self.segmenter.process(frame)
        if segment is None:
            return None
        return self._process_segment(segment)

    def flush(self) -> Optional[ProcessedSegment]:
        segment = self.segmenter.flush()
        if segment is None:
            return None
        return self._process_segment(segment)

    def _process_segment(self, segment: SpeechSegment) -> ProcessedSegment:
        _console_log(
            "SEGMENT START",
            [
                f"speech_start={segment.start_timestamp:.3f}s",
                f"speech_end={segment.end_timestamp:.3f}s",
                f"duration={segment.end_timestamp - segment.start_timestamp:.3f}s",
                f"frames={segment.frame_count}",
                f"pcm_bytes={len(segment.pcm)}",
                f"whitelist={list(self.whitelist)!r}",
                f"confidence_threshold={self.confidence_threshold}",
                f"buffer_seconds={self.buffer_seconds}",
                f"seed={self.seed!r}",
            ],
        )
        transcript = self.asr.transcribe(segment.pcm, self.config.sample_rate)
        _console_log(
            "ASR TRANSCRIPT",
            [
                f"text={transcript.text!r}",
                "words:",
                *(
                    f"  [{index}] word={word.word!r} start={word.start:.3f}s end={word.end:.3f}s confidence={word.confidence:.3f}"
                    for index, word in enumerate(transcript.words)
                ),
            ],
        )
        if self.sample_substitution_engine is not None:
            return self._process_segment_with_sample_bank(segment, transcript)

        filtered_words = self.word_pipeline.process(segment, transcript)
        _console_log(
            "WHITELIST DECISIONS",
            [
                *(
                    f"  [{index}] original={item.decision.original.word!r} normalized={item.decision.normalized_word!r} "
                    f"allowed={item.decision.allowed} reason={item.decision.reason!r} "
                    f"start={item.decision.original.start:.3f}s end={item.decision.original.end:.3f}s "
                    f"confidence={item.decision.original.confidence:.3f} source_pcm_bytes={len(item.audio.pcm)}"
                    for index, item in enumerate(filtered_words)
                ),
            ]
            or ["  no words returned by ASR"],
        )
        replacements = tuple(self.replacement_engine.replace(word) for word in filtered_words)
        _console_log(
            "GIBBERISH OUTPUT",
            [
                *(
                    _replacement_log_line(index, replacement)
                    for index, replacement in enumerate(replacements)
                ),
            ]
            or ["  no replacements generated"],
        )
        output_pcm = self.assembler.assemble(
            replacements,
            start=segment.start_timestamp,
            end=segment.end_timestamp,
        ).pcm
        _console_log(
            "SEGMENT END",
            [
                f"input_seconds={segment.end_timestamp - segment.start_timestamp:.3f}",
                f"output_seconds={_pcm_duration_seconds(output_pcm, self.config):.3f}",
                f"output_to_input_ratio={_safe_ratio(_pcm_duration_seconds(output_pcm, self.config), segment.end_timestamp - segment.start_timestamp):.3f}",
                f"output_pcm_bytes={len(output_pcm)}",
                f"replacement_count={len(replacements)}",
            ],
        )
        return ProcessedSegment(
            speech=segment,
            transcript=transcript,
            filtered_words=filtered_words,
            replacements=replacements,
            output_pcm=output_pcm,
        )

    def _process_segment_with_sample_bank(self, segment: SpeechSegment, transcript: Transcript) -> ProcessedSegment:
        word_events = transcript_to_final_word_events(segment, transcript)
        _console_log(
            "WORD EVENTS",
            [
                *(
                    f"  [{index}] raw={event.raw_text!r} normalized={event.normalized_text!r} "
                    f"start_ms={event.start_ms} end_ms={event.end_ms} confidence={event.confidence:.3f} "
                    f"is_final={event.is_final}"
                    for index, event in enumerate(word_events)
                ),
            ]
            or ["  no finalized word events"],
        )
        substitutions = []
        output_frames = []
        output_cursor = segment.start_timestamp
        if not word_events:
            self.output_scheduler.enqueue_silence(segment.end_timestamp - segment.start_timestamp)
        for event in word_events:
            self.output_scheduler.enqueue_silence(event.start_seconds - output_cursor)
            substitution = self.sample_substitution_engine.substitute(event)
            substitutions.append(substitution)
            latency_ms = max(0.0, segment.end_timestamp * 1000.0 - event.end_ms)
            self.metrics.asr_to_output_latency_ms += latency_ms
            logger.info(
                "ASR-to-output latency: word=%r latency_ms=%.3f",
                event.normalized_text,
                latency_ms,
            )
            self.output_scheduler.enqueue_clip(substitution.replacement.output_pcm)
            output_cursor = max(event.end_seconds, output_cursor + _pcm_duration_seconds(substitution.replacement.output_pcm, self.config))
            output_frames.extend(self.output_scheduler.emit_ready_frames())
        self.output_scheduler.enqueue_silence(segment.end_timestamp - output_cursor)
        substitutions = tuple(substitutions)
        filtered_words = tuple(item.filtered_word for item in substitutions)
        replacements = tuple(item.replacement for item in substitutions)
        _console_log(
            "SAMPLE SUBSTITUTION OUTPUT",
            [
                *(_replacement_log_line(index, replacement) for index, replacement in enumerate(replacements)),
            ]
            or ["  no samples enqueued"],
        )
        output_frames.extend(self.output_scheduler.emit_available())
        if not output_frames and word_events:
            output_frames = (self.output_scheduler.emit_frame(),)
        output_pcm = b"".join(output_frames)
        self._copy_sample_mode_metrics()
        _console_log(
            "SEGMENT END",
            [
                f"input_seconds={segment.end_timestamp - segment.start_timestamp:.3f}",
                f"output_seconds={_pcm_duration_seconds(output_pcm, self.config):.3f}",
                f"output_to_input_ratio={_safe_ratio(_pcm_duration_seconds(output_pcm, self.config), segment.end_timestamp - segment.start_timestamp):.3f}",
                f"output_pcm_bytes={len(output_pcm)}",
                f"replacement_count={len(replacements)}",
                f"scheduler_frames={len(output_frames)}",
                f"scheduler_underruns={self.output_scheduler.metrics.underruns}",
                f"sample_lookup_ms={self.metrics.sample_lookup_ms:.3f}",
                f"asr_to_output_latency_ms={self.metrics.asr_to_output_latency_ms:.3f}",
                f"missing_sample_fallbacks={self.metrics.missing_sample_fallbacks}",
            ],
        )
        return ProcessedSegment(
            speech=segment,
            transcript=transcript,
            filtered_words=filtered_words,
            replacements=replacements,
            output_pcm=output_pcm,
        )

    def _copy_sample_mode_metrics(self) -> None:
        if self.sample_substitution_engine is None:
            return
        self.metrics.sample_lookup_ms = self.sample_substitution_engine.metrics.total_lookup_ms
        self.metrics.missing_sample_fallbacks = self.sample_substitution_engine.metrics.missing_sample_fallbacks
        self.metrics.scheduler_underruns = self.output_scheduler.metrics.underruns


def _replacement_log_line(index: int, replacement: ReplacementSegment) -> str:
    decision = replacement.source.decision
    if replacement.is_original_audio:
        action = "SAMPLE" if decision.reason.startswith("sample-bank") else "KEEP"
        return (
            f"  [{index}] {action} original={decision.original.word!r} normalized={decision.normalized_word!r} "
            f"reason={decision.reason!r} pcm_bytes={len(replacement.output_pcm)}"
        )
    gibberish = replacement.gibberish
    gibberish_text = gibberish.text if gibberish else None
    syllables = gibberish.syllable_count if gibberish else None
    return (
        f"  [{index}] REPLACE original={decision.original.word!r} normalized={decision.normalized_word!r} "
        f"reason={decision.reason!r} gibberish={gibberish_text!r} syllables={syllables} "
        f"tts_pcm_bytes={len(replacement.output_pcm)} error={replacement.error!r}"
    )


def _pcm_duration_seconds(pcm: bytes, config: AudioConfig) -> float:
    if not pcm:
        return 0.0
    samples = len(pcm) / (config.channels * config.sample_width_bytes)
    return samples / config.sample_rate


def _safe_ratio(numerator: float, denominator: float) -> float:
    if denominator <= 0.0:
        return 0.0
    return numerator / denominator


def _console_log(title: str, lines: list[str]) -> None:
    message = "\n".join(["", f"========== LIVE GIBBERISH: {title} ==========", *lines, "=" * 58])
    logger.info(message)
    print(message, flush=True)
