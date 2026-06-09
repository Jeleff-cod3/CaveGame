import sys
import unittest
from unittest import mock

from scripts import setup_real_backends
from scripts.setup_real_backends import require_coqui_xtts_python


class SetupScriptTests(unittest.TestCase):
    def test_coqui_xtts_rejects_python_312_or_newer(self):
        with mock.patch.object(sys, "version_info", (3, 13, 1)):
            with self.assertRaises(SystemExit) as raised:
                require_coqui_xtts_python()

        self.assertIn("requires Python >=3.9 and <3.12", str(raised.exception))

    def test_find_vsdevcmd_uses_environment_override(self):
        with mock.patch.dict("os.environ", {"LIVE_GIBBERISH_VSDEVCMD": r"C:\Tools\VsDevCmd.bat"}):
            with mock.patch.object(setup_real_backends.Path, "is_file", return_value=True):
                self.assertEqual(setup_real_backends.find_vsdevcmd(), r"C:\Tools\VsDevCmd.bat")

    def test_native_build_uses_vsdevcmd_on_windows(self):
        calls = []

        with mock.patch.object(setup_real_backends.os, "name", "nt"):
            with mock.patch.object(setup_real_backends, "find_vsdevcmd", return_value=r"C:\VS\VsDevCmd.bat"):
                with mock.patch.object(setup_real_backends.subprocess, "check_call", side_effect=calls.append):
                    setup_real_backends.install_packages(["TTS>=0.22,<0.23"], needs_native_build=True)

        self.assertEqual(calls[0][0:3], ["cmd.exe", "/d", "/c"])
        self.assertTrue(calls[0][3].endswith(".cmd"))


if __name__ == "__main__":
    unittest.main()
