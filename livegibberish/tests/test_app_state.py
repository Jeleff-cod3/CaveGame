import unittest
from unittest.mock import patch

from live_gibberish.vad import VadDecision
from live_gibberish_web.app_state import RuntimeConfig, build_processor


class FakeAsr:
    pass


class FakeVad:
    def is_speech(self, frame):
        return VadDecision(False, 0.0)


class FakeBank:
    pass


class AppStateTests(unittest.TestCase):
    def test_audio_bank_processor_startup_does_not_create_tts(self):
        config = RuntimeConfig(
            whitelist=("hello",),
            audio_bank_user="player-one",
            audio_bank_missing_word_policy="strict",
            audio_replacement_mode="prerecorded_sample_substitution",
        )

        with patch("live_gibberish_web.app_state.SampleBank.load", return_value=FakeBank()) as load_bank:
            with patch("live_gibberish_web.app_state.create_asr", return_value=FakeAsr()):
                with patch("live_gibberish_web.app_state.create_vad", return_value=FakeVad()):
                    with patch("live_gibberish_web.app_state.create_tts_engine") as create_tts:
                        processor = build_processor(config)

        self.assertIsNotNone(processor)
        create_tts.assert_not_called()
        load_bank.assert_called_once()

    def test_original_mode_keeps_tts_path_even_when_audio_bank_user_is_set(self):
        config = RuntimeConfig(
            whitelist=("hello",),
            audio_bank_user="player-one",
            audio_replacement_mode="original_gibberish",
        )

        with patch("live_gibberish_web.app_state.SampleBank.load") as load_bank:
            with patch("live_gibberish_web.app_state.create_asr", return_value=FakeAsr()):
                with patch("live_gibberish_web.app_state.create_vad", return_value=FakeVad()):
                    with patch("live_gibberish_web.app_state.create_tts_engine", return_value=object()) as create_tts:
                        processor = build_processor(config)

        self.assertIsNotNone(processor)
        load_bank.assert_not_called()
        create_tts.assert_called_once()


if __name__ == "__main__":
    unittest.main()
