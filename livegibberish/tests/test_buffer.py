import unittest

from live_gibberish.audio_io import AudioConfig, AudioFrame
from live_gibberish.buffer import TimedAudioBuffer


def frame(config: AudioConfig, sample: int, timestamp: float) -> AudioFrame:
    pcm = int(sample).to_bytes(2, byteorder="little", signed=True) * config.samples_per_frame
    return AudioFrame(pcm=pcm, timestamp=timestamp, config=config)


class BufferTests(unittest.TestCase):
    def test_extract_slices_across_frames(self):
        config = AudioConfig()
        buffer = TimedAudioBuffer(config=config)
        buffer.append(frame(config, 1000, 0.00))
        buffer.append(frame(config, 2000, 0.02))
        buffer.append(frame(config, 3000, 0.04))

        audio_slice = buffer.extract(0.01, 0.05)

        self.assertEqual(len(audio_slice.pcm), config.bytes_per_frame * 2)
        self.assertEqual(audio_slice.start, 0.01)
        self.assertEqual(audio_slice.end, 0.05)

    def test_drop_before_removes_old_frames(self):
        config = AudioConfig()
        buffer = TimedAudioBuffer(config=config, max_duration_seconds=0.04)
        buffer.append(frame(config, 1000, 0.00))
        buffer.append(frame(config, 2000, 0.02))
        buffer.append(frame(config, 3000, 0.04))

        audio_slice = buffer.extract(0.00, 0.02)

        self.assertEqual(audio_slice.pcm, b"")


if __name__ == "__main__":
    unittest.main()

