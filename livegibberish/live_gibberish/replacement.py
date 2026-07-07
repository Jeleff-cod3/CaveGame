from __future__ import annotations

import logging
from dataclasses import dataclass
from typing import Optional

from .audio_bank import SampleBank
from .audio_io import AudioConfig
from .gibberish import GibberishMapper, GibberishToken
from .pipeline import FilteredWordSegment
from .speaker import SpeakerProfile
from .tts import SynthesizedSpeech, TtsEngine, match_duration, match_source_character


logger = logging.getLogger(__name__)


@dataclass(frozen=True)
class ReplacementSegment:
    source: FilteredWordSegment
    output_pcm: bytes
    is_original_audio: bool
    gibberish: Optional[GibberishToken] = None
    synthesized: Optional[SynthesizedSpeech] = None
    error: Optional[str] = None


@dataclass(frozen=True)
class AssembledAudio:
    pcm: bytes
    start: float
    end: float


class ReplacementEngine:
    def __init__(
        self,
        mapper: GibberishMapper,
        tts: Optional[TtsEngine],
        config: AudioConfig = AudioConfig(),
        speaker_profile: Optional[SpeakerProfile] = None,
        sample_bank: Optional[SampleBank] = None,
    ) -> None:
        self.mapper = mapper
        self.tts = tts
        self.config = config
        self.speaker_profile = speaker_profile
        self.sample_bank = sample_bank

    def replace(self, word_segment: FilteredWordSegment) -> ReplacementSegment:
        if not word_segment.needs_replacement:
            if self.sample_bank and self.sample_bank.has_whitelist_word(word_segment.decision.normalized_word):
                target_duration = word_segment.audio.end - word_segment.audio.start
                output_pcm = self.sample_bank.render_whitelist_word(
                    word_segment.decision.normalized_word,
                    target_duration,
                )
                return ReplacementSegment(
                    source=word_segment,
                    output_pcm=output_pcm,
                    is_original_audio=True,
                )
            return ReplacementSegment(
                source=word_segment,
                output_pcm=word_segment.audio.pcm,
                is_original_audio=True,
            )

        source_word = word_segment.decision.normalized_word or word_segment.decision.original.word
        gibberish = self.mapper.map_word(source_word)
        _console_log(
            "GIBBERISH GENERATED",
            [
                f"original={word_segment.decision.original.word!r}",
                f"normalized={word_segment.decision.normalized_word!r}",
                f"reason={word_segment.decision.reason!r}",
                f"gibberish={gibberish.text!r}",
                f"syllables={gibberish.syllable_count}",
            ],
        )
        target_duration = word_segment.audio.end - word_segment.audio.start
        if self.sample_bank:
            output_pcm = self.sample_bank.render_gibberish(
                gibberish.text,
                target_duration,
            )
            return ReplacementSegment(
                source=word_segment,
                output_pcm=output_pcm,
                is_original_audio=False,
                gibberish=gibberish,
            )

        if self.tts is None:
            raise ValueError("ReplacementEngine needs either a sample bank or a TTS engine.")

        voice_id = self.speaker_profile.voice_id if self.speaker_profile else None
        synthesized = self.tts.synthesize(gibberish.text, self.config, voice_id=voice_id)
        output_pcm = match_source_character(
            synthesized.pcm,
            word_segment.audio.pcm,
            target_duration,
            self.config,
        )
        return ReplacementSegment(
            source=word_segment,
            output_pcm=output_pcm,
            is_original_audio=False,
            gibberish=gibberish,
            synthesized=synthesized,
        )


class ReplacementAssembler:
    def __init__(self, config: AudioConfig = AudioConfig()) -> None:
        self.config = config

    def assemble(
        self,
        replacements: tuple[ReplacementSegment, ...],
        start: float,
        end: float,
    ) -> AssembledAudio:
        if end <= start:
            return AssembledAudio(pcm=b"", start=start, end=end)

        byte_count = self._duration_to_bytes(end - start)
        output = bytearray(byte_count)
        occupied = [False] * (byte_count // self._bytes_per_sample)
        ordered = sorted(replacements, key=lambda item: item.source.audio.start)

        for replacement in (item for item in ordered if item.is_original_audio):
            self._write_replacement(output, occupied, replacement, start, end, protect_existing=False)
        for replacement in (item for item in ordered if not item.is_original_audio):
            self._write_replacement(output, occupied, replacement, start, end, protect_existing=True)

        return AssembledAudio(pcm=bytes(output), start=start, end=end)

    def _silence(self, duration_seconds: float) -> bytes:
        return b"\x00" * max(0, self._duration_to_bytes(duration_seconds))

    def _write_replacement(
        self,
        output: bytearray,
        occupied: list[bool],
        replacement: ReplacementSegment,
        start: float,
        end: float,
        protect_existing: bool,
    ) -> None:
        source = replacement.source.audio
        clip_start = max(source.start, start)
        clip_end = min(source.end, end)
        if clip_end <= clip_start:
            return

        source_duration = source.end - source.start
        expected_bytes = self._duration_to_bytes(source_duration)
        source_pcm = replacement.output_pcm
        if len(source_pcm) != expected_bytes:
            source_pcm = match_duration(source_pcm, source_duration, self.config)

        source_offset = clip_start - source.start
        clip = self._slice_pcm(source_pcm, source_offset, clip_end - clip_start)
        destination_sample = self._seconds_to_sample_offset(clip_start - start)
        self._copy_samples(output, occupied, clip, destination_sample, protect_existing)

    def _slice_pcm(self, pcm: bytes, start_seconds: float, duration_seconds: float) -> bytes:
        byte_start = self._duration_to_bytes(start_seconds)
        byte_count = self._duration_to_bytes(duration_seconds)
        return pcm[byte_start : byte_start + byte_count]

    def _copy_samples(
        self,
        output: bytearray,
        occupied: list[bool],
        pcm: bytes,
        destination_sample: int,
        protect_existing: bool,
    ) -> None:
        if destination_sample >= len(occupied):
            return

        sample_width = self._bytes_per_sample
        sample_count = min(len(pcm) // sample_width, len(occupied) - destination_sample)
        for sample_index in range(sample_count):
            output_sample = destination_sample + sample_index
            if protect_existing and occupied[output_sample]:
                continue

            source_byte = sample_index * sample_width
            output_byte = output_sample * sample_width
            output[output_byte : output_byte + sample_width] = pcm[source_byte : source_byte + sample_width]
            occupied[output_sample] = True

    @property
    def _bytes_per_sample(self) -> int:
        return self.config.channels * self.config.sample_width_bytes

    def _duration_to_bytes(self, duration_seconds: float) -> int:
        return self._seconds_to_sample_offset(duration_seconds) * self._bytes_per_sample

    def _seconds_to_sample_offset(self, seconds: float) -> int:
        return max(0, round(seconds * self.config.sample_rate))


def _console_log(title: str, lines: list[str]) -> None:
    message = "\n".join(["", f"========== LIVE GIBBERISH: {title} ==========", *lines, "=" * 58])
    logger.info(message)
    print(message, flush=True)
