import unittest

from live_gibberish.asr import Transcript, WordResult
from live_gibberish.audio_io import AudioConfig, AudioFrame
from live_gibberish.buffer import TimedAudioBuffer
from live_gibberish.filtering import WhitelistChecker
from live_gibberish.pipeline import WordFilterPipeline
from live_gibberish.vad import SpeechSegment


def frame(config: AudioConfig, sample: int, timestamp: float) -> AudioFrame:
    pcm = int(sample).to_bytes(2, byteorder="little", signed=True) * config.samples_per_frame
    return AudioFrame(pcm=pcm, timestamp=timestamp, config=config)


class PipelineTests(unittest.TestCase):
    def test_pipeline_offsets_words_and_extracts_audio(self):
        config = AudioConfig()
        audio_buffer = TimedAudioBuffer(config=config)
        for index, sample in enumerate([1000, 2000, 3000, 4000]):
            audio_buffer.append(frame(config, sample, index * config.frame_seconds))

        checker = WhitelistChecker(["hello"], confidence_threshold=0.7)
        pipeline = WordFilterPipeline(checker=checker, audio_buffer=audio_buffer)
        segment = SpeechSegment(pcm=b"", start_timestamp=0.02, end_timestamp=0.08, frame_count=3)
        transcript = Transcript(
            text="hello danger",
            words=(
                WordResult("hello", 0.00, 0.02, 0.9),
                WordResult("danger", 0.02, 0.04, 0.9),
            ),
        )

        results = pipeline.process(segment, transcript)

        self.assertEqual(len(results), 2)
        self.assertTrue(results[0].decision.allowed)
        self.assertFalse(results[1].decision.allowed)
        self.assertEqual(results[0].audio.start, 0.02)
        self.assertEqual(results[1].audio.end, 0.06)
        self.assertEqual(len(results[0].audio.pcm), config.bytes_per_frame)


if __name__ == "__main__":
    unittest.main()

