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
            "src/AssetShellExtension.def",
            "src/AssetHostStub.cpp",
            "scripts/build.ps1",
            "scripts/register.ps1",
            "scripts/verify-registration.ps1",
            "scripts/unregister.ps1",
            "scripts/run-host.ps1",
            "scripts/soak.ps1",
        }
        actual = {
            path.relative_to(ROOT).as_posix()
            for path in ROOT.rglob("*")
            if path.is_file()
        }
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
        skip = shell[shell.index("Skip(ULONG count)"):shell.index("Reset() override")]
        self.assertIn("if (returned_) return S_FALSE", skip)
        self.assertIn("return count == 1 ? S_OK : S_FALSE", skip)

    def test_shell_folder_persists_initialized_pidl(self):
        shell = (SRC / "AssetShellExtension.cpp").read_text(encoding="utf-8")
        self.assertIn("public IPersistFolder", shell)
        for method in ("GetClassID", "Initialize", "ILCloneFull", "folder_pidl_", "IID_IPersistFolder"):
            self.assertIn(method, shell)
        for ipersist_file_method in ("IsDirty() override", "SaveCompleted(", "GetCurFile("):
            self.assertNotIn(ipersist_file_method, shell)

    def test_windows_build_entrypoint_and_exports_match_sdk_contracts(self):
        shell = (SRC / "AssetShellExtension.cpp").read_text(encoding="utf-8")
        exports = (SRC / "AssetShellExtension.def").read_text(encoding="utf-8")
        build = (SCRIPTS / "build.ps1").read_text(encoding="utf-8")
        self.assertIn("SHGDN_FORPARSING", shell)
        self.assertNotIn("SHGDNF_FORPARSING", shell)
        self.assertIn("STDAPI DllGetClassObject", shell)
        self.assertIn("STDAPI DllCanUnloadNow", shell)
        self.assertNotIn("__declspec(dllexport)", shell)
        self.assertIn("DllGetClassObject PRIVATE", exports)
        self.assertIn("DllCanUnloadNow PRIVATE", exports)
        self.assertEqual(2, build.count("if ($LASTEXITCODE -ne 0)"))
        self.assertIn("CMake configure failed", build)
        self.assertIn("CMake build failed", build)

    def test_explorer_view_activation_is_async_and_factory_is_counted(self):
        shell = (SRC / "AssetShellExtension.cpp").read_text(encoding="utf-8")
        self.assertIn("StartAssetHostPing(HWND window, UINT_PTR token) noexcept", shell)
        self.assertIn("std::thread([state]()", shell)
        self.assertIn("PostMessageW(state->window", shell)
        view_body = shell[shell.index("CreateViewWindow("):shell.index("DestroyViewWindow()")]
        self.assertNotIn("AskAssetHost()", view_body)
        self.assertNotIn("WaitForSingleObject", view_body)
        refresh_body = shell[shell.index("Refresh() override"):shell.index("CreateViewWindow(")]
        self.assertIn("BeginAssetHostPing()", refresh_body)
        self.assertNotIn("AskAssetHost()", refresh_body)
        self.assertNotIn("WaitForSingleObject", refresh_body)
        ping_body = shell[shell.index("void BeginAssetHostPing()") :]
        self.assertIn("if (!window_ || host_ping_in_flight_) return", ping_body)
        self.assertIn("StartAssetHostPing(window_, host_ping_token_)", ping_body)
        self.assertIn("static_cast<UINT_PTR>(lparam) != view->host_ping_token_", shell)
        worker_body = shell[shell.index("void StartAssetHostPing("):shell.index("class AssetShellView")]
        self.assertIn("noexcept", worker_body)
        self.assertIn("catch (...)", worker_body)
        self.assertIn("enum class HostState", shell)
        self.assertIn('L"AssetHost connected"', shell)
        self.assertIn('L"AssetHost unavailable; press F5 to retry"', shell)
        worker_at = shell.index("std::thread([state]()")
        ask_at = shell.index("AskAssetHost();", worker_at)
        self.assertGreater(ask_at, worker_at)
        self.assertNotIn("const bool host_ready = AskAssetHost();", shell)
        self.assertIn("ClassFactory() : ref_count_(1) { ++g_object_count; }", shell)
        self.assertIn("~ClassFactory() { --g_object_count; }", shell)
        self.assertIn("g_server_lock_count", shell)
        self.assertIn("g_object_count == 0 && g_server_lock_count == 0", shell)
        self.assertIn("++g_object_count; // Keep the DLL loaded", shell)
        thread_body = worker_body[worker_body.index("std::thread"):worker_body.index("}).detach();")]
        self.assertIn("--g_object_count;", thread_body)

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
        self.assertIn("[array]::Reverse", register)
        self.assertIn("Created-KeyCanRollback", register)
        self.assertIn("Get-ChildItem -LiteralPath $path", register)
        self.assertIn("if (Created-KeyCanRollback $path)", register)
        self.assertNotIn("$ownedBeforeRollback", register)
        self.assertIn("originalValues", register)
        self.assertIn("existingNamespace", register)
        self.assertIn("ShellFolder", register)
        self.assertIn("Refusing to replace", register)
        self.assertIn("Refusing to remove", unregister)
        self.assertIn("Unregistration must still work", unregister)
        self.assertIn("Remove-Item -LiteralPath $namespace", unregister)
        self.assertIn("Remove-Item -LiteralPath $classes", unregister)
        self.assertIn("$namespaceRegistration", unregister)
        self.assertIn("$classRegistration", unregister)
        self.assertIn("$classOwned", unregister)
        self.assertIn("$inprocOwned", unregister)
        self.assertIn("$namespaceOwned", unregister)
        self.assertIn("$unknownChildren", unregister)

    def test_new_install_mid_failure_rolls_back_created_keys(self):
        register = (SCRIPTS / "register.ps1").read_text(encoding="utf-8")
        self.assertIn("$createdKeys", register)
        self.assertIn("$createdArray", register)
        self.assertIn("Created-KeyCanRollback", register)
        self.assertIn("# Restore values first", register)
        self.assertIn("$children.Count -ne 0", register)

    def test_registration_refuses_uac_disabled_hkcu_com_host(self):
        register = (SCRIPTS / "register.ps1").read_text(encoding="utf-8")
        self.assertIn("Registry]::LocalMachine.OpenSubKey", register)
        self.assertIn("EnableLUA=1", register)
        self.assertIn("refusing per-user registration", register)
        self.assertIn("Do not switch this Spike to HKLM", register)
        self.assertLess(register.index("EnableLUA"), register.index("Set-ItemProperty"))

    def test_registration_refreshes_shell_cache_symmetrically(self):
        register = (SCRIPTS / "register.ps1").read_text(encoding="utf-8")
        unregister = (SCRIPTS / "unregister.ps1").read_text(encoding="utf-8")
        for script in (register, unregister):
            self.assertIn("Notify-ShellAssociationChanged", script)
            self.assertIn("SHChangeNotify", script)
            self.assertIn("0x08000000", script)
            self.assertIn("SHCNE_ASSOCCHANGED", script)
            self.assertIn("SHCNF_IDLIST", script)
            self.assertIn("BoundedShellChangeNotifier", script)
            self.assertIn("thread.IsBackground = true", script)
            self.assertIn("thread.Join(timeoutMilliseconds)", script)
            self.assertIn("NotifyWithTimeout(3000)", script)
        self.assertLess(
            register.index("Set-ItemProperty -LiteralPath $namespace -Name '(default)'"),
            register.rindex("Notify-ShellAssociationChanged"),
        )
        self.assertLess(
            unregister.index("Remove-Item -LiteralPath $classes"),
            unregister.rindex("Notify-ShellAssociationChanged"),
        )
        self.assertIn("Notify-ShellAssociationChanged -Required $false", unregister)
        self.assertIn("Registry cleanup is already complete", unregister)

    def test_existing_values_are_restored_and_new_values_removed(self):
        register = (SCRIPTS / "register.ps1").read_text(encoding="utf-8")
        self.assertIn("$originalValues", register)
        self.assertIn("$missingValues", register)
        self.assertIn("foreach ($key in $missingValues.Keys)", register)
        self.assertIn("Remove-ItemProperty", register)
        self.assertIn("Set-ItemProperty -LiteralPath $parts[0]", register)

    def test_owner_path_mismatch_and_namespace_only_residue_are_negative(self):
        unregister = (SCRIPTS / "unregister.ps1").read_text(encoding="utf-8")
        self.assertIn("InprocServer32 registration with an owner/path mismatch", unregister)
        self.assertIn("Desktop namespace registration with an owner/name mismatch", unregister)
        self.assertIn("if (-not $classExists -and -not $namespaceExists)", unregister)
        self.assertIn("if ($namespaceOwned)", unregister)
        self.assertIn("CLSID tree without the expected root owner/name", unregister)
        self.assertIn("CLSID tree containing unknown child keys", unregister)

    def test_failure_modes_are_exposed_by_the_host_entrypoint(self):
        host = (SRC / "AssetHostStub.cpp").read_text(encoding="utf-8")
        entrypoint = (SCRIPTS / "run-host.ps1").read_text(encoding="utf-8")
        for mode in ("--delay-ms=", "--crash-after=", "--invalid-response"):
            self.assertIn(mode, host)
        for mode in ("slow", "crash", "invalid"):
            self.assertIn("'" + mode + "'", entrypoint)

    def test_soak_entrypoint_is_explicitly_host_only(self):
        soak = (SCRIPTS / "soak.ps1").read_text(encoding="utf-8")
        protocol = (
            REPO_ROOT / "docs/spikes/M0-002/explorer-soak-protocol.md"
        ).read_text(encoding="utf-8")
        self.assertIn("host-cycle helper", soak)
        self.assertNotIn("shell:::{", soak)
        self.assertIn("Explorer.exe /e,::{9D52B2F8-9EF4-4F4C-9C1A-529F665F0A02}", protocol)
        self.assertNotIn("shell:::{", protocol)
        self.assertIn("EnableLUA=1", protocol)
        self.assertIn("Explorer soak evidence", protocol)

    def test_no_generated_or_private_artifacts_are_packaged(self):
        names = {path.name for path in ROOT.rglob("*") if path.is_file()}
        self.assertFalse({"AssetShellExtension.dll", "AssetHostStub.exe"} & names)
        self.assertFalse(
            any(
                path.suffix.lower() in {".reg", ".log", ".pdb"}
                for path in ROOT.rglob("*")
            ),
        )
        text_suffixes = {".md", ".ps1", ".py", ".h", ".cpp", ".txt"}
        content = "\n".join(
            path.read_text(encoding="utf-8")
            for path in ROOT.rglob("*")
            if path.is_file() and path.suffix in text_suffixes
        )
        self.assertNotRegex(content, r"(?i)(password|access[_ -]?token|private[_ -]?key)\s*[:=]")


if __name__ == "__main__":
    unittest.main()
