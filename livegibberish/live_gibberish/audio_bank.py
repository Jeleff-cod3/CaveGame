from __future__ import annotations

import hashlib
import json
import re
import wave
from dataclasses import asdict, dataclass
from io import BytesIO
from pathlib import Path
from typing import Any, Iterable

import numpy as np

from .audio_io import AudioConfig
from .text_normalization import normalize_word
from .tts import apply_fade, match_duration
from .wav_utils import resample_pcm16, write_wav


AUDIO_BANK_ROOT = Path(__file__).resolve().parent.parent / "audio_bank"
DURATION_BUCKETS = ("short", "medium", "long")
MANIFEST_FILE_NAME = "manifest.json"
TARGET_RMS = 6000.0
SILENCE_PEAK_THRESHOLD = 256
SILENCE_RMS_THRESHOLD = 96.0
CLIPPING_SAMPLE_THRESHOLD = 32760


@dataclass(frozen=True)
class AudioBankIssue:
    code: str
    message: str
    path: str = ""


@dataclass(frozen=True)
class AudioBankValidationReport:
    user_id: str
    ok: bool
    issues: tuple[AudioBankIssue, ...]

    def to_dict(self) -> dict[str, Any]:
        return {
            "user_id": self.user_id,
            "ok": self.ok,
            "issues": [asdict(issue) for issue in self.issues],
        }


@dataclass(frozen=True)
class AudioBankClip:
    name: str
    pcm: bytes
    duration_seconds: float


class AudioBankValidationError(ValueError):
    def __init__(self, report: AudioBankValidationReport) -> None:
        self.report = report
        messages = "; ".join(issue.message for issue in report.issues)
        super().__init__(messages or "Audio bank validation failed.")


class SampleBank:
    def __init__(
        self,
        user_id: str,
        whitelist: dict[str, AudioBankClip],
        gibberish: dict[str, tuple[AudioBankClip, ...]],
        config: AudioConfig = AudioConfig(),
    ) -> None:
        self.user_id = user_id
        self.whitelist = whitelist
        self.gibberish = gibberish
        self.config = config

    @classmethod
    def load(
        cls,
        user_id: str,
        required_whitelist: Iterable[str] = (),
        root: str | Path = AUDIO_BANK_ROOT,
        config: AudioConfig = AudioConfig(),
    ) -> "SampleBank":
        report = validate_audio_bank(user_id, required_whitelist=required_whitelist, root=root, config=config)
        if not report.ok:
            raise AudioBankValidationError(report)

        manifest = load_manifest(user_id, root=root)
        user_directory = audio_bank_user_directory(user_id, root=root)
        whitelist = {
            word: AudioBankClip(
                name=word,
                pcm=_read_preprocessed_wav(user_directory / entry["path"], config),
                duration_seconds=_read_wav_duration(user_directory / entry["path"]),
            )
            for word, entry in manifest.get("whitelist", {}).items()
        }
        gibberish = {
            bucket: tuple(
                AudioBankClip(
                    name=str(entry.get("name") or Path(entry["path"]).stem),
                    pcm=_read_preprocessed_wav(user_directory / entry["path"], config),
                    duration_seconds=_read_wav_duration(user_directory / entry["path"]),
                )
                for entry in manifest.get("gibberish", {}).get(bucket, [])
            )
            for bucket in DURATION_BUCKETS
        }
        return cls(user_id=user_id, whitelist=whitelist, gibberish=gibberish, config=config)

    def has_whitelist_word(self, word: str) -> bool:
        return normalize_word(word) in self.whitelist

    def render_whitelist_word(self, word: str, target_seconds: float) -> bytes:
        clip = self.whitelist[normalize_word(word)]
        return match_duration(clip.pcm, max(target_seconds, clip.duration_seconds), self.config)

    def render_gibberish(self, key: str, target_seconds: float) -> bytes:
        bucket = duration_bucket(target_seconds)
        clips = self.gibberish.get(bucket, ())
        if not clips:
            raise ValueError(f"Audio bank has no gibberish clips for duration bucket: {bucket}.")
        clip = clips[_stable_index(key, len(clips))]
        return _repeat_to_duration(clip.pcm, target_seconds, self.config)


def create_empty_manifest(user_id: str) -> dict[str, Any]:
    return {
        "user_id": sanitize_user_id(user_id),
        "whitelist": {},
        "gibberish": {bucket: [] for bucket in DURATION_BUCKETS},
    }


def load_manifest(user_id: str, root: str | Path = AUDIO_BANK_ROOT) -> dict[str, Any]:
    path = manifest_path(user_id, root=root)
    if not path.exists():
        return create_empty_manifest(user_id)
    data = json.loads(path.read_text(encoding="utf-8"))
    manifest = create_empty_manifest(str(data.get("user_id") or user_id))
    manifest["whitelist"].update(data.get("whitelist", {}))
    for bucket in DURATION_BUCKETS:
        manifest["gibberish"][bucket] = list(data.get("gibberish", {}).get(bucket, []))
    return manifest


def save_manifest(user_id: str, manifest: dict[str, Any], root: str | Path = AUDIO_BANK_ROOT) -> Path:
    directory = audio_bank_user_directory(user_id, root=root)
    directory.mkdir(parents=True, exist_ok=True)
    path = manifest_path(user_id, root=root)
    path.write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    return path


def save_whitelist_recording(
    user_id: str,
    word: str,
    wav_bytes: bytes,
    root: str | Path = AUDIO_BANK_ROOT,
    config: AudioConfig = AudioConfig(),
) -> dict[str, Any]:
    normalized = normalize_word(word)
    if not normalized:
        raise ValueError("Whitelist recording needs a non-empty word.")

    pcm = preprocess_recorded_wav(wav_bytes, config=config)
    user_directory = audio_bank_user_directory(user_id, root=root)
    relative_path = Path("samples") / "whitelist" / f"{normalized}.wav"
    destination = user_directory / relative_path
    destination.parent.mkdir(parents=True, exist_ok=True)
    write_wav(destination, pcm, config=config)

    manifest = load_manifest(user_id, root=root)
    manifest["whitelist"][normalized] = {"path": _manifest_path(relative_path), "word": normalized}
    save_manifest(user_id, manifest, root=root)
    return manifest


def save_gibberish_recording(
    user_id: str,
    bucket: str,
    wav_bytes: bytes,
    name: str = "",
    root: str | Path = AUDIO_BANK_ROOT,
    config: AudioConfig = AudioConfig(),
) -> dict[str, Any]:
    bucket = bucket.strip().lower()
    if bucket not in DURATION_BUCKETS:
        raise ValueError(f"Gibberish bucket must be one of: {', '.join(DURATION_BUCKETS)}.")

    pcm = preprocess_recorded_wav(wav_bytes, config=config)
    clip_name = sanitize_clip_name(name) or _recording_digest(wav_bytes)
    user_directory = audio_bank_user_directory(user_id, root=root)
    relative_path = Path("samples") / "gibberish" / bucket / f"{clip_name}.wav"
    destination = user_directory / relative_path
    destination.parent.mkdir(parents=True, exist_ok=True)
    write_wav(destination, pcm, config=config)

    manifest = load_manifest(user_id, root=root)
    entries = [entry for entry in manifest["gibberish"][bucket] if entry.get("name") != clip_name]
    entries.append({"path": _manifest_path(relative_path), "name": clip_name})
    manifest["gibberish"][bucket] = entries
    save_manifest(user_id, manifest, root=root)
    return manifest


def validate_audio_bank(
    user_id: str,
    required_whitelist: Iterable[str] = (),
    root: str | Path = AUDIO_BANK_ROOT,
    config: AudioConfig = AudioConfig(),
) -> AudioBankValidationReport:
    issues: list[AudioBankIssue] = []
    path = manifest_path(user_id, root=root)
    if not path.exists():
        issues.append(AudioBankIssue("missing-manifest", f"Missing audio bank manifest: {path}", str(path)))
        return AudioBankValidationReport(user_id=sanitize_user_id(user_id), ok=False, issues=tuple(issues))

    try:
        manifest = load_manifest(user_id, root=root)
    except Exception as exc:
        issues.append(AudioBankIssue("invalid-manifest", f"Manifest cannot be read: {type(exc).__name__}: {exc}", str(path)))
        return AudioBankValidationReport(user_id=sanitize_user_id(user_id), ok=False, issues=tuple(issues))

    whitelist = manifest.get("whitelist", {})
    for word in _normalized_words(required_whitelist):
        if word not in whitelist:
            issues.append(AudioBankIssue("missing-whitelist-word", f"Missing whitelist recording for word: {word}"))

    user_directory = audio_bank_user_directory(user_id, root=root)
    for word, entry in whitelist.items():
        issues.extend(_validate_manifest_clip(user_directory, entry, f"whitelist:{word}", config))

    gibberish = manifest.get("gibberish", {})
    for bucket in DURATION_BUCKETS:
        entries = gibberish.get(bucket, [])
        if not entries:
            issues.append(AudioBankIssue("missing-gibberish-bucket", f"Missing gibberish recordings for bucket: {bucket}"))
            continue
        for index, entry in enumerate(entries):
            issues.extend(_validate_manifest_clip(user_directory, entry, f"gibberish:{bucket}:{index}", config))

    return AudioBankValidationReport(
        user_id=sanitize_user_id(user_id),
        ok=not issues,
        issues=tuple(issues),
    )


def preprocess_recorded_wav(wav_bytes: bytes, config: AudioConfig = AudioConfig()) -> bytes:
    samples, sample_rate = _read_recording_samples(wav_bytes)
    _raise_if_bad_recording(samples)
    if sample_rate != config.sample_rate:
        samples = resample_pcm16(samples, sample_rate, config.sample_rate)
    trimmed = _trim_silence(samples)
    _raise_if_bad_recording(trimmed)
    normalized = _normalize_loudness(trimmed)
    return apply_fade(normalized.astype(np.int16).tobytes(), config, fade_ms=5)


def duration_bucket(seconds: float) -> str:
    if seconds < 0.30:
        return "short"
    if seconds < 0.80:
        return "medium"
    return "long"


def audio_bank_user_directory(user_id: str, root: str | Path = AUDIO_BANK_ROOT) -> Path:
    return Path(root) / "users" / sanitize_user_id(user_id)


def manifest_path(user_id: str, root: str | Path = AUDIO_BANK_ROOT) -> Path:
    return audio_bank_user_directory(user_id, root=root) / MANIFEST_FILE_NAME


def sanitize_user_id(user_id: str) -> str:
    cleaned = re.sub(r"[^a-zA-Z0-9_.-]+", "-", str(user_id).strip())
    return cleaned.strip(".-") or "default"


def sanitize_clip_name(name: str) -> str:
    cleaned = re.sub(r"[^a-zA-Z0-9_.-]+", "-", str(name).strip())
    return cleaned.strip(".-")


def _validate_manifest_clip(
    user_directory: Path,
    entry: dict[str, Any],
    label: str,
    config: AudioConfig,
) -> tuple[AudioBankIssue, ...]:
    issues: list[AudioBankIssue] = []
    relative = str(entry.get("path") or "")
    if not relative:
        return (AudioBankIssue("missing-path", f"{label} has no path."),)

    path = (user_directory / relative).resolve()
    if not path.exists():
        return (AudioBankIssue("missing-file", f"{label} file is missing: {path}", str(path)),)

    try:
        with wave.open(str(path), "rb") as wav:
            channels = wav.getnchannels()
            sample_width = wav.getsampwidth()
            sample_rate = wav.getframerate()
            pcm = wav.readframes(wav.getnframes())
    except Exception as exc:
        return (AudioBankIssue("invalid-wav", f"{label} is not a readable WAV: {type(exc).__name__}: {exc}", str(path)),)

    if channels != config.channels:
        issues.append(AudioBankIssue("invalid-channels", f"{label} has {channels} channels, expected {config.channels}.", str(path)))
    if sample_width != config.sample_width_bytes:
        issues.append(AudioBankIssue("invalid-sample-width", f"{label} is {sample_width * 8}-bit, expected {config.sample_width_bytes * 8}-bit.", str(path)))
    if sample_rate != config.sample_rate:
        issues.append(AudioBankIssue("invalid-sample-rate", f"{label} is {sample_rate} Hz, expected {config.sample_rate} Hz.", str(path)))
    issues.extend(_audio_quality_issues(label, path, pcm))
    return tuple(issues)


def _audio_quality_issues(label: str, path: Path, pcm: bytes) -> tuple[AudioBankIssue, ...]:
    samples = np.frombuffer(pcm, dtype=np.int16)
    if samples.size == 0:
        return (AudioBankIssue("silence", f"{label} has no audio samples.", str(path)),)

    peak = int(np.max(np.abs(samples.astype(np.int32))))
    rms = float(np.sqrt(np.mean(samples.astype(np.float32) ** 2)))
    issues: list[AudioBankIssue] = []
    if peak < SILENCE_PEAK_THRESHOLD or rms < SILENCE_RMS_THRESHOLD:
        issues.append(AudioBankIssue("silence", f"{label} is silent or too quiet.", str(path)))
    if int(np.count_nonzero(np.abs(samples.astype(np.int32)) >= CLIPPING_SAMPLE_THRESHOLD)) > 0:
        issues.append(AudioBankIssue("clipping", f"{label} contains clipped samples.", str(path)))
    return tuple(issues)


def _read_recording_samples(wav_bytes: bytes) -> tuple[np.ndarray, int]:
    try:
        with wave.open(BytesIO(wav_bytes), "rb") as wav:
            channels = wav.getnchannels()
            sample_width = wav.getsampwidth()
            sample_rate = wav.getframerate()
            pcm = wav.readframes(wav.getnframes())
    except Exception as exc:
        raise ValueError(f"Recording is not a readable WAV: {type(exc).__name__}: {exc}") from exc

    if sample_width != 2:
        raise ValueError(f"Recording must be 16-bit PCM WAV, got {sample_width * 8}-bit.")
    if sample_rate <= 0:
        raise ValueError("Recording has an invalid sample rate.")

    samples = np.frombuffer(pcm, dtype=np.int16)
    if channels > 1:
        samples = samples.reshape(-1, channels).mean(axis=1).astype(np.int16)
    return samples.astype(np.int16), sample_rate


def _raise_if_bad_recording(samples: np.ndarray) -> None:
    pcm = samples.astype(np.int16).tobytes()
    issues = _audio_quality_issues("recording", Path("recording.wav"), pcm)
    if issues:
        raise ValueError("; ".join(issue.message for issue in issues))


def _trim_silence(samples: np.ndarray) -> np.ndarray:
    if samples.size == 0:
        return samples
    absolute = np.abs(samples.astype(np.int32))
    peak = int(np.max(absolute))
    threshold = max(SILENCE_PEAK_THRESHOLD, round(peak * 0.03))
    voiced = np.flatnonzero(absolute >= threshold)
    if voiced.size == 0:
        return samples[:0]
    return samples[int(voiced[0]) : int(voiced[-1]) + 1]


def _normalize_loudness(samples: np.ndarray) -> np.ndarray:
    if samples.size == 0:
        return samples.astype(np.int16)
    float_samples = samples.astype(np.float32)
    rms = float(np.sqrt(np.mean(float_samples ** 2)))
    if rms <= 0.0:
        return samples.astype(np.int16)
    peak = float(np.max(np.abs(float_samples)))
    gain = min(TARGET_RMS / rms, 0.95 * 32767.0 / max(1.0, peak))
    return np.clip(float_samples * gain, -32767, 32767).astype(np.int16)


def _read_preprocessed_wav(path: Path, config: AudioConfig) -> bytes:
    with wave.open(str(path), "rb") as wav:
        return wav.readframes(wav.getnframes())


def _read_wav_duration(path: Path) -> float:
    with wave.open(str(path), "rb") as wav:
        if wav.getframerate() <= 0:
            return 0.0
        return wav.getnframes() / wav.getframerate()


def _repeat_to_duration(pcm: bytes, target_seconds: float, config: AudioConfig) -> bytes:
    target_bytes = _duration_to_bytes(target_seconds, config)
    if target_bytes <= 0 or not pcm:
        return b""
    if len(pcm) >= target_bytes:
        return apply_fade(pcm[:target_bytes], config, fade_ms=5)
    repeats = (target_bytes + len(pcm) - 1) // len(pcm)
    return apply_fade((pcm * repeats)[:target_bytes], config, fade_ms=5)


def _duration_to_bytes(seconds: float, config: AudioConfig) -> int:
    sample_count = max(0, round(seconds * config.sample_rate))
    return sample_count * config.channels * config.sample_width_bytes


def _normalized_words(words: Iterable[str]) -> tuple[str, ...]:
    seen = set()
    normalized_words = []
    for word in words:
        normalized = normalize_word(str(word))
        if normalized and normalized not in seen:
            seen.add(normalized)
            normalized_words.append(normalized)
    return tuple(normalized_words)


def _manifest_path(path: Path) -> str:
    return path.as_posix()


def _recording_digest(wav_bytes: bytes) -> str:
    return hashlib.sha256(wav_bytes).hexdigest()[:16]


def _stable_index(key: str, size: int) -> int:
    digest = hashlib.sha256(key.encode("utf-8")).digest()
    return int.from_bytes(digest[:4], "big") % size
