#!/usr/bin/env python3
"""Launch a private trial Host without inheriting the caller's capture pipes."""
from __future__ import annotations

import argparse
import ctypes
from ctypes import wintypes
import json
import os
from pathlib import Path
import subprocess
import sys


class TrialProcessError(RuntimeError):
    pass


def process_identity(pid: int) -> dict | None:
    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
    kernel.OpenProcess.restype = wintypes.HANDLE
    handle = kernel.OpenProcess(0x1000, False, pid)
    if not handle:
        if ctypes.get_last_error() == 87:
            return None
        raise TrialProcessError("trial_process_identity_unavailable")
    try:
        image = ctypes.create_unicode_buffer(32768)
        size = wintypes.DWORD(len(image))
        kernel.QueryFullProcessImageNameW.argtypes = [wintypes.HANDLE, wintypes.DWORD, wintypes.LPWSTR, ctypes.POINTER(wintypes.DWORD)]
        if not kernel.QueryFullProcessImageNameW(handle, 0, image, ctypes.byref(size)):
            raise TrialProcessError("trial_process_image_unavailable")
        times = [wintypes.FILETIME() for _ in range(4)]
        kernel.GetProcessTimes.argtypes = [wintypes.HANDLE, *([ctypes.POINTER(wintypes.FILETIME)] * 4)]
        if not kernel.GetProcessTimes(handle, *(ctypes.byref(value) for value in times)):
            raise TrialProcessError("trial_process_start_unavailable")
        created = (times[0].dwHighDateTime << 32) | times[0].dwLowDateTime
        return {"pid": pid, "image": str(Path(image.value)), "created": created}
    finally:
        kernel.CloseHandle.argtypes = [wintypes.HANDLE]
        kernel.CloseHandle(handle)


def launch(state: Path) -> dict:
    package = Path(__file__).resolve().parent
    if not state.is_absolute() or str(state).startswith("\\\\"):
        raise TrialProcessError("trial_process_state_invalid")
    owner_path = state / "trial-owner.json"
    if owner_path.stat().st_size > 65536:
        raise TrialProcessError("trial_process_owner_invalid")
    owner = json.loads(owner_path.read_text(encoding="utf-8-sig"))
    if (owner.get("product") != "AssetLibrary/read-only-trial" or owner.get("state_path") != str(state)
            or owner.get("package_root") != str(package)):
        raise TrialProcessError("trial_process_owner_mismatch")
    executable = package / "host/AssetLibrary.CoreServer.Host.exe"
    configuration = state / "trial.json"
    startup = subprocess.STARTUPINFO()
    startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW
    startup.wShowWindow = subprocess.SW_HIDE
    environment = os.environ.copy()
    environment.update({"ASPNETCORE_ENVIRONMENT": "Production", "DOTNET_ENVIRONMENT": "Production"})
    with (state / "logs/host.stdout.log").open("ab") as stdout, (state / "logs/host.stderr.log").open("ab") as stderr:
        process = subprocess.Popen(
            [str(executable), "--read-only-trial", str(configuration)], cwd=package,
            stdin=subprocess.DEVNULL, stdout=stdout, stderr=stderr, env=environment,
            close_fds=True, creationflags=subprocess.CREATE_NO_WINDOW, startupinfo=startup,
        )
    identity = process_identity(process.pid)
    if identity is None or Path(identity["image"]) != executable:
        if process.poll() is None:
            process.terminate()
            process.wait(timeout=10)
        raise TrialProcessError("trial_process_start_failed")
    return identity


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--state", required=True, type=Path)
    args = parser.parse_args()
    if os.name != "nt" or not sys.flags.isolated:
        print('{"status":"failed","code":"trial_windows_isolated_python_required"}')
        return 1
    try:
        print(json.dumps(launch(args.state), sort_keys=True))
        return 0
    except Exception:
        print('{"status":"failed","code":"trial_process_start_failed"}')
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
