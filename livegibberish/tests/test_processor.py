import unittest

from live_gibberish.asr import Transcript, WordResult
from live_gibberish.audio_io import AudioConfig
from live_gibberish.processor import LiveGibberishProcessor
from live_gibberish.tts import SynthesizedSpeech
from live_gibberish.vad import EnergyVad


class StaticAsr:
    def transcribe(self, pcm16, sample_rate):
        return Transcript(
            text="hello danger",
            words=(
                WordResult("hello", 0.0, 0.02, 1.0),
                WordResult("danger", 0.02, 0.04, 1.0),
            ),
        )


class StaticTts:
    def synthesize(self, text, config, voice_id=None):
        return SynthesizedSpeech(
            text=text,
            pcm=b"\x05\x00" * config.samples_per_frame * 4,
            sample_rate=config.sample_rate,
            voice_id=voice_id,
        )


class ProcessorTests(unittest.TestCase):
    def test_processor_outputs_full_segment_span(self):
        config = AudioConfig()
        processor = LiveGibberishProcessor(
            asr=StaticAsr(),
            vad=EnergyVad(threshold=0.02),
            tts=StaticTts(),
            whitelist=["hello"],
            seed="secret",
            config=config,
        )

        frames = [
            (b"\x00\x00" * config.samples_per_frame, 0.00),
            (int(8000).to_bytes(2, "little", signed=True) * config.samples_per_frame, 0.02),
            (int(8000).to_bytes(2, "little", signed=True) * config.samples_per_frame, 0.04),
        ]
        for pcm, timestamp in frames:
            self.assertIsNone(processor.accept_frame(pcm, timestamp))

        result = processor.flush()

        self.assertIsNotNone(result)
        self.assertEqual(len(result.filtered_words), 2)
        self.assertGreater(len(result.output_pcm), 0)
        expected_bytes = round((result.speech.end_timestamp - result.speech.start_timestamp) * config.sample_rate) * 2
        self.assertEqual(len(result.output_pcm), expected_bytes)


if __name__ == "__main__":
    unittest.main()
