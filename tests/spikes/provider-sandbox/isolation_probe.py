"""Executed host capability probes; a manifest flag is never treated as proof."""
from __future__ import annotations

import json
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
        "systemd_user_scope": run_probe(["systemd-run", "--user", "--scope", "--quiet", "/bin/true"]),
    }
    result["enforced_network_namespace"] = bool(result["unshare_network"].get("returncode") == 0 or result["bubblewrap_network"].get("returncode") == 0)
    result["delegated_user_cgroup_executed"] = result["systemd_user_scope"].get("returncode") == 0
    result["filesystem_confinement_proven"] = False
    return result


def windows_candidate_evidence() -> dict[str, Any]:
    if os.name != "nt":
        return {
            "executed": False,
            "available": shutil.which("powershell") is not None,
            "reason": "Windows host unavailable",
        }
    from windows_isolation_probe import collect_windows_isolation_evidence

    return collect_windows_isolation_evidence()


def main() -> int:
    evidence = windows_candidate_evidence() if os.name == "nt" else linux_isolation_evidence()
    print(json.dumps(evidence, ensure_ascii=False, indent=2, sort_keys=True))
    if os.name == "nt":
        return 0 if evidence.get("job_object_all_passed") and evidence.get("restricted_token_candidate_executed") else 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
