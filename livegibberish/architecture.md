# Live Gibberish Architecture

`live_gibberish/` contains the audio pipeline. It takes 16 kHz mono PCM frames, detects speech, transcribes finalized word events, and emits processed PCM.

`live_gibberish_web/` exposes the Django/Channels tester. HTTP config endpoints update runtime settings. The `/ws/audio/` websocket records microphone PCM and returns processed audio/session events.

`audio_bank/` stores user-owned prerecorded assets. Each user has `audio_bank/users/<user_id>/manifest.json`, whitelist WAVs by normalized word, and gibberish WAVs in `short`, `medium`, and `long` buckets. Upload endpoints preprocess WAVs before manifest write. `SampleBank` validates the manifest and preloads decoded PCM before live mode starts.

Banked live flow: microphone PCM is used for VAD and ASR only. ASR words become `WordEvent` items with raw text, normalized text, timing, confidence, and finality. Final words are substituted with a prerecorded whitelist sample when present. Whitelist samples keep the full recorded word if ASR reports a shorter span. Other words are substituted with prerecorded gibberish selected by duration: under 300 ms is `short`, 300-800 ms is `medium`, and over 800 ms is `long`. Gibberish repeats to cover the full blocked word span. `AudioOutputScheduler` queues samples on the ASR timeline, leaves gaps as silence, emits fixed-size frames, inserts silence on underrun, and fades clip edges to avoid clicks. Original microphone PCM and TTS output are not used in the banked output stream.

`runtime/` stores generated local runtime files. `runtime/sessions/` contains recorded input WAVs and processed output WAVs from browser sessions. `runtime/live-gibberish-config.json` stores the latest runtime config.

Tester config files: the web page can download the current form state as a JSON file with `live_gibberish_config_version`, `saved_at`, and `config`. Loading that JSON fills the form, saves it through the config API, validates the selected audio bank, and then the user can start the filter from the same page.

Benchmark logs: each processed segment logs input seconds, output seconds, output/input ratio, and output bytes. Each recorded browser session logs input WAV length, output WAV length, processing seconds, processing/input ratio, and realtime speed. The websocket session response includes the same benchmark object so the tester page can show the numbers beside the live logs.

API flow:

`POST /api/audio-bank/recording/` takes `user_id`, `kind`, a WAV `file`, and either `word` for whitelist clips or `bucket` plus optional `name` for gibberish clips. It writes a preprocessed WAV and updates the manifest.

`GET /api/audio-bank/status/` takes `user_id` and optional `whitelist` words. It validates the manifest, referenced files, sample format, silence, clipping, required whitelist coverage, and gibberish bucket coverage.

`POST /api/config/whitelist/` stores live settings. `audio_replacement_mode=original_gibberish` keeps the old live passthrough plus TTS replacement behavior. `audio_replacement_mode=prerecorded_sample_substitution` loads `audio_bank_user` at processor startup and uses scheduled prerecorded sample output. `audio_bank_missing_word_policy=strict` fails startup when configured whitelist recordings are missing. `safe` uses gibberish for missing whitelist samples. `debug` uses silence and logs a warning for missing whitelist samples.
