import unittest

from live_gibberish.asr import WordResult
from live_gibberish.audio_io import AudioConfig
from live_gibberish.buffer import AudioSlice
from live_gibberish.filtering import WordDecision
from live_gibberish.gibberish import GibberishMapper
from live_gibberish.pipeline import FilteredWordSegment
from live_gibberish.replacement import ReplacementAssembler, ReplacementEngine
from live_gibberish.tts import SynthesizedSpeech


class FailingTtsEngine:
    def synthesize(self, text, config, voice_id=None):
        raise RuntimeError("tts unavailable")


class StaticTtsEngine:
    def synthesize(self, text, config, voice_id=None):
        pcm = b"\x04\x00" * config.samples_per_frame * 4
        return SynthesizedSpeech(text=text, pcm=pcm, sample_rate=config.sample_rate, voice_id=voice_id)


class ReplacementTests(unittest.TestCase):
    def test_allowed_words_keep_original_pcm(self):
        config = AudioConfig()
        pcm = b"\x02\x00" * config.samples_per_frame
        segment = FilteredWordSegment(
            decision=WordDecision(WordResult("hello", 0.0, 0.02, 1.0), "hello", True, "whitelist"),
            audio=AudioSlice(pcm=pcm, start=0.0, end=0.02),
        )
        engine = ReplacementEngine(GibberishMapper(), StaticTtsEngine(), config=config)

        replacement = engine.replace(segment)

        self.assertTrue(replacement.is_original_audio)
        self.assertEqual(replacement.output_pcm, pcm)
        self.assertIsNone(replacement.gibberish)

    def test_blocked_words_are_synthesized_to_source_duration(self):
        config = AudioConfig()
        segment = FilteredWordSegment(
            decision=WordDecision(WordResult("danger", 0.0, 0.04, 1.0), "danger", False, "not-whitelisted"),
            audio=AudioSlice(pcm=b"\x03\x00" * config.samples_per_frame * 2, start=0.0, end=0.04),
        )
        engine = ReplacementEngine(GibberishMapper(seed="secret"), StaticTtsEngine(), config=config)

        replacement = engine.replace(segment)

        self.assertFalse(replacement.is_original_audio)
        self.assertIsNotNone(replacement.gibberish)
        self.assertNotEqual(replacement.gibberish.text, "danger")
        self.assertEqual(len(replacement.output_pcm), config.bytes_per_frame * 2)

    def test_blocked_words_raise_when_tts_fails(self):
        config = AudioConfig()
        segment = FilteredWordSegment(
            decision=WordDecision(WordResult("danger", 0.0, 0.02, 1.0), "danger", False, "not-whitelisted"),
            audio=AudioSlice(pcm=b"\x03\x00" * config.samples_per_frame, start=0.0, end=0.02),
        )
        engine = ReplacementEngine(GibberishMapper(seed="secret"), FailingTtsEngine(), config=config)

        with self.assertRaises(RuntimeError):
            engine.replace(segment)

    def test_assembler_preserves_segment_duration_with_silence_gaps(self):
        config = AudioConfig()
        first = FilteredWordSegment(
            decision=WordDecision(WordResult("hello", 0.02, 0.04, 1.0), "hello", True, "whitelist"),
            audio=AudioSlice(pcm=b"\x01\x00" * config.samples_per_frame, start=0.02, end=0.04),
        )
        second = FilteredWordSegment(
            decision=WordDecision(WordResult("danger", 0.06, 0.08, 1.0), "danger", False, "not-whitelisted"),
            audio=AudioSlice(pcm=b"\x02\x00" * config.samples_per_frame, start=0.06, end=0.08),
        )
        engine = ReplacementEngine(GibberishMapper(), StaticTtsEngine(), config=config)
        replacements = (engine.replace(first), engine.replace(second))

        assembled = ReplacementAssembler(config=config).assemble(replacements, start=0.0, end=0.10)

        self.assertEqual(len(assembled.pcm), config.bytes_per_frame * 5)


if __name__ == "__main__":
    unittest.main()
