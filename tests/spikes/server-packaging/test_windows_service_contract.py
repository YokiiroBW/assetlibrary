"""Service guardrails tested without invoking SCM, registry writes or elevation."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


ROOT = Path(__file__).parent
SCRIPT = ROOT / "scripts" / "windows-service.ps1"
POWERSHELL = shutil.which("pwsh") or shutil.which("powershell")


class WindowsServiceContractTests(unittest.TestCase):
    def test_default_and_absence_checks_precede_elevation_and_mutations(self):
        source = SCRIPT.read_text(encoding="utf-8")
        self.assertIn("[string]$Action = 'preflight'", source)
        dispatch = source.index("if ($Action -eq 'preflight')")
        elevation = source.index("if ($Action -ne 'health') { Assert-Elevated }")
        self.assertLess(dispatch, elevation)
        self.assertLess(source.index("if ($Action -eq 'verify-absent')"), elevation)
        self.assertNotIn("service.cmd", source)
        self.assertIn("AssetLibrarySpikeOwner", source)
        self.assertIn("--SPIKE_DATA_PATH", source)

    def test_native_probe_and_build_provenance_are_retained(self):
        program = (ROOT / "src" / "Program.cs").read_text(encoding="utf-8")
        for marker in ("--build-info", "AssemblyInformationalVersionAttribute", "UseWindowsService()", 'status.GetString() == "ok"', 'contract.GetString() == "m0-004/v1"'):
            self.assertIn(marker, program)

    def test_waits_and_operation_serialization_are_bounded(self):
        source = SCRIPT.read_text(encoding="utf-8")
        self.assertIn("[Diagnostics.Stopwatch]::StartNew()", source)
        self.assertIn("-OperationTimeoutSec 5 -ErrorAction Stop", source)
        self.assertIn("$Mutex.WaitOne(30000)", source)
        self.assertIn("$Mutex.ReleaseMutex()", source)
        self.assertIn("$Mutex.Dispose()", source)
        self.assertIn("service readback is not Running", source)
        self.assertIn("service readback is not Stopped", source)


@unittest.skipUnless(POWERSHELL, "PowerShell is required for executable adapter regressions")
class WindowsServiceBehaviorTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="m0-004-service-contract-")
        self.root = Path(self.temp.name)
        artifact = self.root / "artifact"
        artifact.mkdir()
        (artifact / "ServerPackagingSpike.exe").write_text("synthetic artifact", encoding="utf-8")

    def tearDown(self):
        self.temp.cleanup()

    def run_powershell(self, body):
        # Load only function definitions, never the adapter's top-level dispatch.
        prefix = r"""
$ErrorActionPreference = 'Stop'
$Tokens = $null
$ParseErrors = $null
$Ast = [Management.Automation.Language.Parser]::ParseFile($env:ALIGN004_SCRIPT, [ref]$Tokens, [ref]$ParseErrors)
if ($ParseErrors.Count) { throw ($ParseErrors | Out-String) }
foreach ($Definition in $Ast.FindAll({ param($Node) $Node -is [Management.Automation.Language.FunctionDefinitionAst] }, $false)) {
    Invoke-Expression $Definition.Extent.Text
}
$Name = 'AssetLibrary-M0-004-Spike'
$OwnerMarker = 'AssetLibrary/M0-004/v1'
$StageParent = $env:ALIGN004_CASE_ROOT
$StageRoot = Join-Path $StageParent $Name
$StageApp = Join-Path $StageRoot 'app'
$StageExe = Join-Path $StageApp 'ServerPackagingSpike.exe'
$Data = Join-Path $StageRoot 'data'
$Marker = Join-Path $StageRoot 'owner.json'
$LegacyData = Join-Path $StageParent 'legacy-data'
$ArtifactDir = Join-Path $StageParent 'artifact'
$SourceExe = Join-Path $ArtifactDir 'ServerPackagingSpike.exe'
$ServiceKey = 'registry-fixture-only'
$ImagePath = '"{0}" --SPIKE_DATA_PATH "{1}" --SPIKE_PORT 5080 --SPIKE_BIND_HOST 127.0.0.1' -f $StageExe, $Data
$InstallId = '00000000-0000-0000-0000-000000000001'
function Seed-Staging {
    New-Item -ItemType Directory -Path $StageApp, $Data -Force | Out-Null
    [ordered]@{ owner = $OwnerMarker; install_id = $InstallId; bin_path = $ImagePath; data_path = $Data } |
        ConvertTo-Json -Compress | Set-Content -LiteralPath $Marker -Encoding UTF8
    Set-Content -LiteralPath $StageExe -Value 'synthetic artifact'
}
"""
        env = dict(os.environ, ALIGN004_SCRIPT=str(SCRIPT), ALIGN004_CASE_ROOT=str(self.root))
        result = subprocess.run([POWERSHELL, "-NoProfile", "-NonInteractive", "-Command", prefix + body], env=env, text=True, encoding="utf-8", capture_output=True, timeout=20)
        self.assertEqual(result.returncode, 0, result.stderr)
        return json.loads(result.stdout)

    def test_sc_argument_forwarding_and_exit_failure(self):
        value = self.run_powershell(r"""
function sc.exe { $script:Forwarded = @($args); $global:LASTEXITCODE = 0 }
Invoke-Sc @('create', 'sample-service', 'binPath=', 'sample artifact.exe')
$Success = $Forwarded
function sc.exe { $global:LASTEXITCODE = 5 }
$Rejected = $false
try { Invoke-Sc @('delete', 'sample-service') } catch { $Rejected = $_.Exception.Message.Contains('sc.exe failed (5)') }
@{ forwarded = @($Success); rejected = $Rejected } | ConvertTo-Json -Compress
""")
        self.assertEqual(value["forwarded"], ["create", "sample-service", "binPath=", "sample artifact.exe"])
        self.assertTrue(value["rejected"])

    def test_service_query_failure_is_not_absence(self):
        value = self.run_powershell(r"""
function Get-CimInstance { param($ClassName, $Filter, $OperationTimeoutSec, $ErrorAction) throw 'CIM unavailable' }
function Get-ServiceRegistration { return $null }
$QueryRejected = $false
$WaitRejected = $false
try { [void](Get-ServiceObject) } catch { $QueryRejected = $_.Exception.Message -eq 'CIM unavailable' }
try { Wait-ServiceAbsent -Seconds 0 } catch { $WaitRejected = $_.Exception.Message -eq 'CIM unavailable' }
@{ query = $QueryRejected; wait = $WaitRejected } | ConvertTo-Json -Compress
""")
        self.assertEqual(value, {"query": True, "wait": True})

    def test_service_identity_rejects_account_path_and_install_id_changes(self):
        value = self.run_powershell(r"""
Seed-Staging
$script:Service = [pscustomobject]@{ PathName = $ImagePath; StartName = 'NT AUTHORITY\LocalService'; Description = ('AssetLibrary M0-004 Spike ' + $InstallId) }
function Get-ServiceObject { return $script:Service }
function Get-ServiceRegistration { [pscustomobject]@{ AssetLibrarySpikeOwner = $OwnerMarker; ImagePath = $ImagePath } }
[void](Assert-OwnedService)
$Rejected = 0
foreach ($Property in @('PathName', 'StartName', 'Description')) {
    $Original = $Service.$Property
    $Service.$Property = 'unowned'
    try { [void](Assert-OwnedService) } catch { $Rejected++ }
    $Service.$Property = $Original
}
@{ rejected = $Rejected } | ConvertTo-Json -Compress
""")
        self.assertEqual(value["rejected"], 3)

    def test_cleanup_retains_nonempty_data_and_is_reentrant(self):
        value = self.run_powershell(r"""
Seed-Staging
Set-Content -LiteralPath (Join-Path $Data 'evidence.txt') -Value 'keep this data'
function Get-ServiceObject { return $null }
function Get-ServiceRegistration { return $null }
Remove-OwnedStaging
Remove-OwnedStaging
@{ app = (Test-Path -LiteralPath $StageApp); marker = (Test-Path -LiteralPath $Marker); data = (Get-Content -LiteralPath (Join-Path $Data 'evidence.txt')) } | ConvertTo-Json -Compress
""")
        self.assertFalse(value["app"])
        self.assertTrue(value["marker"])
        self.assertEqual(value["data"], "keep this data")

    def test_cleanup_refuses_live_service_or_escaping_path(self):
        value = self.run_powershell(r"""
Seed-Staging
function Get-ServiceObject { [pscustomobject]@{ State = 'Running' } }
function Get-ServiceRegistration { return $null }
$LiveRejected = $false
try { Remove-OwnedStaging } catch { $LiveRejected = $_.Exception.Message.Contains('while service exists') }
$Preserved = Test-Path -LiteralPath $StageExe
$StageApp = Join-Path $StageParent 'outside-owned-root'
$EscapeRejected = $false
try { Assert-StagingPaths } catch { $EscapeRejected = $_.Exception.Message.Contains('escapes') }
@{ live = $LiveRejected; preserved = $Preserved; escape = $EscapeRejected } | ConvertTo-Json -Compress
""")
        self.assertEqual(value, {"live": True, "preserved": True, "escape": True})

    def test_failed_service_delete_retains_staging_during_install_rollback(self):
        value = self.run_powershell(r"""
$script:ServiceLive = $false
function Get-ServiceObject {
    if ($script:ServiceLive) { [pscustomobject]@{ PathName = $ImagePath; StartName = 'NT AUTHORITY\LocalService' } }
}
function Get-ServiceRegistration { return $null }
function Assert-PortAvailable { }
function Invoke-Icacls { }
function New-ItemProperty { }
function sc.exe {
    if ($args[0] -eq 'create') { $script:ServiceLive = $true; $global:LASTEXITCODE = 0 }
    else { $global:LASTEXITCODE = 5 }
}
$Rejected = $false
try { Install-Service } catch { $Rejected = $_.Exception.Message.Contains('cleanup incomplete; staging was retained') }
@{ rejected = $Rejected; live = $script:ServiceLive; app = (Test-Path -LiteralPath $StageExe); data = (Test-Path -LiteralPath $Data); marker = (Test-Path -LiteralPath $Marker) } | ConvertTo-Json -Compress
""")
        self.assertTrue(all(value.values()), value)


if __name__ == "__main__":
    unittest.main()
