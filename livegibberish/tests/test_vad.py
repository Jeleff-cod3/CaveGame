import unittest

from live_gibberish.audio_io import AudioConfig, AudioFrame
from live_gibberish.vad import EnergyVad, SpeechSegmenter


def make_frame(config: AudioConfig, sample: int, timestamp: float) -> AudioFrame:
    pcm = int(sample).to_bytes(2, byteorder="little", signed=True) * config.samples_per_frame
    return AudioFrame(pcm=pcm, timestamp=timestamp, config=config)


class VadTests(unittest.TestCase):
    def test_energy_vad_splits_silence_and_speech(self):
        config = AudioConfig()
        vad = EnergyVad(threshold=0.02)

        silence = make_frame(config, 0, 0.0)
        speech = make_frame(config, 8_000, config.frame_seconds)

        self.assertFalse(vad.is_speech(silence).is_speech)
        self.assertTrue(vad.is_speech(speech).is_speech)

    def test_segmenter_emits_after_trailing_silence(self):
        config = AudioConfig()
        segmenter = SpeechSegmenter(
            vad=EnergyVad(threshold=0.02),
            config=config,
            min_silence_ms=40,
            speech_pad_ms=20,
        )
        frames = [
            make_frame(config, 0, 0.00),
            make_frame(config, 9_000, 0.02),
            make_frame(config, 9_000, 0.04),
            make_frame(config, 0, 0.06),
            make_frame(config, 0, 0.08),
        ]

        segments = [segment for frame in frames if (segment := segmenter.process(frame))]

        self.assertEqual(len(segments), 1)
        self.assertEqual(segments[0].frame_count, 5)
        self.assertGreater(len(segments[0].pcm), 0)


if __name__ == "__main__":
    unittest.main()

