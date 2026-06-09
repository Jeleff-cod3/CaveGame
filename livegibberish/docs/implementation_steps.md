# Implementation Notes

This folder implements a strict real-backend version of the live gibberish
pipeline:

1. Capture or receive 16 kHz mono 16-bit PCM frames.
2. Detect speech and buffer segments.
3. Run GPU `faster-whisper` ASR.
4. Normalize word timestamps.
5. Check recognized words against the whitelist.
6. Extract original per-word audio from the timed buffer.
7. Map blocked words to deterministic gibberish text.
8. Synthesize replacement audio with Coqui XTTS.
9. Assemble allowed original audio plus generated replacement audio.
10. Expose REST and WebSocket APIs through Django/Channels.
11. Provide a minimal browser frontend for manual testing.
12. Provide benchmark and setup scripts.

No production fake ASR, fake TTS, Vosk CPU ASR, or CPU ASR fallback is present.
Backend failures are surfaced as errors.

