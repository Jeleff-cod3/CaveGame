# Current Status

The app is now strict GPU-only for production paths.

Implemented:

- GPU `openai-whisper` ASR backend.
- Coqui XTTS TTS backend.
- Whitelist filtering and deterministic gibberish text mapping.
- Audio replacement assembly.
- Django REST endpoints and Channels WebSocket.
- Browser test frontend.
- One-button browser recording that creates a backend session input WAV and
  processed output WAV.
- Unity audio client script.
- Benchmark script.

Explicitly removed:

- Production fake ASR.
- Production fake TTS.
- CPU ASR fallback.
- Vosk production option.
- Silent TTS fallback.

Current limitation:

- The machine must have working CUDA torch for `openai-whisper`.
- Recorded sessions must be 5s+ so the backend can use the session input WAV as
  the Coqui XTTS speaker reference.
- If either model path fails, the browser log should show the exact exception.

