import tempfile
import unittest
from pathlib import Path

from live_gibberish.audio_io import AudioConfig, WavSink
from live_gibberish.speaker import SpeakerEnrollment


class SpeakerTests(unittest.TestCase):
    def test_enrollment_profile_validates_duration_and_format(self):
        config = AudioConfig()
        with tempfile.TemporaryDirectory() as temp_dir:
            path = Path(temp_dir) / "enroll.wav"
            sink = WavSink(path, config=config)
            sink.write(b"\x00\x00" * config.sample_rate * 5)
            sink.close()

            profile = SpeakerEnrollment(config=config, min_seconds=5.0).from_wav(path, voice_id="voice-a")

        self.assertEqual(profile.voice_id, "voice-a")
        self.assertFalse(profile.has_embedding)
        self.assertEqual(len(profile.profile_id), 16)


if __name__ == "__main__":
    unittest.main()

