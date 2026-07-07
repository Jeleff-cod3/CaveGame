import unittest

from live_gibberish.asr import Transcript, WordResult
from live_gibberish.vad import SpeechSegment
from live_gibberish.word_events import transcript_to_final_word_events


class WordEventTests(unittest.TestCase):
    def test_transcript_words_become_final_word_events(self):
        segment = SpeechSegment(pcm=b"\x00\x00", start_timestamp=2.0, end_timestamp=3.0, frame_count=1)
        transcript = Transcript(
            text="Hello cave",
            words=(
                WordResult("Hello", 0.10, 0.30, 0.91),
                WordResult("cave!", 0.40, 0.70, 0.88),
            ),
        )

        events = transcript_to_final_word_events(segment, transcript)

        self.assertEqual([event.raw_text for event in events], ["Hello", "cave!"])
        self.assertEqual([event.normalized_text for event in events], ["hello", "cave"])
        self.assertEqual(events[0].start_ms, 2100)
        self.assertEqual(events[1].end_ms, 2700)
        self.assertTrue(all(event.is_final for event in events))


if __name__ == "__main__":
    unittest.main()
