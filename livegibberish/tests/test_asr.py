import sys
import types
import unittest

from live_gibberish.asr import FasterWhisperAsr, OpenAiWhisperAsr, create_asr


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

    def test_openai_whisper_uses_cuda_only(self):
        original_torch = sys.modules.get("torch")
        original_whisper = sys.modules.get("whisper")
        calls = []

        class FakeCuda:
            @staticmethod
            def is_available():
                return True

        class FakeModel:
            def transcribe(self, audio, language, fp16, verbose, word_timestamps, initial_prompt=None):
                calls.append((language, fp16, verbose, word_timestamps, len(audio), initial_prompt))
                return {
                    "segments": [
                        {
                            "text": "hello cave",
                            "start": 0.0,
                            "end": 1.0,
                            "words": [
                                {"word": " hello", "start": 0.10, "end": 0.30, "probability": 0.91},
                                {"word": " cave", "start": 0.62, "end": 0.90, "probability": 0.88},
                            ],
                        }
                    ]
                }

        def load_model(model_name, device):
            calls.append((model_name, device))
            return FakeModel()

        sys.modules["torch"] = types.SimpleNamespace(cuda=FakeCuda())
        sys.modules["whisper"] = types.SimpleNamespace(load_model=load_model)
        try:
            asr = OpenAiWhisperAsr(model_name="base.en")
            transcript = asr.transcribe(b"\x00\x00" * 16000, 16_000)
        finally:
            if original_torch is None:
                sys.modules.pop("torch", None)
            else:
                sys.modules["torch"] = original_torch
            if original_whisper is None:
                sys.modules.pop("whisper", None)
            else:
                sys.modules["whisper"] = original_whisper

        self.assertEqual(transcript.text, "hello cave")
        self.assertEqual([word.word for word in transcript.words], ["hello", "cave"])
        self.assertEqual(transcript.words[0].start, 0.10)
        self.assertEqual(transcript.words[1].end, 0.90)
        self.assertIn(("base.en", "cuda"), calls)
        self.assertIn(("en", True, False, True, 16000, None), calls)

    def test_openai_whisper_estimates_words_when_word_timestamps_are_missing(self):
        original_torch = sys.modules.get("torch")
        original_whisper = sys.modules.get("whisper")

        class FakeCuda:
            @staticmethod
            def is_available():
                return True

        class FakeModel:
            def transcribe(self, audio, **kwargs):
                return {"segments": [{"text": "hello cave", "start": 0.0, "end": 1.0}]}

        sys.modules["torch"] = types.SimpleNamespace(cuda=FakeCuda())
        sys.modules["whisper"] = types.SimpleNamespace(load_model=lambda model_name, device: FakeModel())
        try:
            asr = OpenAiWhisperAsr(model_name="base.en")
            transcript = asr.transcribe(b"\x00\x00" * 16000, 16_000)
        finally:
            if original_torch is None:
                sys.modules.pop("torch", None)
            else:
                sys.modules["torch"] = original_torch
            if original_whisper is None:
                sys.modules.pop("whisper", None)
            else:
                sys.modules["whisper"] = original_whisper

        self.assertEqual([word.word for word in transcript.words], ["hello", "cave"])
        self.assertEqual(transcript.words[0].start, 0.0)
        self.assertEqual(transcript.words[1].end, 1.0)

    def test_openai_whisper_uses_whitelist_prompt(self):
        original_torch = sys.modules.get("torch")
        original_whisper = sys.modules.get("whisper")
        calls = []

        class FakeCuda:
            @staticmethod
            def is_available():
                return True

        class FakeModel:
            def transcribe(self, audio, **kwargs):
                calls.append(kwargs)
                return {"segments": [{"text": "hello", "start": 0.0, "end": 0.5}]}

        sys.modules["torch"] = types.SimpleNamespace(cuda=FakeCuda())
        sys.modules["whisper"] = types.SimpleNamespace(load_model=lambda model_name, device: FakeModel())
        try:
            asr = OpenAiWhisperAsr(model_name="base.en", whitelist=["hello", "cave"])
            asr.transcribe(b"\x00\x00" * 16000, 16_000)
        finally:
            if original_torch is None:
                sys.modules.pop("torch", None)
            else:
                sys.modules["torch"] = original_torch
            if original_whisper is None:
                sys.modules.pop("whisper", None)
            else:
                sys.modules["whisper"] = original_whisper

        self.assertIn("hello", calls[0]["initial_prompt"])
        self.assertIn("cave", calls[0]["initial_prompt"])


if __name__ == "__main__":
    unittest.main()
