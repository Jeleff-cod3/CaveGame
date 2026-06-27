import math
import unittest

from live_gibberish.audio_io import AudioConfig
from live_gibberish.tts import create_tts_engine, match_duration, match_source_character


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

    def test_match_source_character_follows_source_loudness_and_pitch(self):
        config = AudioConfig()
        duration = 0.40
        source = sine_pcm(config, 120.0, duration, 1200)
        loud_high_pitch = sine_pcm(config, 240.0, duration, 20000)

        matched = match_source_character(loud_high_pitch, source, duration, config)

        self.assertEqual(len(matched), len(source))
        self.assertLess(pcm_rms(matched), 1800)
        self.assertGreater(pcm_rms(matched), 800)
        self.assertLess(rough_pitch_hz(matched, config), 220.0)

    def test_create_tts_rejects_unknown_backend(self):
        with self.assertRaises(ValueError):
            create_tts_engine("removed-backend")


def sine_pcm(config: AudioConfig, frequency: float, duration: float, amplitude: int) -> bytes:
    sample_count = round(config.sample_rate * duration)
    samples = bytearray()
    for index in range(sample_count):
        value = round(math.sin(index * frequency * math.tau / config.sample_rate) * amplitude)
        samples.extend(int(value).to_bytes(2, byteorder="little", signed=True))
    return bytes(samples)


def pcm_rms(pcm: bytes) -> float:
    samples = [
        int.from_bytes(pcm[index : index + 2], byteorder="little", signed=True)
        for index in range(0, len(pcm), 2)
    ]
    return math.sqrt(sum(sample * sample for sample in samples) / len(samples))


def rough_pitch_hz(pcm: bytes, config: AudioConfig) -> float:
    samples = [
        int.from_bytes(pcm[index : index + 2], byteorder="little", signed=True)
        for index in range(0, len(pcm), 2)
    ]
    crossings = 0
    for previous, current in zip(samples, samples[1:]):
        if previous <= 0 < current:
            crossings += 1
    return crossings / (len(samples) / config.sample_rate)


if __name__ == "__main__":
    unittest.main()
