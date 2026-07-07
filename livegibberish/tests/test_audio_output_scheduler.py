import unittest

from live_gibberish.audio_io import AudioConfig
from live_gibberish.audio_output_scheduler import AudioOutputScheduler


class AudioOutputSchedulerTests(unittest.TestCase):
    def test_emit_frame_inserts_silence_on_underrun(self):
        config = AudioConfig()
        scheduler = AudioOutputScheduler(config=config)

        frame = scheduler.emit_frame()

        self.assertEqual(len(frame), config.bytes_per_frame)
        self.assertEqual(set(frame), {0})
        self.assertEqual(scheduler.metrics.underruns, 1)

    def test_emits_fixed_size_frames_from_uneven_clip_lengths(self):
        config = AudioConfig()
        scheduler = AudioOutputScheduler(config=config)
        clip = b"\x11\x00" * (config.samples_per_frame + config.samples_per_frame // 2)

        scheduler.enqueue_clip(clip)
        frames = scheduler.emit_available()

        self.assertEqual(len(frames), 2)
        self.assertTrue(all(len(frame) == config.bytes_per_frame for frame in frames))
        self.assertEqual(scheduler.metrics.frames_emitted, 2)
        self.assertEqual(scheduler.metrics.underruns, 1)

    def test_queue_streams_continuously_when_clips_arrive_between_frames(self):
        config = AudioConfig()
        scheduler = AudioOutputScheduler(config=config)
        first = b"\x11\x00" * config.samples_per_frame
        second = b"\x22\x00" * config.samples_per_frame

        scheduler.enqueue_clip(first)
        first_frame = scheduler.emit_frame()
        scheduler.enqueue_clip(second)
        second_frame = scheduler.emit_frame()

        self.assertEqual(len(first_frame), config.bytes_per_frame)
        self.assertEqual(len(second_frame), config.bytes_per_frame)
        self.assertNotEqual(set(first_frame), {0})
        self.assertNotEqual(set(second_frame), {0})

    def test_ready_frames_emit_without_padding_partial_tail(self):
        config = AudioConfig()
        scheduler = AudioOutputScheduler(config=config)
        clip = b"\x11\x00" * (config.samples_per_frame + config.samples_per_frame // 2)

        scheduler.enqueue_clip(clip)
        ready = scheduler.emit_ready_frames()

        self.assertEqual(len(ready), 1)
        self.assertEqual(len(ready[0]), config.bytes_per_frame)
        self.assertEqual(scheduler.metrics.underruns, 0)
        self.assertGreater(scheduler.metrics.queued_bytes, 0)

    def test_adjacent_clips_do_not_overlap_or_shorten_each_other(self):
        config = AudioConfig()
        scheduler = AudioOutputScheduler(config=config)
        first = b"\x11\x00" * config.samples_per_frame
        second = b"\x22\x00" * config.samples_per_frame

        scheduler.enqueue_clip(first)
        scheduler.enqueue_clip(second)
        frames = scheduler.emit_available()

        self.assertEqual(len(frames), 2)
        self.assertEqual(scheduler.metrics.underruns, 0)

    def test_explicit_silence_is_emitted_between_clips(self):
        config = AudioConfig()
        scheduler = AudioOutputScheduler(config=config)

        scheduler.enqueue_clip(b"\x11\x00" * config.samples_per_frame)
        scheduler.enqueue_silence(config.frame_seconds)
        scheduler.enqueue_clip(b"\x22\x00" * config.samples_per_frame)
        frames = scheduler.emit_available()

        self.assertEqual(len(frames), 3)
        self.assertEqual(set(frames[1]), {0})


if __name__ == "__main__":
    unittest.main()
