from __future__ import annotations

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
        transcript = self.asr.transcribe(segment.pcm, self.config.sample_rate)
        filtered_words = self.word_pipeline.process(segment, transcript)
        replacements = tuple(self.replacement_engine.replace(word) for word in filtered_words)
        output_pcm = self.assembler.assemble(
            replacements,
            start=segment.start_timestamp,
            end=segment.end_timestamp,
        ).pcm
        return ProcessedSegment(
            speech=segment,
            transcript=transcript,
            filtered_words=filtered_words,
            replacements=replacements,
            output_pcm=output_pcm,
        )
