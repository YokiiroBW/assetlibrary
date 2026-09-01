"""Lightweight, Windows-independent contract checks for the service adapter."""
from pathlib import Path
import re
import unittest


SCRIPT = Path(__file__).parent / "scripts" / "windows-service.ps1"


class WindowsServiceContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.source = SCRIPT.read_text(encoding="utf-8")

    def test_install_preflights_existing_service_wrapper_and_port(self):
        for required in (
            "Get-CimInstance -ClassName Win32_Service",
            'throw "service already exists: $Name"',
            "service wrapper already exists; refusing to overwrite",
            "Get-NetTCPConnection -State Listen -LocalPort",
            "Assert-PortAvailable 5080",
        ):
            self.assertIn(required, self.source)
        self.assertIn("(Test-Path -LiteralPath $Wrapper -PathType Leaf) -or (Test-Path -LiteralPath $Marker -PathType Leaf)", self.source)

    def test_cleanup_is_guarded_by_created_this_run_flags(self):
        self.assertIn("$wrapperCreated = $false", self.source)
        self.assertIn("$markerCreated = $false", self.source)
        self.assertIn("$serviceCreated = $false", self.source)
        self.assertRegex(self.source, r"\$serviceCreated = \$true[\s\S]*?if \(\$serviceCreated -and")
        self.assertRegex(self.source, r"\$wrapperCreated = \$true[\s\S]*?if \(\$wrapperCreated\)")
        self.assertNotIn("catch {}; throw", self.source)

    def test_ownership_uses_marker_and_service_binpath_readback(self):
        self.assertIn("function Test-OwnedWrapper", self.source)
        self.assertIn("Get-Content -LiteralPath $Marker -Raw", self.source)
        self.assertIn("$WrapperText", self.source)
        self.assertIn("-NoNewline -Value $WrapperText", self.source)
        self.assertIn("-and (Get-Content -LiteralPath $Wrapper -Raw) -eq $WrapperText", self.source)
        self.assertIn("$service.PathName", self.source)
        self.assertIn("Assert-OwnedService", self.source)
        self.assertIn("service account is not LocalService", self.source)

    def test_cleanup_retains_referenced_files_and_only_removes_empty_data(self):
        self.assertIn("$installError = $_", self.source)
        self.assertIn("install cleanup incomplete; service still exists, wrapper and marker were retained", self.source)
        self.assertIn("throw $installError", self.source)
        self.assertIn("install cleanup incomplete; service still exists, wrapper and marker were retained", self.source)
        self.assertIn("function Remove-OwnedDataIfEmpty", self.source)
        self.assertIn("Get-ChildItem -LiteralPath $Data -Force", self.source)
        self.assertNotIn("Remove-Item -Recurse", self.source)
        self.assertIn("$dataCreated", self.source)

    def test_absent_service_requires_both_owned_files_or_both_missing(self):
        self.assertIn("if (!$wrapperExists -and !$markerExists)", self.source)
        self.assertIn("if (!$wrapperExists -or !$markerExists -or !(Test-OwnedWrapper))", self.source)
        self.assertIn("service absent but wrapper ownership state is partial or unknown", self.source)

    def test_start_stop_uninstall_prove_ownership_before_mutation(self):
        for action in ("$Action -eq 'start'", "$Action -eq 'stop'"):
            start = self.source.index(action)
            end = self.source.find("if ($Action -eq", start + 1)
            block = self.source[start:] if end < 0 else self.source[start:end]
            self.assertIn("Assert-OwnedService", block)
        uninstall = self.source[self.source.index("$Action -eq 'uninstall'"):]
        self.assertIn("$service = Assert-OwnedService", uninstall)
        self.assertIn("!(Test-OwnedWrapper)", uninstall)

    def test_stop_delete_and_final_readback_are_bounded(self):
        self.assertIn("function Wait-ServiceState", self.source)
        self.assertIn("function Wait-ServiceAbsent", self.source)
        self.assertIn("AddSeconds($TimeoutSeconds)", self.source)
        self.assertIn("if (!(Wait-ServiceState 'Stopped'))", self.source)
        self.assertIn("if (!(Wait-ServiceAbsent))", self.source)

    def test_no_localized_sc_output_is_parsed(self):
        self.assertNotRegex(self.source, r"sc\.exe[^\n]*\|\s*(Select-String|findstr|find)")
        self.assertIn("Get-CimInstance", self.source)


if __name__ == "__main__":
    unittest.main()
