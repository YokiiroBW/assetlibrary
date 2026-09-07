"""Windows worker lifetime regression; deliberately kills only its owned parent process."""
from __future__ import annotations

import ctypes
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import unittest
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
FIXTURES = Path(__file__).resolve().parent


@unittest.skipUnless(os.name == "nt", "Windows Job Object evidence is Windows-specific")
class WindowsWorkerLifetimeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.dotnet = os.environ.get("ASSETLIBRARY_TEST_DOTNET")
        if not cls.dotnet:
            raise AssertionError("ASSETLIBRARY_TEST_DOTNET must select the pinned SDK/runtime")
        cls.core = ROOT / "services/core-server/bin/Release/net10.0/AssetLibrary.CoreServer.dll"
        if not cls.core.is_file():
            raise AssertionError("build the current Release core before this regression")
        cls.runtime = tempfile.TemporaryDirectory(prefix="al-worker-lifetime-")
        cls.addClassCleanup(cls.runtime.cleanup)
        runtime = Path(cls.runtime.name)
        (runtime / "Entry.cs").write_text("using AssetLibrary.ReadCore.Tests; return await WorkerParentFixture.RunAsync(args);\n", encoding="utf-8")
        project = runtime / "WorkerParentFixture.csproj"
        project.write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnableNETAnalyzers>true</EnableNETAnalyzers><AnalysisLevel>latest-recommended</AnalysisLevel>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="{escape(str(runtime / 'Entry.cs'))}" />
    <Compile Include="{escape(str(FIXTURES / 'WorkerParentFixture.cs'))}" />
    <Reference Include="AssetLibrary.CoreServer"><HintPath>{escape(str(cls.core))}</HintPath></Reference>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>
</Project>
''', encoding="utf-8")
        # No PackageReference, new production project, root lock, or runtime dependency.
        result = subprocess.run([cls.dotnet, "build", str(project), "--configuration", "Release"],
                                cwd=ROOT, capture_output=True, text=True, encoding="utf-8", timeout=60)
        if result.returncode:
            raise AssertionError(result.stdout + result.stderr)
        cls.fixture = runtime / "bin/Release/net10.0/WorkerParentFixture.dll"
        cls.kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        cls.kernel.OpenProcess.argtypes = [ctypes.c_uint32, ctypes.c_int, ctypes.c_uint32]
        cls.kernel.OpenProcess.restype = ctypes.c_void_p
        cls.kernel.WaitForSingleObject.argtypes = [ctypes.c_void_p, ctypes.c_uint32]
        cls.kernel.WaitForSingleObject.restype = ctypes.c_uint32
        cls.kernel.TerminateProcess.argtypes = [ctypes.c_void_p, ctypes.c_uint32]
        cls.kernel.TerminateProcess.restype = ctypes.c_int
        cls.kernel.CloseHandle.argtypes = [ctypes.c_void_p]
        cls.kernel.CloseHandle.restype = ctypes.c_int

    def test_hard_parent_death_terminates_a_worker_blocked_after_receiving_its_request(self) -> None:
        state = Path(self.runtime.name) / "hard-death"
        state.mkdir()
        parent = subprocess.Popen([self.dotnet, str(self.fixture), sys.executable,
                                   str(FIXTURES / "worker_faults.py"), str(state)],
                                  cwd=ROOT, stdin=subprocess.DEVNULL, stdout=subprocess.PIPE,
                                  stderr=subprocess.PIPE, creationflags=subprocess.CREATE_NO_WINDOW)
        child_handle = None
        try:
            ready = state / "child.pid.ready"
            deadline = time.monotonic() + 10
            while not ready.is_file() and parent.poll() is None and time.monotonic() < deadline:
                time.sleep(0.01)
            self.assertTrue(ready.is_file(), "production transport did not deliver the request to the blocking fixture")
            self.assertIsNone(parent.poll(), "the parent must be alive before the forced termination")
            child_pid = int((state / "child.pid").read_text(encoding="ascii"))
            child_handle = self.kernel.OpenProcess(0x00100000 | 0x0001, False, child_pid)
            self.assertTrue(child_handle, "cannot retain the exact owned child process handle")
            self.assertEqual(self.kernel.WaitForSingleObject(child_handle, 0), 258)
            # Popen.kill uses TerminateProcess on Windows, not Ctrl-C and not tree termination.
            parent.kill()
            parent.wait(timeout=5)
            self.assertEqual(self.kernel.WaitForSingleObject(child_handle, 5000), 0,
                             "parent finally cannot run; the OS must terminate the blocked child through job ownership")
        finally:
            if parent.poll() is None:
                parent.kill()
                parent.wait(timeout=5)
            if child_handle:
                if self.kernel.WaitForSingleObject(child_handle, 0) == 258:
                    self.kernel.TerminateProcess(child_handle, 99)
                    self.kernel.WaitForSingleObject(child_handle, 5000)
                self.kernel.CloseHandle(child_handle)
            parent.communicate(timeout=5)


if __name__ == "__main__":
    unittest.main()
