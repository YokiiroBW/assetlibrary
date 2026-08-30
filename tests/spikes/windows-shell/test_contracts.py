import pathlib
import re
import unittest


ROOT = pathlib.Path(__file__).parent
SRC = ROOT / "src"
SCRIPTS = ROOT / "scripts"
REPO_ROOT = ROOT.parents[2]


class PackageContractTests(unittest.TestCase):
    def test_expected_package_files_exist(self):
        expected = {
            "CMakeLists.txt",
            "README.md",
            "src/AssetShellProtocol.h",
            "src/AssetShellExtension.cpp",
            "src/AssetHostStub.cpp",
            "scripts/build.ps1",
            "scripts/register.ps1",
            "scripts/verify-registration.ps1",
            "scripts/unregister.ps1",
            "scripts/run-host.ps1",
            "scripts/soak.ps1",
        }
        actual = {str(path.relative_to(ROOT)) for path in ROOT.rglob("*") if path.is_file()}
        self.assertTrue(expected <= actual)

    def test_spike_does_not_reference_forbidden_shell_dependencies(self):
        source = "\n".join(path.read_text(encoding="utf-8") for path in SRC.glob("*.cpp"))
        forbidden = ("WinInet", "WinHTTP", "libcurl", "sqlite", "postgres", "openssl", "Provider")
        for token in forbidden:
            self.assertNotIn(token.lower(), source.lower(), token)

    def test_ipc_is_versioned_bounded_and_local(self):
        protocol = (SRC / "AssetShellProtocol.h").read_text(encoding="utf-8")
        self.assertRegex(protocol, r"kVersion\s*=\s*1")
        self.assertRegex(protocol, r"kMaxPayloadBytes\s*=\s*4096")
        self.assertRegex(protocol, r"kClientTimeoutMs\s*=\s*250")
        self.assertIn("kPipeName", protocol)
        self.assertIn("static_assert(sizeof(FrameHeader) == 16", protocol)
        shell = (SRC / "AssetShellExtension.cpp").read_text(encoding="utf-8")
        self.assertIn("FILE_FLAG_OVERLAPPED", shell)
        self.assertIn("WaitForSingleObject", shell)
        self.assertIn("CancelIoEx", shell)
        self.assertIn("RemainingBudget", shell)
        self.assertIn("const ULONGLONG deadline = GetTickCount64() + kClientTimeoutMs", shell)
        cancel_at = shell.index("CancelIoEx(pipe, &overlapped)")
        drain_at = shell.index("WaitForSingleObject(overlapped.hEvent, INFINITE)", cancel_at)
        close_at = shell.index("CloseHandle(overlapped.hEvent)", drain_at)
        self.assertLess(cancel_at, drain_at)
        self.assertLess(drain_at, close_at)
        self.assertIn("payload_length <= kMaxPayloadBytes", shell)

    def test_enumerator_reports_partial_multi_item_fetch(self):
        shell = (SRC / "AssetShellExtension.cpp").read_text(encoding="utf-8")
        self.assertIn("return count == 1 ? S_OK : S_FALSE", shell)

    def test_registration_is_hkcu_only_and_symmetric(self):
        register = (SCRIPTS / "register.ps1").read_text(encoding="utf-8")
        unregister = (SCRIPTS / "unregister.ps1").read_text(encoding="utf-8")
        verify = (SCRIPTS / "verify-registration.ps1").read_text(encoding="utf-8")
        for script in (register, unregister, verify):
            self.assertIn("HKCU:", script)
            self.assertNotRegex(script, r"HKLM:|HKEY_LOCAL_MACHINE|HKCR:")
        self.assertIn("AssetLibraryOwner", register)
        self.assertIn("AssetLibraryOwner", unregister)
        self.assertIn("AssetLibraryOwner", verify)
        self.assertIn("try {", register)
        self.assertIn("} catch {", register)
        self.assertIn("createdKeys", register)
        self.assertIn("namespaceOwned", register)
        self.assertIn("[array]::Reverse", register)
        self.assertIn("ShellFolder", register)
        self.assertIn("Refusing to replace", register)
        self.assertIn("Refusing to remove", unregister)
        self.assertIn("Unregistration must still work", unregister)
        self.assertIn("Remove-Item -LiteralPath $namespace", unregister)
        self.assertIn("Remove-Item -LiteralPath $classes", unregister)

    def test_failure_modes_are_exposed_by_the_host_entrypoint(self):
        host = (SRC / "AssetHostStub.cpp").read_text(encoding="utf-8")
        entrypoint = (SCRIPTS / "run-host.ps1").read_text(encoding="utf-8")
        for mode in ("--delay-ms=", "--crash-after=", "--invalid-response"):
            self.assertIn(mode, host)
        for mode in ("slow", "crash", "invalid"):
            self.assertIn("'" + mode + "'", entrypoint)

    def test_soak_entrypoint_is_explicitly_host_only(self):
        soak = (SCRIPTS / "soak.ps1").read_text(encoding="utf-8")
        protocol = (REPO_ROOT / "docs/spikes/M0-002/explorer-soak-protocol.md").read_text(encoding="utf-8")
        self.assertIn("host-cycle helper", soak)
        self.assertNotIn("shell:::{", soak)
        self.assertIn("shell:::{9D52B2F8-9EF4-4F4C-9C1A-529F665F0A02}", protocol)
        self.assertIn("Explorer soak evidence", protocol)

    def test_no_generated_or_private_artifacts_are_packaged(self):
        names = {path.name for path in ROOT.rglob("*") if path.is_file()}
        self.assertFalse({"AssetShellExtension.dll", "AssetHostStub.exe"} & names)
        self.assertFalse(any(path.suffix.lower() in {".reg", ".log", ".pdb"} for path in ROOT.rglob("*")))
        content = "\n".join(path.read_text(encoding="utf-8") for path in ROOT.rglob("*") if path.is_file() and path.suffix in {".md", ".ps1", ".py", ".h", ".cpp", ".txt"})
        self.assertNotRegex(content, r"(?i)(password|access[_ -]?token|private[_ -]?key)\s*[:=]")


if __name__ == "__main__":
    unittest.main()
