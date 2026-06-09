import sys
import types
import unittest

from live_gibberish.asr import FasterWhisperAsr, create_asr


class AsrTests(unittest.TestCase):
    def test_create_asr_rejects_unknown_backend(self):
        with self.assertRaises(ValueError):
            create_asr("unknown")

    def test_create_asr_rejects_removed_backend(self):
        with self.assertRaises(ValueError):
            create_asr("removed-backend")

    def test_faster_whisper_uses_cuda_only(self):
        original_module = sys.modules.get("faster_whisper")
        calls = []

        class FakeWord:
            word = " hello"
            start = 0.0
            end = 0.2
            probability = 0.9

        class FakeSegment:
            text = "hello"
            words = [FakeWord()]

        class FakeWhisperModel:
            def __init__(self, model_name, device, compute_type):
                self.device = device
                calls.append((model_name, device, compute_type))

            def transcribe(self, audio, language, word_timestamps, vad_filter):
                return iter([FakeSegment()]), object()

        sys.modules["faster_whisper"] = types.SimpleNamespace(WhisperModel=FakeWhisperModel)
        try:
            asr = FasterWhisperAsr(model_name="base.en")
            transcript = asr.transcribe(b"\x00\x00" * 320, 16_000)
        finally:
            if original_module is None:
                sys.modules.pop("faster_whisper", None)
            else:
                sys.modules["faster_whisper"] = original_module

        self.assertEqual(transcript.text, "hello")
        self.assertEqual(asr.device, "cuda")
        self.assertIn(("base.en", "cuda", "float16"), calls)

    def test_faster_whisper_does_not_fallback_when_cuda_fails(self):
        original_module = sys.modules.get("faster_whisper")

        class FakeWhisperModel:
            def __init__(self, model_name, device, compute_type):
                self.device = device

            def transcribe(self, audio, language, word_timestamps, vad_filter):
                def bad_segments():
                    raise RuntimeError("Library cublas64_12.dll is not found or cannot be loaded")
                    yield

                return bad_segments(), object()

        sys.modules["faster_whisper"] = types.SimpleNamespace(WhisperModel=FakeWhisperModel)
        try:
            asr = FasterWhisperAsr(model_name="base.en")
            with self.assertRaises(RuntimeError):
                asr.transcribe(b"\x00\x00" * 320, 16_000)
        finally:
            if original_module is None:
                sys.modules.pop("faster_whisper", None)
            else:
                sys.modules["faster_whisper"] = original_module


if __name__ == "__main__":
    unittest.main()
