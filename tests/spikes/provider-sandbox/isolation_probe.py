"""Executed host capability probes; a manifest flag is never treated as proof."""
from __future__ import annotations

import os
import shutil
import subprocess
from typing import Any


def run_probe(command: list[str], timeout: float = 2.0) -> dict[str, Any]:
    try:
        completed = subprocess.run(command, capture_output=True, text=True, timeout=timeout, check=False)
        return {"command": command, "available": True, "returncode": completed.returncode, "stdout": completed.stdout[:256], "stderr": completed.stderr[:256]}
    except FileNotFoundError:
        return {"command": command, "available": False, "returncode": None, "stdout": "", "stderr": "not installed"}
    except subprocess.TimeoutExpired:
        return {"command": command, "available": True, "returncode": None, "stdout": "", "stderr": "timeout"}


def linux_isolation_evidence() -> dict[str, Any]:
    result: dict[str, Any] = {
        "cgroup_root_writable": os.access("/sys/fs/cgroup", os.W_OK),
        "unshare_network": run_probe(["unshare", "-n", "--", "/bin/true"]),
        "bubblewrap_network": run_probe(["bwrap", "--unshare-net", "--ro-bind", "/", "/", "/bin/true"]),
    }
    result["enforced_network_namespace"] = bool(result["unshare_network"].get("returncode") == 0 or result["bubblewrap_network"].get("returncode") == 0)
    result["filesystem_confinement_proven"] = False
    return result


def windows_candidate_evidence() -> dict[str, Any]:
    return {"executed": False, "available": shutil.which("powershell") is not None, "reason": "No Windows host or PowerShell executor in M0-008 environment"}
