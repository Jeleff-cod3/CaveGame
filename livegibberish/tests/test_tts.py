import unittest

from live_gibberish.audio_io import AudioConfig
from live_gibberish.tts import create_tts_engine, match_duration


class TtsTests(unittest.TestCase):
    def test_match_duration_trims_or_pads_to_target_length(self):
        config = AudioConfig()
        target_seconds = 0.04
        short = b"\x01\x00" * 10
        long = b"\x01\x00" * config.samples_per_frame * 5

        padded = match_duration(short, target_seconds, config)
        trimmed = match_duration(long, target_seconds, config)

        self.assertEqual(len(padded), config.bytes_per_frame * 2)
        self.assertEqual(len(trimmed), config.bytes_per_frame * 2)

    def test_create_tts_rejects_unknown_backend(self):
        with self.assertRaises(ValueError):
            create_tts_engine("removed-backend")


if __name__ == "__main__":
    unittest.main()
