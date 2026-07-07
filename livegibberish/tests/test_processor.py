import unittest

from live_gibberish.asr import Transcript, WordResult
from live_gibberish.audio_bank import AudioBankClip, SampleBank
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


class ThreeWordAsr:
    def transcribe(self, pcm16, sample_rate):
        return Transcript(
            text="hello badword yes",
            words=(
                WordResult("hello", 0.00, 0.20, 1.0),
                WordResult("badword", 0.20, 0.50, 1.0),
                WordResult("yes", 0.50, 0.70, 1.0),
            ),
        )


class SpacedThreeWordAsr:
    def transcribe(self, pcm16, sample_rate):
        return Transcript(
            text="hello badword yes",
            words=(
                WordResult("hello", 0.00, 0.20, 1.0),
                WordResult("badword", 0.40, 0.70, 1.0),
                WordResult("yes", 0.90, 1.10, 1.0),
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


class ExplodingTts:
    def synthesize(self, text, config, voice_id=None):
        raise AssertionError("TTS must not be called in sample-bank mode")


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

    def test_sample_bank_mode_outputs_only_prerecorded_samples(self):
        config = AudioConfig()
        live_sample = int(8000).to_bytes(2, "little", signed=True)
        bank_sample = int(1200).to_bytes(2, "little", signed=True)
        gibberish_sample = int(2400).to_bytes(2, "little", signed=True)
        sample_bank = SampleBank(
            user_id="processor-bank",
            whitelist={
                "hello": AudioBankClip("hello", bank_sample * config.samples_per_frame * 2, 0.04),
            },
            gibberish={
                "short": (AudioBankClip("short", gibberish_sample * config.samples_per_frame * 2, 0.04),),
                "medium": (AudioBankClip("medium", gibberish_sample * config.samples_per_frame * 30, 0.60),),
                "long": (AudioBankClip("long", gibberish_sample * config.samples_per_frame * 60, 1.20),),
            },
            config=config,
        )
        processor = LiveGibberishProcessor(
            asr=StaticAsr(),
            vad=EnergyVad(threshold=0.02),
            tts=ExplodingTts(),
            whitelist=["hello"],
            seed="secret",
            config=config,
            sample_bank=sample_bank,
        )

        frames = [
            (b"\x00\x00" * config.samples_per_frame, 0.00),
            (live_sample * config.samples_per_frame, 0.02),
            (live_sample * config.samples_per_frame, 0.04),
        ]
        for pcm, timestamp in frames:
            self.assertIsNone(processor.accept_frame(pcm, timestamp))

        result = processor.flush()

        self.assertIsNotNone(result)
        self.assertGreater(len(result.output_pcm), 0)
        self.assertNotIn(live_sample * 8, result.output_pcm)
        self.assertNotEqual(set(result.output_pcm), {0})
        self.assertEqual(len(result.replacements), 2)
        self.assertTrue(result.replacements[0].is_original_audio)
        self.assertFalse(result.replacements[1].is_original_audio)

    def test_prerecorded_mode_full_session_outputs_word_gibberish_word(self):
        config = AudioConfig()
        live_sample = int(8000).to_bytes(2, "little", signed=True)
        hello_sample = int(1200).to_bytes(2, "little", signed=True)
        yes_sample = int(1800).to_bytes(2, "little", signed=True)
        gibberish_sample = int(2400).to_bytes(2, "little", signed=True)
        sample_bank = SampleBank(
            user_id="processor-bank",
            whitelist={
                "hello": AudioBankClip("hello", hello_sample * config.samples_per_frame * 20, 0.40),
                "yes": AudioBankClip("yes", yes_sample * config.samples_per_frame * 20, 0.40),
            },
            gibberish={
                "short": (AudioBankClip("short", gibberish_sample * config.samples_per_frame * 10, 0.20),),
                "medium": (AudioBankClip("medium", gibberish_sample * config.samples_per_frame * 30, 0.60),),
                "long": (AudioBankClip("long", gibberish_sample * config.samples_per_frame * 60, 1.20),),
            },
            config=config,
        )
        processor = LiveGibberishProcessor(
            asr=ThreeWordAsr(),
            vad=EnergyVad(threshold=0.02),
            tts=ExplodingTts(),
            whitelist=["hello", "yes"],
            seed="secret",
            config=config,
            sample_bank=sample_bank,
        )

        frames = [
            (live_sample * config.samples_per_frame, 0.00),
            (live_sample * config.samples_per_frame, 0.02),
            (live_sample * config.samples_per_frame, 0.04),
            (live_sample * config.samples_per_frame, 0.06),
        ]
        for pcm, timestamp in frames:
            self.assertIsNone(processor.accept_frame(pcm, timestamp))

        result = processor.flush()

        self.assertIsNotNone(result)
        self.assertEqual([item.decision.normalized_word for item in result.filtered_words], ["hello", "badword", "yes"])
        self.assertTrue(result.replacements[0].is_original_audio)
        self.assertFalse(result.replacements[1].is_original_audio)
        self.assertTrue(result.replacements[2].is_original_audio)
        self.assertIn(hello_sample * 8, result.output_pcm)
        self.assertIn(gibberish_sample * 8, result.output_pcm)
        self.assertIn(yes_sample * 8, result.output_pcm)
        self.assertNotIn(live_sample * 8, result.output_pcm)
        self.assertEqual(result.output_pcm, b"".join(_frames(result.output_pcm, config.bytes_per_frame)))

    def test_prerecorded_mode_keeps_silence_between_timed_words(self):
        config = AudioConfig()
        live_sample = int(8000).to_bytes(2, "little", signed=True)
        hello_sample = int(1200).to_bytes(2, "little", signed=True)
        yes_sample = int(1800).to_bytes(2, "little", signed=True)
        gibberish_sample = int(2400).to_bytes(2, "little", signed=True)
        sample_bank = SampleBank(
            user_id="processor-bank",
            whitelist={
                "hello": AudioBankClip("hello", hello_sample * config.samples_per_frame * 10, 0.20),
                "yes": AudioBankClip("yes", yes_sample * config.samples_per_frame * 10, 0.20),
            },
            gibberish={
                "short": (AudioBankClip("short", gibberish_sample * config.samples_per_frame * 10, 0.20),),
                "medium": (AudioBankClip("medium", gibberish_sample * config.samples_per_frame * 10, 0.20),),
                "long": (AudioBankClip("long", gibberish_sample * config.samples_per_frame * 60, 1.20),),
            },
            config=config,
        )
        processor = LiveGibberishProcessor(
            asr=SpacedThreeWordAsr(),
            vad=EnergyVad(threshold=0.02),
            tts=ExplodingTts(),
            whitelist=["hello", "yes"],
            seed="secret",
            config=config,
            sample_bank=sample_bank,
        )

        for index in range(60):
            self.assertIsNone(processor.accept_frame(live_sample * config.samples_per_frame, index * config.frame_seconds))

        result = processor.flush()

        self.assertIsNotNone(result)
        first_gap = _pcm_slice(result.output_pcm, 0.22, 0.38, config)
        second_gap = _pcm_slice(result.output_pcm, 0.72, 0.88, config)
        gibberish_span = _pcm_slice(result.output_pcm, 0.40, 0.70, config)
        self.assertEqual(set(first_gap), {0})
        self.assertEqual(set(second_gap), {0})
        self.assertIn(gibberish_sample * 8, gibberish_span)
        self.assertIn(hello_sample * 8, result.output_pcm)
        self.assertIn(yes_sample * 8, result.output_pcm)


def _frames(pcm: bytes, frame_size: int):
    return [pcm[index : index + frame_size] for index in range(0, len(pcm), frame_size)]


def _pcm_slice(pcm: bytes, start_seconds: float, end_seconds: float, config: AudioConfig) -> bytes:
    start = round(start_seconds * config.sample_rate) * config.sample_width_bytes
    end = round(end_seconds * config.sample_rate) * config.sample_width_bytes
    return pcm[start:end]


if __name__ == "__main__":
    unittest.main()
