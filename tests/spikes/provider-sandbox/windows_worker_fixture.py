"""Bounded Windows worker modes for the M0-008 isolation probe."""
from __future__ import annotations

import json
import subprocess
import sys
import time
from pathlib import Path


def write_report(path: str, value: dict[str, object]) -> None:
    Path(path).write_text(json.dumps(value, sort_keys=True), encoding="utf-8")


def sleep_mode(seconds: float) -> int:
    if seconds < 0 or seconds > 30:
        raise ValueError("sleep fixture must stay within 30 seconds")
    time.sleep(seconds)
    return 0


def descendant_mode(report_path: str) -> int:
    creation_flags = getattr(subprocess, "CREATE_NO_WINDOW", 0)
    child = subprocess.Popen(
        [sys.executable, "-B", __file__, "sleep", "30"],
        close_fds=True,
        creationflags=creation_flags,
    )
    write_report(report_path, {"child_pid": child.pid})
    time.sleep(30)
    return 0


def memory_mode(report_path: str) -> int:
    blocks: list[bytearray] = []
    try:
        for _ in range(96):
            block = bytearray(1024 * 1024)
            block[0] = 1
            block[-1] = 1
            blocks.append(block)
    except MemoryError:
        allocated = len(blocks) * 1024 * 1024
        blocks.clear()
        write_report(report_path, {"memory_error": True, "allocated_bytes": allocated})
        return 0
    allocated = len(blocks) * 1024 * 1024
    blocks.clear()
    write_report(report_path, {"memory_error": False, "allocated_bytes": allocated, "fallback_cap_reached": True})
    return 0


def cpu_mode(report_path: str, duration_seconds: float) -> int:
    if duration_seconds <= 0 or duration_seconds > 30:
        raise ValueError("CPU fixture must stay within 30 seconds")
    wall_started = time.perf_counter()
    cpu_started = time.process_time()
    iterations = 0
    while time.perf_counter() - wall_started < duration_seconds:
        iterations += 1
    write_report(
        report_path,
        {
            "wall_ms": round((time.perf_counter() - wall_started) * 1000),
            "process_cpu_ms": round((time.process_time() - cpu_started) * 1000),
            "iterations_positive": iterations > 0,
        },
    )
    return 0


def main() -> int:
    mode = sys.argv[1]
    if mode == "sleep":
        return sleep_mode(float(sys.argv[2]))
    if mode == "descendant":
        return descendant_mode(sys.argv[2])
    if mode == "memory":
        return memory_mode(sys.argv[2])
    if mode == "cpu":
        return cpu_mode(sys.argv[2], float(sys.argv[3]))
    raise ValueError(f"unknown bounded fixture mode: {mode}")


if __name__ == "__main__":
    raise SystemExit(main())
