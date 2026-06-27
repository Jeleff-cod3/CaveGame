import unittest

from live_gibberish.asr import Transcript, WordResult
from live_gibberish.audio_io import AudioConfig, AudioFrame
from live_gibberish.buffer import TimedAudioBuffer
from live_gibberish.filtering import WhitelistChecker
from live_gibberish.pipeline import ALLOWED_WORD_TAIL_SECONDS, WordFilterPipeline
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
        segment_pcm = b"".join(frame(config, sample, 0.0).pcm for sample in [2000, 3000, 4000])
        segment = SpeechSegment(pcm=segment_pcm, start_timestamp=0.02, end_timestamp=0.08, frame_count=3)
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
        expected_allowed_duration = min(segment.end_timestamp, 0.04 + ALLOWED_WORD_TAIL_SECONDS) - results[0].audio.start
        expected_allowed_bytes = round(expected_allowed_duration * config.sample_rate) * config.sample_width_bytes
        self.assertEqual(len(results[0].audio.pcm), expected_allowed_bytes)
        self.assertEqual(sample_at(results[0].audio.pcm, 0), 2000)

    def test_pipeline_extracts_allowed_audio_from_full_segment_after_buffer_rollover(self):
        config = AudioConfig()
        audio_buffer = TimedAudioBuffer(config=config, max_duration_seconds=config.frame_seconds * 2)
        segment_frames = [frame(config, sample, index * config.frame_seconds) for index, sample in enumerate([1000, 2000, 3000, 4000, 5000, 6000])]
        for item in segment_frames:
            audio_buffer.append(item)

        checker = WhitelistChecker(["hello"], confidence_threshold=0.7)
        pipeline = WordFilterPipeline(checker=checker, audio_buffer=audio_buffer)
        segment = SpeechSegment(
            pcm=b"".join(item.pcm for item in segment_frames),
            start_timestamp=0.0,
            end_timestamp=config.frame_seconds * len(segment_frames),
            frame_count=len(segment_frames),
        )
        transcript = Transcript(
            text="hello",
            words=(WordResult("hello", 0.0, config.frame_seconds, 0.9),),
        )

        results = pipeline.process(segment, transcript)

        self.assertEqual(len(results), 1)
        self.assertTrue(results[0].decision.allowed)
        self.assertEqual(results[0].audio.start, 0.0)
        self.assertEqual(sample_at(results[0].audio.pcm, 0), 1000)

    def test_pipeline_preserves_leading_audio_for_allowed_word_onsets(self):
        config = AudioConfig()
        audio_buffer = TimedAudioBuffer(config=config)
        segment_frames = [
            frame(config, sample, index * config.frame_seconds)
            for index, sample in enumerate([1000, 2000, 3000, 4000])
        ]
        for item in segment_frames:
            audio_buffer.append(item)

        checker = WhitelistChecker(["cave"], confidence_threshold=0.7)
        pipeline = WordFilterPipeline(checker=checker, audio_buffer=audio_buffer)
        segment = SpeechSegment(
            pcm=b"".join(item.pcm for item in segment_frames),
            start_timestamp=0.0,
            end_timestamp=config.frame_seconds * len(segment_frames),
            frame_count=len(segment_frames),
        )
        transcript = Transcript(
            text="cave",
            words=(WordResult("cave", 0.02, 0.04, 0.9),),
        )

        results = pipeline.process(segment, transcript)

        self.assertEqual(len(results), 1)
        self.assertTrue(results[0].decision.allowed)
        self.assertEqual(results[0].audio.start, 0.0)
        self.assertEqual(sample_at(results[0].audio.pcm, 0), 1000)
        self.assertEqual(sample_at(results[0].audio.pcm, config.samples_per_frame), 2000)

def sample_at(pcm: bytes, sample_index: int) -> int:
    byte_index = sample_index * 2
    return int.from_bytes(pcm[byte_index : byte_index + 2], byteorder="little", signed=True)


if __name__ == "__main__":
    unittest.main()

