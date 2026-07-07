import math
import tempfile
import unittest
import wave
from io import BytesIO
from pathlib import Path

from live_gibberish.audio_bank import (
    AudioBankValidationError,
    SampleBank,
    duration_bucket,
    save_gibberish_recording,
    save_whitelist_recording,
    validate_audio_bank,
)
from live_gibberish.audio_io import AudioConfig


def wav_bytes(sample_rate=8000, amplitude=8000, seconds=0.35):
    sample_count = max(1, round(sample_rate * seconds))
    samples = []
    for index in range(sample_count):
        value = round(math.sin(index / sample_rate * 440.0 * math.tau) * amplitude)
        samples.append(int(value).to_bytes(2, "little", signed=True))
    silence = b"\x00\x00" * round(sample_rate * 0.05)
    payload = silence + b"".join(samples) + silence
    output = BytesIO()
    with wave.open(output, "wb") as wav:
        wav.setnchannels(1)
        wav.setsampwidth(2)
        wav.setframerate(sample_rate)
        wav.writeframes(payload)
    return output.getvalue()


class AudioBankTests(unittest.TestCase):
    def test_recordings_are_preprocessed_validated_and_preloaded(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            user_id = "player-one"

            save_whitelist_recording(user_id, "Hello!", wav_bytes(), root=root)
            save_gibberish_recording(user_id, "short", wav_bytes(seconds=0.20), name="a", root=root)
            save_gibberish_recording(user_id, "medium", wav_bytes(seconds=0.55), name="b", root=root)
            save_gibberish_recording(user_id, "long", wav_bytes(seconds=1.00), name="c", root=root)

            report = validate_audio_bank(user_id, required_whitelist=["hello"], root=root)
            self.assertTrue(report.ok, report.to_dict())

            bank = SampleBank.load(user_id, required_whitelist=["hello"], root=root)
            self.assertIn("hello", bank.whitelist)
            self.assertGreater(len(bank.whitelist["hello"].pcm), 0)

            for path in root.glob("users/player-one/samples/**/*.wav"):
                path.unlink()

            self.assertGreater(len(bank.render_whitelist_word("hello", 0.20)), 0)
            self.assertGreater(len(bank.render_gibberish("gib", 0.20)), 0)

    def test_validation_reports_missing_required_whitelist_words(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            user_id = "player-two"
            save_gibberish_recording(user_id, "short", wav_bytes(seconds=0.20), name="a", root=root)
            save_gibberish_recording(user_id, "medium", wav_bytes(seconds=0.55), name="b", root=root)
            save_gibberish_recording(user_id, "long", wav_bytes(seconds=1.00), name="c", root=root)

            report = validate_audio_bank(user_id, required_whitelist=["cave"], root=root)

            self.assertFalse(report.ok)
            self.assertIn("missing-whitelist-word", [issue.code for issue in report.issues])
            with self.assertRaises(AudioBankValidationError):
                SampleBank.load(user_id, required_whitelist=["cave"], root=root)

    def test_clipped_recording_is_rejected_before_manifest_write(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            with self.assertRaises(ValueError):
                save_whitelist_recording("player-three", "hello", wav_bytes(amplitude=32767), root=Path(temp_dir))

    def test_duration_buckets_match_live_word_rules(self):
        self.assertEqual(duration_bucket(0.299), "short")
        self.assertEqual(duration_bucket(0.300), "medium")
        self.assertEqual(duration_bucket(0.800), "long")


if __name__ == "__main__":
    unittest.main()
