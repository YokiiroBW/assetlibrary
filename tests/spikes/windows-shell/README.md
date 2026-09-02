# M0-002 Windows Shell Spike

This package is a deliberately small Windows-only experiment. `AssetShellExtension.dll`
contains a COM `IShellFolder` namespace object and a custom `IShellView`; its only
out-of-process call is a local, versioned named-pipe ping to `AssetHostStub.exe`.
The shell bridge does not contain network, hashing, media decoding, Provider, or
business/domain code.

## Build on Windows 11 x64

Open a PowerShell 7 or Windows PowerShell prompt with Visual Studio 2022 (Desktop
C++ workload), Windows 11 SDK, and CMake available on `PATH`:

```powershell
./scripts/build.ps1
```

The script configures the x64 generator and builds a Release DLL and host under
the local `build/bin/Release` directory. Build output is intentionally ignored
by the repository and must not be committed.

## Current-user registration cycle

```powershell
./scripts/register.ps1
./scripts/verify-registration.ps1
./scripts/run-host.ps1 -Mode normal
# Launch Explorer.exe /e,::{9D52B2F8-9EF4-4F4C-9C1A-529F665F0A02}, then select AssetHost.
./scripts/run-host.ps1 -Mode invalid
./scripts/run-host.ps1 -Mode crash -Once
./scripts/run-host.ps1 -Mode slow -Once
./scripts/unregister.ps1
./scripts/verify-registration.ps1 # expected to fail after removal
```

Registration is limited to `HKCU`, refuses to replace a different DLL, and uses
an owner marker so unregistration cannot remove another product's CLSID. Restart
Explorer after unregistering to verify that the shell returns to its normal state.
The final `SHChangeNotify` cache refresh runs on a background thread with a
3-second join limit. A registration-side timeout is treated as failure and rolls
the registration back; after an owner-checked unregistration has already removed
the keys, a timeout produces a warning and the script exits so an Explorer window
cannot keep the cleanup process alive indefinitely. Sign out once to refresh the
shell if that warning appears.

## Failure modes

The view activation returns immediately after starting a worker. The worker's host
ping uses a 250 ms logical I/O deadline; after that deadline, cancelled overlapped
I/O is drained by the worker before worker-owned resources are released. That drain
does not block the Explorer UI thread and is not claimed to have its own proven
upper bound until the Windows gate runs. Missing, crashed, slow, malformed, or
oversized host frames result in a status message and the view remains usable; the
bridge does not retry forever. `run-host.ps1` exposes
normal, delayed, crash-after-one-request, and invalid-frame substitutes.

`soak.ps1` is a host-cycle helper only; it does not navigate Explorer. Use
`docs/spikes/M0-002/explorer-soak-protocol.md` for the manual Explorer
navigation, bounded-response, failure-recovery, and evidence-collection gate.

## Linux checks

From the repository root:

```text
PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s tests/spikes/windows-shell -p 'test_*.py' -v
git diff --check
```

The C++ build and Explorer registration are intentionally not simulated on Linux;
the exact Windows commands and the unexecuted gates are recorded in the M0-002
handoff.
