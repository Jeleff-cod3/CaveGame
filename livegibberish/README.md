# Live Gibberish

Live Gibberish is the CaveGame voice filter. The current app is strict:

- ASR is GPU `openai-whisper` only.
- TTS is GPU Coqui XTTS only.
- There is no production fake ASR.
- There is no production fake TTS.
- There is no CPU fallback.
- If CUDA, cuBLAS, model loading, or TTS setup fails, the server reports the
  exact error to the browser log and stops processing.

## Install

Coqui TTS 0.22 requires Python `>=3.9` and `<3.12`. Use Python 3.11 for this
prototype. Python 3.13 cannot install the `TTS` package.

```powershell
cd "C:\Users\sashk\CaveGame\livegibberish"
Rename-Item .venv .venv-py313-broken
py -3.11 -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -r requirements.txt
python scripts\setup_real_backends.py --openai-whisper --coqui-xtts
```

The setup script installs CUDA 11.8 PyTorch wheels for this machine:
`torch==2.6.0+cu118` and `torchaudio==2.6.0+cu118`.

If `py -3.11` is not available, install Python 3.11 first:

```powershell
winget install Python.Python.3.11
```

Coqui TTS builds a native extension on Windows. If the build log mentions
`io.h` or `cl.exe`, install the Visual Studio C++ build tools and Windows SDK:

```powershell
winget install Microsoft.VisualStudio.2022.BuildTools --override "--wait --passive --add Microsoft.VisualStudio.Workload.VCTools --add Microsoft.VisualStudio.Component.Windows11SDK.22621"
```

The setup script will use `VsDevCmd.bat` automatically when it finds Visual
Studio Build Tools or Visual Studio Community.

Optional VAD package:

```powershell
python scripts\setup_real_backends.py --webrtcvad
```

## Run

Run Daphne from the project root, not from `docs` or `live_gibberish_web`.
Using the venv executable avoids accidentally running a globally installed
Daphne with the wrong Python environment. The `python -m daphne` form also
avoids stale Windows launcher paths if the project folder was renamed.

```powershell
cd "C:\Users\sashk\CaveGame\livegibberish"
.\.venv\Scripts\python.exe -m daphne -b 127.0.0.1 -p 8000 live_gibberish_web.asgi:application
```

Open:

```text
http://localhost:8000/
```

## Configure

Use the browser page:

- Whitelist Words: comma-separated allowed words.
- GPU ASR Model: `base.en`, `small.en`, `large-v3`, `turbo`, etc.
- Seed: deterministic gibberish mapping seed.
- Confidence: minimum confidence before a word is treated as allowed.
- Buffer Seconds: how much recent speech audio is retained for word replacement.

The ASR and TTS backends are fixed: GPU `openai-whisper` and GPU Coqui XTTS.
There is no upload or separate enrollment step. Press **Record**, speak for at
least five seconds, then press **Stop**. The backend writes a timestamped input
WAV in `runtime/sessions`, uses that WAV as the Coqui XTTS speaker reference,
runs the whitelist/gibberish/TTS replacement pipeline, and writes the filtered
output WAV next to it.

## Benchmark

```powershell
python scripts\benchmark_pipeline.py --source sample.wav --asr openai-whisper --model base.en --whitelist hello cave --tts coqui-xtts --output benchmark-output.wav --report benchmark-report.json
```

## Tests

```powershell
.\.venv\Scripts\python.exe -m unittest discover tests
```
