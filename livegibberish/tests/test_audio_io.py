import tempfile
import unittest
import wave
from pathlib import Path

from live_gibberish.audio_io import AudioConfig, WavFrameSource, WavSink


class AudioIoTests(unittest.TestCase):
    def test_wav_sink_and_source_round_trip_frames(self):
        config = AudioConfig()
        pcm = b"\x01\x00" * config.samples_per_frame * 3

        with tempfile.TemporaryDirectory() as temp_dir:
            path = Path(temp_dir) / "roundtrip.wav"
            sink = WavSink(path, config=config)
            sink.write(pcm)
            sink.close()

            frames = list(WavFrameSource(path, config=config).frames())

        self.assertEqual(len(frames), 3)
        self.assertEqual(b"".join(frame.pcm for frame in frames), pcm)

    def test_wav_source_rejects_wrong_sample_rate(self):
        config = AudioConfig()
        with tempfile.TemporaryDirectory() as temp_dir:
            path = Path(temp_dir) / "bad-rate.wav"
            with wave.open(str(path), "wb") as wav:
                wav.setnchannels(1)
                wav.setsampwidth(2)
                wav.setframerate(8_000)
                wav.writeframes(b"\x00\x00" * 160)

            with self.assertRaises(ValueError):
                list(WavFrameSource(path, config=config).frames())


if __name__ == "__main__":
    unittest.main()

