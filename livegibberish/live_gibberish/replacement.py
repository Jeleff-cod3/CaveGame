from __future__ import annotations

from dataclasses import dataclass
from typing import Optional

from .audio_io import AudioConfig
from .gibberish import GibberishMapper, GibberishToken
from .pipeline import FilteredWordSegment
from .speaker import SpeakerProfile
from .tts import SynthesizedSpeech, TtsEngine, match_duration


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
        tts: TtsEngine,
        config: AudioConfig = AudioConfig(),
        speaker_profile: Optional[SpeakerProfile] = None,
    ) -> None:
        self.mapper = mapper
        self.tts = tts
        self.config = config
        self.speaker_profile = speaker_profile

    def replace(self, word_segment: FilteredWordSegment) -> ReplacementSegment:
        if not word_segment.needs_replacement:
            return ReplacementSegment(
                source=word_segment,
                output_pcm=word_segment.audio.pcm,
                is_original_audio=True,
            )

        source_word = word_segment.decision.normalized_word or word_segment.decision.original.word
        gibberish = self.mapper.map_word(source_word)
        voice_id = self.speaker_profile.voice_id if self.speaker_profile else None
        synthesized = self.tts.synthesize(gibberish.text, self.config, voice_id=voice_id)
        target_duration = word_segment.audio.end - word_segment.audio.start
        output_pcm = match_duration(synthesized.pcm, target_duration, self.config)
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

        pieces: list[bytes] = []
        cursor = start
        for replacement in sorted(replacements, key=lambda item: item.source.audio.start):
            source = replacement.source.audio
            if source.start > cursor:
                pieces.append(self._silence(source.start - cursor))
            pieces.append(match_duration(replacement.output_pcm, source.end - source.start, self.config))
            cursor = max(cursor, source.end)

        if cursor < end:
            pieces.append(self._silence(end - cursor))

        return AssembledAudio(pcm=b"".join(pieces), start=start, end=end)

    def _silence(self, duration_seconds: float) -> bytes:
        byte_count = (
            round(duration_seconds * self.config.sample_rate)
            * self.config.channels
            * self.config.sample_width_bytes
        )
        return b"\x00" * max(0, byte_count)
