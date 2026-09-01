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
            "staging root already exists; refusing to overwrite",
            "Get-NetTCPConnection -State Listen -LocalPort",
            "Assert-PortAvailable 5080",
        ):
            self.assertIn(required, self.source)
        self.assertIn("$StageRoot", self.source)
        self.assertIn("$BinPath = '\"' + $StageExe", self.source)
        self.assertNotIn(".cmd", self.source)

    def test_cleanup_is_guarded_by_created_this_run_flags(self):
        self.assertIn("$stagingCreated = $false", self.source)
        self.assertIn("$stagingCreated = $true", self.source)
        self.assertIn("$installError = $_", self.source)
        self.assertRegex(self.source, r"\$stagingCreated[\s\S]*?Remove-OwnedStaging")
        self.assertNotIn("catch {}; throw", self.source)

    def test_ownership_uses_marker_and_service_binpath_readback(self):
        self.assertIn("function Get-Marker", self.source)
        self.assertIn("ConvertFrom-Json", self.source)
        self.assertIn("$marker.bin_path", self.source)
        self.assertIn("$service.PathName", self.source)
        self.assertIn("Assert-OwnedService", self.source)
        self.assertIn("service account is not exact LocalService", self.source)
        self.assertIn("[Guid]::NewGuid()", self.source)
        self.assertIn("[Guid]::TryParse", self.source)
        self.assertIn("sc.exe", self.source)
        self.assertIn("description", self.source)

    def test_cleanup_retains_referenced_files_and_only_removes_empty_data(self):
        self.assertIn("$installError = $_", self.source)
        self.assertIn("install cleanup incomplete; service remains and staging was retained", self.source)
        self.assertIn("throw $installError", self.source)
        self.assertIn("function Remove-EmptyOwnedData", self.source)
        self.assertIn("Get-ChildItem -LiteralPath $Data -Force", self.source)
        self.assertIn("Remove-Item -LiteralPath $StageApp -Recurse -Force", self.source)
        self.assertIn("Test-Path -LiteralPath $StageApp -PathType Container", self.source)
        self.assertIn("unknown staging item", self.source)

    def test_absent_service_requires_root_marker_ownership(self):
        self.assertIn("if (!(Test-Path -LiteralPath $StageRoot))", self.source)
        self.assertIn("[void](Get-Marker)", self.source)
        self.assertIn("refusing to remove unknown staging", self.source)
        self.assertIn("staging data is not a directory", self.source)

    def test_start_stop_uninstall_prove_ownership_before_mutation(self):
        for action in ("$Action -eq 'start'", "$Action -eq 'stop'"):
            start = self.source.index(action)
            end = self.source.find("if ($Action -eq", start + 1)
            block = self.source[start:] if end < 0 else self.source[start:end]
            self.assertIn("Assert-OwnedService", block)
        uninstall = self.source[self.source.index("$Action -eq 'uninstall'"):]
        self.assertIn("$service = Assert-OwnedService", uninstall)
        self.assertIn("$service = Assert-OwnedService", uninstall)

    def test_stop_delete_and_final_readback_are_bounded(self):
        self.assertIn("function Wait-ServiceState", self.source)
        self.assertIn("function Wait-ServiceAbsent", self.source)
        self.assertIn("if (!(Wait-ServiceState 'Stopped'))", self.source)
        self.assertIn("if (!(Wait-ServiceAbsent))", self.source)
        self.assertIn("[Diagnostics.Stopwatch]::StartNew()", self.source)

    def test_cli_probe_acl_mutex_and_health_pid_listener_contract(self):
        self.assertIn("--spike-data-path", self.source)
        self.assertIn("--spike-port", self.source)
        self.assertIn("--spike-bind-host", self.source)
        self.assertIn("--health-probe --spike-port 5080 --spike-probe-host 127.0.0.1", self.source)
        program = SCRIPT.parent.parent / "src" / "Program.cs"
        self.assertIn("IsSuccessStatusCode", program.read_text(encoding="utf-8"))
        self.assertIn('status.GetString() == "ok"', program.read_text(encoding="utf-8"))
        self.assertIn("TryAdd(key, value)", program.read_text(encoding="utf-8"))
        self.assertIn("missing value for {key}", program.read_text(encoding="utf-8"))
        self.assertIn("*S-1-5-19", self.source)
        self.assertIn("Global\\AssetLibrary-M0-004-Spike", self.source)
        self.assertIn("OwningProcess -eq $service.ProcessId", self.source)

    def test_no_localized_sc_output_is_parsed(self):
        self.assertNotRegex(self.source, r"sc\.exe[^\n]*\|\s*(Select-String|findstr|find)")
        self.assertIn("Get-CimInstance", self.source)


if __name__ == "__main__":
    unittest.main()
