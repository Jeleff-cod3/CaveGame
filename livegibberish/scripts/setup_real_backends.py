from __future__ import annotations

import argparse
import os
from pathlib import Path
import subprocess
import sys
import tempfile


VSDEVCMD_CANDIDATES = (
    r"C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\Tools\VsDevCmd.bat",
    r"C:\Program Files\Microsoft Visual Studio\2022\BuildTools\Common7\Tools\VsDevCmd.bat",
    r"C:\Program Files\Microsoft Visual Studio\2022\Professional\Common7\Tools\VsDevCmd.bat",
    r"C:\Program Files\Microsoft Visual Studio\2022\Enterprise\Common7\Tools\VsDevCmd.bat",
)
WINDOWS_KITS_10_ROOT = Path(r"C:\Program Files (x86)\Windows Kits\10")


def require_coqui_xtts_python() -> None:
    if sys.version_info >= (3, 12):
        major, minor = sys.version_info[:2]
        raise SystemExit(
            "Coqui TTS 0.22 requires Python >=3.9 and <3.12. "
            f"This venv is Python {major}.{minor}. "
            "Install Python 3.11, recreate .venv with `py -3.11 -m venv .venv`, "
            "then rerun this setup command."
        )


def find_vsdevcmd() -> str | None:
    override = os.environ.get("LIVE_GIBBERISH_VSDEVCMD")
    if override and Path(override).is_file():
        return override
    for candidate in VSDEVCMD_CANDIDATES:
        if Path(candidate).is_file():
            return candidate
    return None


def quote_cmd_arg(value: str) -> str:
    return '"' + value.replace('"', r'\"') + '"'


def find_windows_sdk_version() -> str | None:
    include_root = WINDOWS_KITS_10_ROOT / "Include"
    if not include_root.is_dir():
        return None
    versions = sorted(
        (path.name for path in include_root.iterdir() if (path / "ucrt" / "io.h").is_file()),
        reverse=True,
    )
    return versions[0] if versions else None


def write_windows_sdk_env(batch, version: str) -> None:
    include_root = WINDOWS_KITS_10_ROOT / "Include" / version
    lib_root = WINDOWS_KITS_10_ROOT / "Lib" / version
    bin_root = WINDOWS_KITS_10_ROOT / "bin" / version
    batch.write(f'set "WindowsSdkDir={WINDOWS_KITS_10_ROOT}\\"\n')
    batch.write(f'set "WindowsSDKVersion={version}\\"\n')
    batch.write(f'set "UCRTVersion={version}"\n')
    batch.write(f'set "INCLUDE={include_root / "ucrt"};{include_root / "um"};{include_root / "shared"};%INCLUDE%"\n')
    batch.write(f'set "LIB={lib_root / "ucrt" / "x64"};{lib_root / "um" / "x64"};%LIB%"\n')
    batch.write(f'set "PATH={bin_root / "x64"};{bin_root / "x86"};%PATH%"\n')


def install_packages(packages: list[str], *, needs_native_build: bool = False) -> None:
    if not packages:
        return

    if needs_native_build and os.name == "nt":
        vsdevcmd = find_vsdevcmd()
        if vsdevcmd is None:
            raise SystemExit(
                "Coqui TTS needs the Visual Studio C++ build environment on Windows. "
                "Install Visual Studio 2022 Build Tools with the C++ workload and "
                "Windows 10/11 SDK, then rerun this command."
            )
        pip_args = " ".join(quote_cmd_arg(package) for package in packages)
        with tempfile.NamedTemporaryFile("w", suffix=".cmd", delete=False, encoding="utf-8") as batch:
            batch_path = Path(batch.name)
            batch.write("@echo off\n")
            batch.write(f'call "{vsdevcmd}" -arch=x64 -host_arch=x64\n')
            batch.write("if errorlevel 1 exit /b %errorlevel%\n")
            sdk_version = find_windows_sdk_version()
            if sdk_version is not None:
                write_windows_sdk_env(batch, sdk_version)
            batch.write(f'"{sys.executable}" -m pip install {pip_args}\n')
            batch.write("exit /b %errorlevel%\n")
        try:
            subprocess.check_call(["cmd.exe", "/d", "/c", str(batch_path)])
        finally:
            batch_path.unlink(missing_ok=True)
        return

    subprocess.check_call([sys.executable, "-m", "pip", "install", *packages])


def main() -> None:
    parser = argparse.ArgumentParser(description="Install optional real ASR/TTS backends for live gibberish.")
    parser.add_argument("--faster-whisper", action="store_true", help="Install faster-whisper.")
    parser.add_argument("--webrtcvad", action="store_true", help="Install wheel-backed WebRTC VAD when available.")
    parser.add_argument("--coqui-xtts", action="store_true", help="Install Coqui TTS for XTTS voice cloning.")
    args = parser.parse_args()

    packages: list[str] = []
    needs_native_build = False
    if args.faster_whisper:
        packages.append("faster-whisper>=1.0,<2.0")
    if args.webrtcvad:
        packages.append("webrtcvad-wheels")
    if args.coqui_xtts:
        require_coqui_xtts_python()
        packages.append("TTS>=0.22,<0.23")
        needs_native_build = True

    install_packages(packages, needs_native_build=needs_native_build)

    print("Optional backend setup complete.")


if __name__ == "__main__":
    main()
