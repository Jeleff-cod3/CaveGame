# Current Status

The app is now strict GPU-only for production paths.

Implemented:

- GPU `faster-whisper` ASR backend.
- Coqui XTTS TTS backend.
- Whitelist filtering and deterministic gibberish text mapping.
- Audio replacement assembly.
- Django REST endpoints and Channels WebSocket.
- Browser test frontend.
- Unity audio client script.
- Benchmark script.

Explicitly removed:

- Production fake ASR.
- Production fake TTS.
- CPU ASR fallback.
- Vosk production option.
- Silent TTS fallback.

Current limitation:

- The machine must have a working CUDA/cuBLAS setup for `faster-whisper`.
- Coqui XTTS must be installed and configured with an enrollment WAV for real
  replacement speech.
- If either model path fails, the browser log should show the exact exception.

