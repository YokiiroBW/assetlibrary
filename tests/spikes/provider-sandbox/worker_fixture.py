"""Opaque, bounded fault fixtures. Never run against real assets or network."""
from __future__ import annotations

import json
import os
import resource
import subprocess
import sys
import time
from pathlib import Path

from protocol import encode_frame, read_frame


def send(value: dict[str, object]) -> None:
    sys.stdout.buffer.write(encode_frame(value, 64 * 1024))
    sys.stdout.buffer.flush()


def main() -> int:
    mode = sys.argv[1] if len(sys.argv) > 1 else "happy"
    request = read_frame(sys.stdin.buffer, 64 * 1024)
    request_id = request.get("request_id", "unknown")
    if mode == "happy":
        send({"message_type": "result", "rpc_version": "1.0", "request_id": request_id, "status": "ok", "asset_level": "L1", "original_write": False, "metadata": {"fixture": True}, "future_optional": {"kept": True}})
    elif mode == "cancel" or mode == "hang" or mode == "child":
        if mode == "child":
            child = subprocess.Popen([sys.executable, "-c", "import time; time.sleep(30)"], close_fds=True)
            Path(sys.argv[2]).write_text(str(child.pid), encoding="utf-8")
        time.sleep(30)
    elif mode == "crash":
        os._exit(23)
    elif mode == "malformed":
        sys.stdout.buffer.write(b"\x00\x00\x00\x04oops")
        sys.stdout.buffer.flush()
    elif mode == "oversized":
        sys.stdout.buffer.write((2 * 1024 * 1024).to_bytes(4, "big"))
        sys.stdout.buffer.flush()
        time.sleep(2)
    elif mode == "stdout_flood":
        sys.stdout.buffer.write(b"F" * (2 * 1024 * 1024))
        sys.stdout.buffer.flush()
    elif mode == "stderr_flood":
        sys.stderr.buffer.write(b"E" * (2 * 1024 * 1024))
        sys.stderr.buffer.flush()
        time.sleep(30)
    elif mode == "memory":
        blocks = []
        try:
            while True:
                blocks.append(bytearray(1024 * 1024))
        except MemoryError:
            send({"message_type": "error", "rpc_version": "1.0", "request_id": request_id, "code": "memory_limit", "retryable": False})
    elif mode == "cpu":
        while True:
            pass
    elif mode == "fd_exhaust":
        opened = []
        try:
            while True:
                opened.append(open("/dev/null"))
        except OSError as exc:
            if exc.errno != 24:
                raise
            send({"message_type": "error", "rpc_version": "1.0", "request_id": request_id, "code": "descriptor_limit", "retryable": False, "detail": str(len(opened))})
    elif mode == "process_exhaust":
        children = []
        try:
            for _ in range(64):
                children.append(subprocess.Popen([sys.executable, "-c", "import time; time.sleep(3)"], close_fds=True))
        except OSError as exc:
            send({"message_type": "error", "rpc_version": "1.0", "request_id": request_id, "code": "process_limit", "retryable": False, "detail": str(len(children))})
        finally:
            for child in children:
                child.terminate()
            for child in children:
                child.wait()
    else:
        send({"message_type": "error", "rpc_version": "1.0", "request_id": request_id, "code": "unknown_fixture", "retryable": False})
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
