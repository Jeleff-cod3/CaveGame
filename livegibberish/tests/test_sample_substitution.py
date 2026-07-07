import unittest

from live_gibberish.audio_bank import AudioBankClip, SampleBank
from live_gibberish.audio_io import AudioConfig
from live_gibberish.sample_substitution import SampleSubstitutionEngine
from live_gibberish.word_events import WordEvent


def bank(config):
    return SampleBank(
        user_id="sample-user",
        whitelist={
            "hello": AudioBankClip("hello", b"\x11\x00" * config.samples_per_frame, 0.02),
        },
        gibberish={
            "short": (AudioBankClip("short", b"\x22\x00" * config.samples_per_frame * 10, 0.20),),
            "medium": (AudioBankClip("medium", b"\x33\x00" * config.samples_per_frame * 30, 0.60),),
            "long": (AudioBankClip("long", b"\x44\x00" * config.samples_per_frame * 50, 1.00),),
        },
        config=config,
    )


class SampleSubstitutionTests(unittest.TestCase):
    def test_whitelisted_word_uses_prerecorded_sample_not_microphone_audio(self):
        config = AudioConfig()
        engine = SampleSubstitutionEngine(bank(config), whitelist=["hello"], config=config)
        event = WordEvent("hello", "hello", 0, 200, 1.0, True)

        substitution = engine.substitute(event)

        self.assertTrue(substitution.replacement.is_original_audio)
        self.assertEqual(len(substitution.replacement.output_pcm), round(0.20 * config.sample_rate) * 2)
        self.assertNotEqual(set(substitution.replacement.output_pcm), {0})

    def test_whitelisted_word_is_not_cut_shorter_than_recording(self):
        config = AudioConfig()
        engine = SampleSubstitutionEngine(bank(config), whitelist=["hello"], config=config)
        event = WordEvent("hello", "hello", 0, 5, 1.0, True)

        substitution = engine.substitute(event)

        self.assertEqual(len(substitution.replacement.output_pcm), config.bytes_per_frame)

    def test_non_whitelisted_word_uses_gibberish_bucket(self):
        config = AudioConfig()
        engine = SampleSubstitutionEngine(bank(config), whitelist=["hello"], config=config)
        event = WordEvent("danger", "danger", 0, 500, 1.0, True)

        substitution = engine.substitute(event)

        self.assertFalse(substitution.replacement.is_original_audio)
        self.assertEqual(substitution.replacement.gibberish.text, "bank-medium")
        self.assertEqual(len(substitution.replacement.output_pcm), config.sample_rate)

    def test_non_whitelisted_long_word_repeats_gibberish_instead_of_padding_silence(self):
        config = AudioConfig()
        engine = SampleSubstitutionEngine(bank(config), whitelist=["hello"], config=config)
        event = WordEvent("danger", "danger", 0, 1200, 1.0, True)

        substitution = engine.substitute(event)

        tail = substitution.replacement.output_pcm[-config.bytes_per_frame:]
        self.assertEqual(len(substitution.replacement.output_pcm), round(1.20 * config.sample_rate) * 2)
        self.assertNotEqual(set(tail), {0})

    def test_missing_whitelist_safe_uses_gibberish(self):
        config = AudioConfig()
        engine = SampleSubstitutionEngine(bank(config), whitelist=["cave"], fallback_policy="safe", config=config)
        event = WordEvent("cave", "cave", 0, 200, 1.0, True)

        substitution = engine.substitute(event)

        self.assertFalse(substitution.replacement.is_original_audio)
        self.assertEqual(substitution.replacement.gibberish.text, "bank-short")

    def test_missing_whitelist_debug_uses_silence(self):
        config = AudioConfig()
        engine = SampleSubstitutionEngine(bank(config), whitelist=["cave"], fallback_policy="debug", config=config)
        event = WordEvent("cave", "cave", 0, 200, 1.0, True)

        substitution = engine.substitute(event)

        self.assertTrue(substitution.replacement.is_original_audio)
        self.assertEqual(set(substitution.replacement.output_pcm), {0})
        self.assertEqual(substitution.replacement.error, "missing-whitelist-sample-debug-silence")

    def test_missing_whitelist_strict_fails(self):
        config = AudioConfig()
        engine = SampleSubstitutionEngine(bank(config), whitelist=["cave"], fallback_policy="strict", config=config)

        with self.assertRaises(ValueError):
            engine.substitute(WordEvent("cave", "cave", 0, 200, 1.0, True))


if __name__ == "__main__":
    unittest.main()
