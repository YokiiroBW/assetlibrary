"""Test-only process supervisor for M0-008.

It deliberately exposes enforcement observations so the Spike can distinguish
an executed OS limit from a manifest assertion. It must not be imported by
production code.
"""
from __future__ import annotations

import os
import queue
import re
import signal
import subprocess
import sys
import threading
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from manifest import validate_manifest
from protocol import ProtocolError, canonical_size, encode_frame, read_frame

try:
    import resource
except ModuleNotFoundError:  # Windows evidence uses the separate Job Object probe.
    resource = None

REQUEST_ID_PATTERN = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$")
OPAQUE_TOKEN_PATTERN = re.compile(r"^(?:opaque|artifact):[A-Za-z0-9._:-]{1,240}$")


@dataclass(frozen=True)
class Limits:
    max_request_bytes: int = 64 * 1024
    max_response_bytes: int = 64 * 1024
    max_artifact_bytes: int = 1024 * 1024
    request_deadline_ms: int = 500
    cpu_ms: int = 1000
    memory_bytes: int = 128 * 1024 * 1024
    processes: int = 16
    file_descriptors: int = 64


@dataclass
class Outcome:
    status: str
    detail: str = ""
    response: dict[str, Any] | None = None
    stdout_bytes: int = 0
    stderr_bytes: int = 0
    returncode: int | None = None
    elapsed_ms: int = 0


def accept_response(request: dict[str, Any], response: dict[str, Any], limits: Limits, allowed_versions: set[str]) -> None:
    """Validate the trust boundary before exposing Provider output to callers."""
    if response.get("rpc_version") != request.get("rpc_version") or response.get("rpc_version") not in allowed_versions:
        raise ProtocolError("response rpc_version mismatch")
    if response.get("request_id") != request.get("request_id"):
        raise ProtocolError("response request_id mismatch")
    if response.get("message_type") not in {"result", "error"}:
        raise ProtocolError("unexpected response message_type")
    if response["message_type"] == "error":
        if not isinstance(response.get("code"), str) or not isinstance(response.get("retryable"), bool):
            raise ProtocolError("invalid error envelope")
        return
    if response.get("status") != "ok" or response.get("original_write") is not False:
        raise ProtocolError("result is not a read-only successful result")
    artifacts = response.get("artifacts", [])
    if not isinstance(artifacts, list):
        raise ProtocolError("artifacts must be a list")
    if len(artifacts) > 64:
        raise ProtocolError("too many artifacts")
    for artifact in artifacts:
        token = artifact.get("artifact_token") if isinstance(artifact, dict) else None
        size = artifact.get("size_bytes") if isinstance(artifact, dict) else None
        digest = artifact.get("sha256") if isinstance(artifact, dict) else None
        if (not isinstance(token, str) or OPAQUE_TOKEN_PATTERN.fullmatch(token) is None
                or not isinstance(size, int) or isinstance(size, bool) or size < 0 or size > limits.max_artifact_bytes
                or not isinstance(digest, str) or len(digest) != 64 or any(char not in "0123456789abcdefABCDEF" for char in digest)):
            raise ProtocolError("invalid or oversized artifact descriptor")


def default_manifest(limits: Limits | None = None, trust_class: str = "test_only") -> dict[str, Any]:
    values = limits or Limits()
    return validate_manifest({
        "manifest_version": "1.0", "provider_id": "fixture.provider",
        "provider_version": "0.1.0", "api_versions": ["1.0"],
        "capabilities": ["metadata", "thumbnail", "preview"], "trust_class": trust_class,
        "library_scope": ["test-library"],
        "permissions": {"network": False, "original_write": False, "input_tokens_only": True, "filesystem": "supervisor_temp_only"},
        "resource_limits": values.__dict__,
    })


def _set_limits(limits: Limits, runtime_dir: Path) -> None:
    """Apply the limits available on this Linux host before worker code runs."""
    if resource is None:
        raise RuntimeError("POSIX resource limits are unavailable")
    resource.setrlimit(resource.RLIMIT_CPU, (max(1, (limits.cpu_ms + 999) // 1000), max(2, (limits.cpu_ms + 1999) // 1000)))
    resource.setrlimit(resource.RLIMIT_AS, (limits.memory_bytes, limits.memory_bytes))
    resource.setrlimit(resource.RLIMIT_NPROC, (limits.processes, limits.processes))
    resource.setrlimit(resource.RLIMIT_NOFILE, (limits.file_descriptors, limits.file_descriptors))
    resource.setrlimit(resource.RLIMIT_CORE, (0, 0))
    os.chdir(runtime_dir)


def _read_stderr(stream: Any, box: list[int], cap: int = 8192) -> None:
    while True:
        chunk = stream.read(4096)
        if not chunk:
            return
        box[0] += len(chunk)
        if box[0] > cap:
            box[0] = cap


class ProviderSupervisor:
    def __init__(self, manifest: dict[str, Any], runtime_dir: Path):
        self.manifest = validate_manifest(manifest)
        self.runtime_dir = runtime_dir.resolve()
        self.runtime_dir.mkdir(parents=True, exist_ok=True)
        self._process: subprocess.Popen[bytes] | None = None
        self._pgid: int | None = None
        self._stderr_count = [0]
        self._writer_thread: threading.Thread | None = None
        self._last_group_drained = True

    @property
    def process(self) -> subprocess.Popen[bytes] | None:
        return self._process

    def _limits(self) -> Limits:
        values = self.manifest["resource_limits"]
        return Limits(**{name: values[name] for name in Limits.__dataclass_fields__})

    def _spawn(self, mode: str) -> subprocess.Popen[bytes]:
        worker = Path(__file__).with_name("worker_fixture.py")
        env = {
            "PATH": "/usr/bin:/bin", "LANG": "C.UTF-8", "LC_ALL": "C.UTF-8",
            "PYTHONDONTWRITEBYTECODE": "1", "PYTHONHASHSEED": "0",
            "PROVIDER_RUNTIME_DIR": str(self.runtime_dir),
        }
        command = [sys.executable, str(worker), mode]
        if mode in {"child", "parent_exit_child"}:
            command.append(str(self.runtime_dir / "child.pid"))
        preexec = (lambda: (_set_limits(self._limits(), self.runtime_dir), os.setsid())) if os.name != "nt" else None
        process = subprocess.Popen(command, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                   cwd=self.runtime_dir, env=env, close_fds=True, start_new_session=False,
                                   preexec_fn=preexec)
        self._process = process
        self._pgid = os.getpgid(process.pid) if os.name != "nt" else None
        self._stderr_count = [0]
        threading.Thread(target=_read_stderr, args=(process.stderr, self._stderr_count), daemon=True).start()
        return process

    def _validate_request(self, request: dict[str, Any], deadline_ms: int | None) -> tuple[int, int]:
        if not isinstance(request, dict) or request.get("message_type") != "request":
            raise ProtocolError("request message_type must be request")
        if request.get("rpc_version") not in self.manifest["api_versions"]:
            raise ProtocolError("unsupported request rpc_version")
        request_id = request.get("request_id")
        if not isinstance(request_id, str) or REQUEST_ID_PATTERN.fullmatch(request_id) is None:
            raise ProtocolError("invalid request_id")
        operation = request.get("operation")
        if not isinstance(operation, str) or not operation or len(operation) > 128:
            raise ProtocolError("invalid operation")
        tokens = request.get("input_tokens")
        if not isinstance(tokens, list) or len(tokens) > 64 or any(not isinstance(token, str) or OPAQUE_TOKEN_PATTERN.fullmatch(token) is None for token in tokens):
            raise ProtocolError("input_tokens must be opaque tokens")
        deadline_at = request.get("deadline_at")
        if not isinstance(deadline_at, str) or not deadline_at or len(deadline_at) > 64:
            raise ProtocolError("invalid deadline_at")
        max_response = request.get("max_response_bytes")
        if not isinstance(max_response, int) or isinstance(max_response, bool) or max_response <= 0:
            raise ProtocolError("invalid request max_response_bytes")
        try:
            size = canonical_size(request)
        except (TypeError, ValueError) as exc:
            raise ProtocolError("request is not JSON encodable") from exc
        if size > self.manifest["resource_limits"]["max_request_bytes"]:
            raise ProtocolError("request exceeds max_request_bytes")
        manifest_deadline = self.manifest["resource_limits"]["request_deadline_ms"]
        if deadline_ms is not None and (not isinstance(deadline_ms, int) or isinstance(deadline_ms, bool) or deadline_ms <= 0):
            raise ProtocolError("invalid deadline override")
        return min(max_response, self.manifest["resource_limits"]["max_response_bytes"]), min(deadline_ms or manifest_deadline, manifest_deadline)

    def _terminate_tree(self, reason: str) -> None:
        process = self._process
        if process is None:
            self._last_group_drained = True
            return
        pgid = self._pgid if os.name != "nt" else None
        if pgid is not None:
            self._signal_owned_group(pgid, signal.SIGTERM)
        else:
            process.terminate()
        try:
            process.wait(timeout=0.2)
        except subprocess.TimeoutExpired:
            if pgid is not None:
                self._signal_owned_group(pgid, signal.SIGKILL)
            else:
                process.kill()
            try:
                process.wait(timeout=0.5)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=0.5)
        else:
            # A parent can exit before its descendants. Reap the whole owned
            # group even when wait() observed a clean parent exit.
            if pgid is not None:
                self._signal_owned_group(pgid, signal.SIGKILL)
        if pgid is not None:
            self._last_group_drained = self._drain_owned_group(pgid)
        else:
            self._last_group_drained = True
        self._process = process
        self._close_pipes(process)

    @staticmethod
    def _group_members(pgid: int) -> list[int]:
        members: list[int] = []
        for entry in Path("/proc").glob("[0-9]*"):
            try:
                stat = (entry / "stat").read_text(encoding="utf-8")
                fields = stat[stat.rfind(")") + 2 :].split()
                if len(fields) >= 3 and int(fields[2]) == pgid:
                    members.append(int(entry.name))
            except (FileNotFoundError, ProcessLookupError, ValueError):
                continue
        return members

    @classmethod
    def _signal_owned_group(cls, pgid: int, signum: int) -> None:
        """Signal the owned group and its observed members to cover orphan races."""
        try:
            os.killpg(pgid, signum)
        except ProcessLookupError:
            pass
        for pid in cls._group_members(pgid):
            try:
                os.kill(pid, signum)
            except ProcessLookupError:
                pass

    @classmethod
    def _drain_owned_group(cls, pgid: int, timeout: float = 0.2) -> bool:
        """Confirm the owned PGID is empty, retrying KILL for a bounded interval."""
        deadline = time.monotonic() + timeout
        while True:
            members = cls._group_members(pgid)
            if not members:
                return True
            for pid in members:
                try:
                    os.kill(pid, signal.SIGKILL)
                except ProcessLookupError:
                    pass
            if time.monotonic() >= deadline:
                return not cls._group_members(pgid)
            time.sleep(0.01)

    @staticmethod
    def _close_pipes(process: subprocess.Popen[bytes]) -> None:
        for stream in (process.stdin, process.stdout, process.stderr):
            if stream is not None:
                try:
                    stream.close()
                except OSError:
                    pass

    def _join_writer(self) -> None:
        if self._writer_thread is not None:
            self._writer_thread.join(timeout=0.2)

    def _cleanup_detail(self, detail: str) -> str:
        return detail if self._last_group_drained else f"{detail};group_drain_timeout"

    def run(self, mode: str = "happy", request: dict[str, Any] | None = None, deadline_ms: int | None = None) -> Outcome:
        request = request or {"message_type": "request", "rpc_version": "1.0", "request_id": "req-1", "operation": "metadata", "input_tokens": ["opaque:test"], "deadline_at": "monotonic", "max_response_bytes": self.manifest["resource_limits"]["max_response_bytes"]}
        try:
            response_limit, deadline_limit_ms = self._validate_request(request, deadline_ms)
        except ProtocolError as exc:
            return Outcome("rejected", str(exc))
        max_request = self.manifest["resource_limits"]["max_request_bytes"]
        try:
            frame = encode_frame(request, max_request)
        except ProtocolError as exc:
            return Outcome("rejected", str(exc))
        started = time.monotonic()
        process = self._spawn(mode)
        responses: queue.Queue[dict[str, Any] | BaseException] = queue.Queue(maxsize=1)

        def read_response() -> None:
            try:
                responses.put(read_frame(process.stdout, response_limit))
            except BaseException as exc:
                responses.put(exc)

        threading.Thread(target=read_response, daemon=True).start()
        writes: queue.Queue[BaseException | None] = queue.Queue(maxsize=1)

        def write_request() -> None:
            try:
                process.stdin.write(frame)
                process.stdin.flush()
                writes.put(None)
            except BaseException as exc:
                writes.put(exc)

        self._writer_thread = threading.Thread(target=write_request, daemon=True)
        self._writer_thread.start()
        remaining = started + deadline_limit_ms / 1000 - time.monotonic()
        if remaining <= 0:
            self._terminate_tree("deadline")
            self._join_writer()
            return Outcome("timeout", self._cleanup_detail("absolute_deadline"), stderr_bytes=self._stderr_count[0], returncode=process.returncode, elapsed_ms=round((time.monotonic() - started) * 1000))
        try:
            write_result = writes.get(timeout=remaining)
        except queue.Empty:
            self._terminate_tree("write_deadline")
            self._join_writer()
            return Outcome("timeout", self._cleanup_detail("absolute_deadline_write"), stderr_bytes=self._stderr_count[0], returncode=process.returncode, elapsed_ms=round((time.monotonic() - started) * 1000))
        if isinstance(write_result, BaseException):
            self._terminate_tree("write_failure")
            self._join_writer()
            return Outcome("crashed", self._cleanup_detail(str(write_result)), returncode=process.returncode)
        remaining = started + deadline_limit_ms / 1000 - time.monotonic()
        if remaining <= 0:
            self._terminate_tree("deadline")
            self._join_writer()
            return Outcome("timeout", self._cleanup_detail("absolute_deadline"), stderr_bytes=self._stderr_count[0], returncode=process.returncode, elapsed_ms=round((time.monotonic() - started) * 1000))
        try:
            value = responses.get(timeout=remaining)
        except queue.Empty:
            self._terminate_tree("deadline")
            self._close_pipes(process)
            return Outcome("timeout", self._cleanup_detail("absolute_deadline"), stderr_bytes=self._stderr_count[0], returncode=process.returncode, elapsed_ms=round((time.monotonic() - started) * 1000))
        finally:
            try:
                process.stdin.close()
            except OSError:
                pass
            self._join_writer()
        elapsed = round((time.monotonic() - started) * 1000)
        if isinstance(value, BaseException):
            self._terminate_tree("protocol")
            status = "protocol_error" if isinstance(value, ProtocolError) else "crashed"
            self._close_pipes(process)
            return Outcome(status, self._cleanup_detail(str(value)), stderr_bytes=self._stderr_count[0], returncode=process.returncode, elapsed_ms=elapsed)
        try:
            accept_response(request, value, self._limits(), set(self.manifest["api_versions"]))
        except ProtocolError as exc:
            self._terminate_tree("response_rejected")
            self._close_pipes(process)
            return Outcome("protocol_error", self._cleanup_detail(str(exc)), stderr_bytes=self._stderr_count[0], returncode=process.returncode, elapsed_ms=elapsed)
        cleanup_detail = ""
        try:
            process.wait(timeout=0.2)
        except subprocess.TimeoutExpired:
            self._terminate_tree("response_cleanup")
            cleanup_detail = self._cleanup_detail("response_cleanup")
        self._close_pipes(process)
        if process.returncode != 0 and not cleanup_detail:
            return Outcome("crashed", f"returncode={process.returncode}", response=value, stderr_bytes=self._stderr_count[0], returncode=process.returncode, elapsed_ms=elapsed)
        return Outcome("ok", detail=cleanup_detail, response=value, stderr_bytes=self._stderr_count[0], returncode=process.returncode, elapsed_ms=elapsed)

    def cancel(self) -> None:
        self._terminate_tree("cancel")


@dataclass
class RestartPolicy:
    max_failures: int = 3
    base_delay_ms: int = 10
    failures: int = 0
    opened_at: float | None = None

    def record(self, failed: bool, now: float | None = None) -> None:
        if not failed:
            self.failures = 0
            self.opened_at = None
            return
        self.failures += 1
        if self.failures >= self.max_failures:
            self.opened_at = time.monotonic() if now is None else now

    def can_start(self, now: float | None = None) -> bool:
        if self.opened_at is None:
            return True
        current = time.monotonic() if now is None else now
        return current - self.opened_at >= self.delay_ms() * 0.001

    def delay_ms(self) -> int:
        return self.base_delay_ms * (2 ** max(0, self.failures - 1))
