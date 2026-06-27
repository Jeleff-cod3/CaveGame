from __future__ import annotations

import logging
from dataclasses import dataclass
from typing import Optional

from .asr import AsrEngine, Transcript
from .audio_io import AudioConfig, AudioFrame
from .buffer import TimedAudioBuffer
from .filtering import WhitelistChecker
from .gibberish import GibberishMapper
from .pipeline import FilteredWordSegment, WordFilterPipeline
from .replacement import ReplacementAssembler, ReplacementEngine, ReplacementSegment
from .speaker import SpeakerProfile
from .tts import TtsEngine
from .vad import SpeechSegment, SpeechSegmenter, VoiceActivityDetector


logger = logging.getLogger(__name__)


@dataclass(frozen=True)
class ProcessedSegment:
    speech: SpeechSegment
    transcript: Transcript
    filtered_words: tuple[FilteredWordSegment, ...]
    replacements: tuple[ReplacementSegment, ...]
    output_pcm: bytes


class LiveGibberishProcessor:
    def __init__(
        self,
        asr: AsrEngine,
        vad: VoiceActivityDetector,
        tts: TtsEngine,
        whitelist: list[str],
        seed: str,
        config: AudioConfig = AudioConfig(),
        confidence_threshold: float = 0.70,
        buffer_seconds: float = 5.0,
        speaker_profile: Optional[SpeakerProfile] = None,
    ) -> None:
        self.config = config
        self.whitelist = tuple(whitelist)
        self.seed = seed
        self.confidence_threshold = confidence_threshold
        self.buffer_seconds = buffer_seconds
        self.asr = asr
        self.segmenter = SpeechSegmenter(vad=vad, config=config)
        self.audio_buffer = TimedAudioBuffer(config=config, max_duration_seconds=buffer_seconds)
        checker = WhitelistChecker(whitelist=whitelist, confidence_threshold=confidence_threshold)
        self.word_pipeline = WordFilterPipeline(checker=checker, audio_buffer=self.audio_buffer)
        self.replacement_engine = ReplacementEngine(
            mapper=GibberishMapper(seed=seed),
            tts=tts,
            config=config,
            speaker_profile=speaker_profile,
        )
        self.assembler = ReplacementAssembler(config=config)

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


def _replacement_log_line(index: int, replacement: ReplacementSegment) -> str:
    decision = replacement.source.decision
    if replacement.is_original_audio:
        return (
            f"  [{index}] KEEP original={decision.original.word!r} normalized={decision.normalized_word!r} "
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


def _console_log(title: str, lines: list[str]) -> None:
    message = "\n".join(["", f"========== LIVE GIBBERISH: {title} ==========", *lines, "=" * 58])
    logger.info(message)
    print(message, flush=True)
