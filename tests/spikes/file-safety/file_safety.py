"""M0-006 test-only physical file operation spike (Python 3.12 stdlib).

The journal is coordination only: every recovery decision re-reads the physical
files and verifies their SHA-256.  This module deliberately is not a production
filesystem abstraction.
"""
from __future__ import annotations

import ctypes
import ctypes.util
import errno
import fcntl
import hashlib
import json
import os
import secrets
import shutil
import stat
import time
from contextlib import contextmanager
from dataclasses import asdict, dataclass
from enum import StrEnum
from pathlib import Path
from typing import Callable

CHUNK = 1024 * 1024
SCHEMA = "m0-006.operation.v1"
TRASH_SCHEMA = "m0-006.trash.v1"


class State(StrEnum):
    PREFLIGHT = "preflight"
    STAGED = "staged"
    VERIFIED = "verified"
    TARGET_COMMITTED = "target_committed"
    SOURCE_TRASHED = "source_trashed"
    COMPLETE = "complete"
    CANCELLED = "cancelled"
    CONFLICT = "conflict"
    MANUAL = "manual_attention"


class Failure(ValueError):
    pass


class Conflict(Failure):
    pass


class UnsupportedPrimitive(Failure):
    pass


@dataclass(frozen=True)
class Identity:
    size: int
    mtime_ns: int
    device: int
    inode: int
    sha256: str


@dataclass(frozen=True)
class Policy:
    protected_sources: frozenset[str] = frozenset()
    protected_targets: frozenset[str] = frozenset()
    permission_denied: bool = False
    insufficient_space: bool = False
    cancel: bool = False


def hash_file(path: Path, chunk_size: int = CHUNK, progress: Callable[[], None] | None = None) -> tuple[str, int]:
    digest = hashlib.sha256()
    total = 0
    with path.open("rb") as stream:
        while True:
            block = stream.read(chunk_size)
            if not block:
                break
            digest.update(block)
            total += len(block)
            if progress:
                progress()
    return digest.hexdigest(), total


def same_device(source: Path, target_parent: Path) -> bool:
    return source.stat().st_dev == target_parent.stat().st_dev


def _validate_op_id(operation_id: str) -> str:
    if not isinstance(operation_id, str) or not operation_id or Path(operation_id).name != operation_id:
        raise Failure("operation id must be a single safe basename")
    if operation_id in (".", "..") or any(part in ("", ".", "..") for part in Path(operation_id).parts):
        raise Failure("unsafe operation id")
    return operation_id


def _assert_internal_path(root: Path, path: Path) -> None:
    try:
        relative = path.relative_to(root)
    except ValueError as exc:
        raise Failure("internal path escapes configured root") from exc
    cursor = root
    for part in relative.parts:
        cursor = cursor / part
        if cursor.is_symlink():
            raise Failure("internal path contains symlink")
    resolved = path.resolve(strict=False)
    if resolved != root and root not in resolved.parents:
        raise Failure("internal path escapes configured root")


def identity(path: Path, progress: Callable[[], None] | None = None) -> Identity:
    before = path.stat()
    digest, size = hash_file(path, progress=progress)
    after = path.stat()
    # A mounted NAS may quantize consecutive stat reads by one tick while the
    # file is unchanged; identity/size and a bounded mtime delta still detect
    # real mutation without making the physical adapter unusable on NAS.
    if (before.st_size, before.st_ino, before.st_dev) != (
        after.st_size, after.st_ino, after.st_dev
    ) or abs(before.st_mtime_ns - after.st_mtime_ns) > 5_000_000_000 or size != before.st_size:
        raise Failure("source changed while hashing")
    if not stat.S_ISREG(after.st_mode):
        raise Failure("source is not a regular file")
    return Identity(size, after.st_mtime_ns, after.st_dev, after.st_ino, digest)


def _fsync_dir(path: Path) -> None:
    try:
        fd = os.open(path, os.O_RDONLY | getattr(os, "O_DIRECTORY", 0))
    except OSError as exc:
        raise UnsupportedPrimitive(f"directory fsync open failed: {path}") from exc
    try:
        os.fsync(fd)
    except OSError as exc:
        raise UnsupportedPrimitive(f"directory fsync failed: {path}") from exc
    finally:
        os.close(fd)


def _durable_json(path: Path, value: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_name(f".{path.name}.{os.getpid()}.{secrets.token_hex(4)}.tmp")
    with tmp.open("w", encoding="utf-8") as stream:
        json.dump(value, stream, sort_keys=True, separators=(",", ":"))
        stream.write("\n")
        stream.flush()
        os.fsync(stream.fileno())
    os.replace(tmp, path)
    _fsync_dir(path.parent)


def _rename_noreplace(source: Path, target: Path, *, source_root: Path | None = None,
                      target_root: Path | None = None) -> None:
    """Linux renameat2(RENAME_NOREPLACE), with a fail-closed fallback error."""
    libc_name = ctypes.util.find_library("c")
    if not libc_name:
        raise UnsupportedPrimitive("libc unavailable: renameat2 required")
    libc = ctypes.CDLL(libc_name, use_errno=True)
    fn = getattr(libc, "renameat2", None)
    if fn is None:
        raise UnsupportedPrimitive("renameat2 unavailable")
    fn.argtypes = [ctypes.c_int, ctypes.c_char_p, ctypes.c_int, ctypes.c_char_p, ctypes.c_uint]
    fn.restype = ctypes.c_int
    flags = getattr(os, "O_DIRECTORY", 0) | getattr(os, "O_NOFOLLOW", 0)

    def open_pinned(directory: Path, anchor: Path | None) -> int:
        # Walk every component from the configured root anchor. Each component
        # is opened with O_NOFOLLOW and the resulting directory fd is retained,
        # so an ancestor exchanged after containment validation cannot redirect
        # the rename into an external tree.
        fd = os.open(os.sep, os.O_RDONLY | flags)
        try:
            if anchor is not None:
                relative = directory.relative_to(anchor)
                components = anchor.parts[1:] + relative.parts
            else:
                components = directory.parts[1:]
            for part in components:
                next_fd = os.open(part, os.O_RDONLY | flags, dir_fd=fd)
                os.close(fd)
                fd = next_fd
            return fd
        except Exception:
            os.close(fd)
            raise

    source_fd = target_fd = -1
    try:
        source_fd = open_pinned(source.parent, source_root)
        target_fd = open_pinned(target.parent, target_root)
        result = fn(source_fd, os.fsencode(source.name), target_fd, os.fsencode(target.name), 1)
        if result != 0:
            code = ctypes.get_errno()
            if code == errno.EEXIST:
                raise Conflict("target exists")
            if code in (errno.ENOSYS, errno.EINVAL, errno.ENOTSUP):
                raise UnsupportedPrimitive(f"renameat2(RENAME_NOREPLACE) unsupported: {code}")
            raise OSError(code, os.strerror(code), str(source))
        # The parent directories are pinned by these fds.  Fsync them before
        # closing, rather than reopening mutable path names after a rename.
        try:
            os.fsync(source_fd)
            if target_fd != source_fd:
                os.fsync(target_fd)
        except OSError as exc:
            raise UnsupportedPrimitive("directory fsync after rename failed") from exc
    finally:
        if source_fd >= 0: os.close(source_fd)
        if target_fd >= 0: os.close(target_fd)


class Journal:
    def __init__(self, path: Path, operation_id: str | None = None):
        self.path = path
        self.operation_id = operation_id

    def read(self) -> dict:
        value = json.loads(self.path.read_text(encoding="utf-8"))
        if value.get("schema_version") != SCHEMA:
            raise Failure("unknown journal schema")
        if value.get("state") not in {state.value for state in State}:
            raise Failure("unknown or missing journal state")
        if self.operation_id is not None and value.get("operation_id") != self.operation_id:
            raise Failure("journal operation identity mismatch")
        return value

    def write(self, state: State, **extra: object) -> dict:
        current = {}
        if self.path.exists():
            current = self.read()
        current.update(state=state.value, sequence=int(current.get("sequence", 0)) + 1, **extra)
        current.setdefault("schema_version", SCHEMA)
        _durable_json(self.path, current)
        return current


class TaskLock:
    """O_EXCL ownership lock; expiry never permits direct mutation."""
    def __init__(self, path: Path, operation_id: str, owner: str, ttl: float = 30.0):
        _validate_op_id(operation_id)
        self.path, self.operation_id, self.owner, self.ttl = path, operation_id, owner, ttl
        self.token: str | None = None
        self.generation: int | None = None

    @contextmanager
    def _guard(self):
        self.path.parent.mkdir(parents=True, exist_ok=True)
        guard = self.path.with_name(".m006-lock-guard")
        fd = os.open(guard, os.O_RDWR | os.O_CREAT, 0o600)
        try:
            fcntl.flock(fd, fcntl.LOCK_EX)
            yield
        finally:
            fcntl.flock(fd, fcntl.LOCK_UN)
            os.close(fd)

    def _claim_unlocked(self, now: float, generation: int = 1) -> str:
        token = secrets.token_hex(16)
        value = {"schema_version": "m0-006.lock.v1", "operation_id": self.operation_id,
                 "owner": self.owner, "generation": generation, "token": token,
                 "heartbeat": now, "expires": now + self.ttl}
        fd = os.open(self.path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        with os.fdopen(fd, "w", encoding="utf-8") as stream:
            json.dump(value, stream, sort_keys=True)
            stream.flush()
            os.fsync(stream.fileno())
        _fsync_dir(self.path.parent)
        self.token = token
        self.generation = generation
        return "claimed"

    def bind_existing(self) -> None:
        """Bind a recovery instance to the exact current lock generation."""
        with self._guard():
            try:
                current = json.loads(self.path.read_text(encoding="utf-8"))
            except (OSError, ValueError) as exc:
                raise Failure("lock unreadable; reconcile required") from exc
            if (current.get("operation_id") != self.operation_id
                    or current.get("owner") != self.owner
                    or not current.get("token")):
                raise Failure("lock ownership mismatch")
            self.token = current["token"]
            self.generation = int(current.get("generation", 0))

    def claim(self, now: float | None = None) -> str:
        now = time.time() if now is None else now
        with self._guard():
            try:
                return self._claim_unlocked(now)
            except FileExistsError:
                try:
                    existing = json.loads(self.path.read_text(encoding="utf-8"))
                except (OSError, ValueError) as exc:
                    raise Failure("lock unreadable; reconcile required") from exc
                if existing.get("operation_id") != self.operation_id:
                    raise Failure("foreign operation lock")
                if existing.get("owner") != self.owner:
                    raise Failure("foreign owner lock")
                if float(existing.get("expires", 0)) >= now:
                    raise Failure("unexpired lock")
                raise Failure("expired lock requires physical reconciliation")

    def heartbeat(self, now: float | None = None) -> None:
        now = time.time() if now is None else now
        with self._guard():
            if not self.path.exists():
                raise Failure("lock missing")
            current = json.loads(self.path.read_text(encoding="utf-8"))
            if (current.get("operation_id") != self.operation_id
                    or current.get("owner") != self.owner
                    or self.token is None
                    or current.get("token") != self.token
                    or int(current.get("generation", 0)) != self.generation):
                raise Failure("lock ownership mismatch")
            current.update(heartbeat=now, expires=now + self.ttl)
            _durable_json(self.path, current)

    def reconcile_and_reclaim(self, physical_check: Callable[[str], dict[str, object]], now: float | None = None) -> None:
        if not callable(physical_check):
            raise TypeError("physical_check callback required")
        observation = physical_check(self.operation_id)
        required = {"source", "target", "stage", "source_trash", "replacement_trash"}
        if (observation.get("operation_id") != self.operation_id
                or observation.get("physical_reconciled") is not True
                or set(observation).intersection(required) != required):
            raise Failure("reconcile callback did not inspect this operation")
        now = time.time() if now is None else now
        with self._guard():
            if self.path.exists():
                current = json.loads(self.path.read_text(encoding="utf-8"))
                if float(current.get("expires", 0)) >= now:
                    raise Failure("unexpired lock")
                if current.get("operation_id") != self.operation_id:
                    raise Failure("different operation lock")
                self.path.unlink()
                _fsync_dir(self.path.parent)
                generation = int(current.get("generation", 0)) + 1
            else:
                generation = 1
            self._claim_unlocked(now, generation)

    def release(self) -> None:
        with self._guard():
            if self.path.exists():
                current = json.loads(self.path.read_text(encoding="utf-8"))
                if (current.get("operation_id") != self.operation_id
                        or current.get("owner") != self.owner
                        or self.token is None
                        or current.get("token") != self.token
                        or int(current.get("generation", 0)) != self.generation):
                    raise Failure("lock ownership mismatch")
                self.path.unlink()
                _fsync_dir(self.path.parent)


def _heartbeat_pulse(lock: TaskLock) -> Callable[[], None]:
    """Throttle durable lease updates while streaming or hashing."""
    last = [0.0]
    interval = max(min(lock.ttl / 3.0, 5.0), 0.001)

    def pulse() -> None:
        now = time.monotonic()
        if not last[0] or now - last[0] >= interval:
            lock.heartbeat()
            last[0] = now

    return pulse


class FileSafety:
    """Bounded test-only move state machine rooted in explicit physical roots."""
    def __init__(self, source_root: str | Path, target_root: str | Path, runtime: str | Path):
        self.source_root = Path(source_root).resolve()
        self.target_root = Path(target_root).resolve()
        self.runtime = Path(runtime).resolve()
        self.runtime.mkdir(parents=True, exist_ok=True)
        self.ops = self.runtime / "operations"
        self.ops.mkdir(exist_ok=True)
        self.source_trash = self.source_root / ".m006-trash"
        self.target_trash = self.target_root / ".m006-trash"
        self.source_trash.mkdir(exist_ok=True)
        self.target_trash.mkdir(exist_ok=True)
        self.hooks: Callable[[str, dict], None] | None = None
        self.last_evidence: dict[str, object] = {
            "same_device_primitive": "renameat2(RENAME_NOREPLACE)",
            "cross_device_primitive": "renameat2(RENAME_NOREPLACE)",
            "file_fsync": "os.fsync(file)", "directory_fsync": "os.fsync(O_DIRECTORY)",
        }

    def _path(self, root: Path, relative: str) -> Path:
        candidate = Path(relative)
        if candidate.is_absolute() or "" in candidate.parts or any(p == ".." for p in candidate.parts):
            raise Failure("absolute or traversal path")
        if not candidate.parts:
            raise Failure("empty relative path")
        # Reject lexical symlink components, including a symlink final target.
        cursor = root
        for part in candidate.parts:
            cursor = cursor / part
            if cursor.is_symlink():
                raise Failure("symlink escape")
        resolved = (root / candidate).resolve(strict=False)
        if resolved != root and root not in resolved.parents:
            raise Failure("path escapes root")
        return candidate if candidate.is_absolute() else root / candidate

    def _hook(self, name: str, **extra: object) -> None:
        if self.hooks:
            self.hooks(name, extra)

    def _journal(self, op_id: str) -> Journal:
        _validate_op_id(op_id)
        return Journal(self.ops / op_id / "journal.json", op_id)

    def _physical_paths(self, op_id: str, journal: dict) -> dict[str, Path]:
        """Derive every path from validated relative fields and configured roots."""
        _validate_op_id(op_id)
        source = self._path(self.source_root, journal["source_relative"])
        target = self._path(self.target_root, journal["target_relative"])
        stage = self.target_root / ".m006-stage" / op_id / "payload"
        source_trash = self.source_root / ".m006-trash" / op_id / f"source-{Path(journal['source_relative']).name}"
        replacement_trash = self.target_root / ".m006-trash" / op_id / f"replacement-{Path(journal['target_relative']).name}"
        expected = {"source": source, "target": target, "stage": stage,
                    "source_trash": source_trash, "replacement_trash": replacement_trash}
        for root, derived in ((self.target_root, stage), (self.source_root, source_trash),
                              (self.target_root, replacement_trash)):
            _assert_internal_path(root, derived)
        for key, derived in expected.items():
            recorded = journal.get(key)
            if recorded and Path(recorded).resolve(strict=False) != derived.resolve(strict=False):
                raise Failure(f"journal {key} path mismatch")
        return expected

    def _new(self, op_id: str, source_rel: str, target_rel: str, source: Path, target: Path,
             source_id: Identity, replace: bool, owner: str,
             replacement_id: Identity | None = None) -> Journal:
        _validate_op_id(op_id)
        directory = self.ops / op_id
        directory.mkdir(parents=True, exist_ok=False)
        _fsync_dir(directory.parent)
        journal = Journal(directory / "journal.json", op_id)
        journal.write(State.PREFLIGHT, operation_id=op_id, owner=owner,
                      source_relative=source_rel, target_relative=target_rel,
                      source=str(source), target=str(target), source_identity=asdict(source_id),
                      replace=replace, replacement_required=replacement_id is not None,
                      replacement_identity=asdict(replacement_id) if replacement_id else None,
                      same_device=source.stat().st_dev == target.parent.stat().st_dev,
                      stage=str(self.target_root / ".m006-stage" / op_id / "payload"),
                      source_trash="", replacement_trash="")
        self._hook("after_preflight_journal", journal=journal.path)
        return journal

    def _trash_record(self, path: Path, original_relative: str, op_id: str, reason: str,
                      ident: Identity, *, role: str, hook: str | None = None,
                      progress: Callable[[], None] | None = None) -> Path:
        root = self.source_root if role == "source" else self.target_root
        trash_dir = root / ".m006-trash" / op_id
        trash_path = trash_dir / f"{role}-{Path(original_relative).name}"
        _assert_internal_path(root, trash_path)
        trash_dir.mkdir(parents=True, exist_ok=True)
        _fsync_dir(trash_dir.parent)
        _fsync_dir(trash_dir)
        record = {"schema_version": TRASH_SCHEMA, "operation_id": op_id, "reason": reason,
                  "root_role": role, "library_id": "m006-test-library",
                  "original_relative_path": original_relative,
                  "trash_relative_path": str(trash_path.relative_to(root)),
                  "size": ident.size, "sha256": ident.sha256}
        metadata = trash_path.with_suffix(trash_path.suffix + ".json")
        if trash_path.is_symlink() or metadata.is_symlink():
            raise Failure("trash payload or metadata is a symlink")
        # Metadata is durable before the physical rename, so a crash between
        # rename and journal advancement remains discoverable and verifiable.
        _durable_json(metadata, record)
        if hook:
            before_hook = "before_source_physical_trash" if role == "source" else "before_replacement_physical_trash"
            self._hook(before_hook, trash_path=trash_path, metadata=metadata)
        if identity(path, progress=progress) != ident:
            raise Failure("trash payload changed before physical move")
        _rename_noreplace(path, trash_path, source_root=root, target_root=root)
        if hash_file(trash_path, progress=progress) != (ident.sha256, ident.size):
            raise Failure("trash payload hash mismatch after physical move")
        if hook:
            self._hook(hook, trash_path=trash_path)
        return trash_path

    def move(self, source_rel: str, target_rel: str, *, op_id: str | None = None,
             owner: str = "worker-1", replace: bool = False, policy: Policy | None = None,
             mutation: Callable[[Path], None] | None = None, lock_ttl: float = 30.0) -> dict:
        policy = policy or Policy()
        op_id = op_id or f"op-{secrets.token_hex(8)}"
        _validate_op_id(op_id)
        source = self._path(self.source_root, source_rel)
        target = self._path(self.target_root, target_rel)
        if source_rel in policy.protected_sources or target_rel in policy.protected_targets:
            raise Failure("protected path")
        if policy.permission_denied:
            raise PermissionError("injected permission denial")
        if not source.is_file():
            raise Failure("source missing or not regular file")
        if target.exists() and not replace:
            raise Conflict("target collision")
        target.parent.mkdir(parents=True, exist_ok=True)
        source_id = identity(source)
        if policy.insufficient_space or (not same_device(source, target.parent)
                                         and shutil.disk_usage(target.parent).free < source_id.size):
            raise OSError(errno.ENOSPC, "insufficient target volume space")
        replacement_id = identity(target) if replace and target.exists() else None
        journal = self._new(op_id, source_rel, target_rel, source, target, source_id, replace, owner,
                            replacement_id)
        recorded = journal.read()
        if (recorded.get("replacement_required") != (replacement_id is not None)
                or recorded.get("replacement_identity") != (asdict(replacement_id) if replacement_id else None)):
            raise Failure("replacement preflight observation was not durably recorded")
        lock = TaskLock(self.ops / op_id / "lock", op_id, owner, ttl=lock_ttl)
        lock.claim()
        progress = _heartbeat_pulse(lock)
        try:
            if policy.cancel:
                journal.write(State.CANCELLED, reason="cancelled before physical commit")
                return journal.read()
            if journal.read().get("replacement_required") and not target.exists():
                journal.write(State.CONFLICT, conflict="replacement target disappeared before commit")
                raise Conflict("replacement target disappeared")
            if replace and not journal.read().get("replacement_required") and target.exists():
                journal.write(State.CONFLICT, conflict="replacement target appeared after preflight")
                raise Conflict("replacement target appeared")
            same = source.stat().st_dev == target.parent.stat().st_dev
            if same:
                if identity(source, progress=progress) != source_id:
                    raise Failure("source changed before same-device commit")
                if journal.read().get("replacement_required") and not target.exists():
                    journal.write(State.CONFLICT, conflict="replacement target disappeared before commit")
                    raise Conflict("replacement target disappeared")
                if replace and not journal.read().get("replacement_required") and target.exists():
                    journal.write(State.CONFLICT, conflict="replacement target appeared after preflight")
                    raise Conflict("replacement target appeared")
                if replace and target.exists():
                    old_id = identity(target, progress=progress)
                    expected_old = journal.read().get("replacement_identity")
                    if not expected_old or old_id != Identity(**expected_old):
                        journal.write(State.CONFLICT, conflict="replacement target changed after preflight")
                        raise Conflict("replacement target changed")
                    replacement = self._trash_record(target, target_rel, op_id, "replacement", old_id,
                                                     role="replacement", hook="after_replacement_physical_trash",
                                                     progress=progress)
                    journal.write(State.PREFLIGHT, replacement_trash=str(replacement))
                _assert_internal_path(self.source_root, source)
                _assert_internal_path(self.target_root, target)
                _rename_noreplace(source, target, source_root=self.source_root, target_root=self.target_root)
                self._hook("after_target_physical_commit", journal=journal.path)
            else:
                stage = Path(journal.read()["stage"])
                _assert_internal_path(self.target_root, stage)
                stage.parent.mkdir(parents=True, exist_ok=True)
                _fsync_dir(stage.parent.parent)
                if stage.parent.stat().st_dev != target.parent.stat().st_dev:
                    raise UnsupportedPrimitive("stage is not on target volume")
                with source.open("rb") as src, stage.open("wb") as dst:
                    copied = 0
                    while True:
                        block = src.read(CHUNK)
                        if not block:
                            break
                        dst.write(block)
                        copied += len(block)
                        progress()
                        if mutation:
                            mutation(source)
                    dst.flush(); os.fsync(dst.fileno())
                _fsync_dir(stage.parent)
                self._hook("after_stage_physical", journal=journal.path)
                journal.write(State.STAGED, staged_size=copied)
                self._hook("after_staged_journal", journal=journal.path)
                staged_hash, staged_size = hash_file(stage, progress=progress)
                if staged_size != source_id.size or staged_hash != source_id.sha256:
                    raise Failure("staged/source hash mismatch")
                if identity(source, progress=progress) != source_id:
                    raise Failure("source mutated during copy")
                journal.write(State.VERIFIED, staged_hash=staged_hash)
                self._hook("after_verified_journal", journal=journal.path)
                current = journal.read()
                if current.get("replacement_required") and not target.exists():
                    journal.write(State.CONFLICT, conflict="replacement target disappeared before commit")
                    raise Conflict("replacement target disappeared")
                if replace and not current.get("replacement_required") and target.exists():
                    journal.write(State.CONFLICT, conflict="replacement target appeared after preflight")
                    raise Conflict("replacement target appeared")
                if replace and target.exists():
                    old_id = identity(target, progress=progress)
                    expected_old = current.get("replacement_identity")
                    if not expected_old or old_id != Identity(**expected_old):
                        journal.write(State.CONFLICT, conflict="replacement target changed after preflight")
                        raise Conflict("replacement target changed")
                    replacement = self._trash_record(target, target_rel, op_id, "replacement", old_id,
                                                     role="replacement", hook="after_replacement_physical_trash",
                                                     progress=progress)
                    journal.write(State.VERIFIED, replacement_trash=str(replacement))
                _assert_internal_path(self.target_root, target)
                _rename_noreplace(stage, target, source_root=self.target_root, target_root=self.target_root)
                self._hook("after_target_physical_commit", journal=journal.path)
            _assert_internal_path(self.target_root, target)
            target_hash, target_size = hash_file(target, progress=progress)
            if (target_hash, target_size) != (source_id.sha256, source_id.size):
                raise Failure("target hash mismatch")
            journal.write(State.TARGET_COMMITTED, target_hash=target_hash)
            self._hook("after_target_committed_journal", journal=journal.path)
            if source.exists():
                try:
                    _assert_internal_path(self.source_root, source)
                    _assert_internal_path(self.target_root, target)
                    source_after = identity(source, progress=progress)
                    target_after = hash_file(target, progress=progress)
                except (OSError, Failure) as exc:
                    journal.write(State.CONFLICT, conflict="source/target changed before source trash")
                    raise Failure("source/target changed before source trash") from exc
                if source_after != source_id or target_after != (source_id.sha256, source_id.size):
                    journal.write(State.CONFLICT, conflict="source/target hash changed before source trash")
                    raise Failure("source/target hash changed before source trash")
                trash = self._trash_record(source, source_rel, op_id, "move-source", source_id,
                                           role="source", hook="after_source_physical_trash",
                                           progress=progress)
                journal.write(State.SOURCE_TRASHED, source_trash=str(trash))
                self._hook("after_source_trashed_journal", journal=journal.path)
                _assert_internal_path(self.target_root, target)
                if hash_file(target, progress=progress) != (source_id.sha256, source_id.size):
                    journal.write(State.CONFLICT, conflict="target changed after source trash")
                    raise Failure("target changed after source trash")
            elif not journal.read().get("source_trash"):
                # Same-device rename consumed the source; this is still a valid
                # move, while cross-device always leaves a physical trash item.
                if not same:
                    raise Failure("source disappeared without trash")
            journal.write(State.COMPLETE)
            self._hook("after_complete_journal", journal=journal.path)
            return journal.read()
        except Exception:
            # A precommit failure must not leave stage or lock behind.
            try:
                current = journal.read()
                if current.get("state") in (State.PREFLIGHT.value, State.STAGED.value, State.VERIFIED.value):
                    # Cleanup is always derived from the configured target root;
                    # a journal absolute path is untrusted input.
                    stage = self.target_root / ".m006-stage" / op_id / "payload"
                    _assert_internal_path(self.target_root, stage)
                    if stage.exists(): stage.unlink()
                    journal.write(State.CANCELLED if policy.cancel else State.MANUAL)
            finally:
                lock.release()
            raise
        finally:
            if lock.path.exists():
                lock.release()

    def inspect(self, op_id: str) -> dict[str, object]:
        journal = self._journal(op_id).read()
        result: dict[str, object] = {"operation_id": op_id, "journal_state": journal["state"]}
        paths = self._physical_paths(op_id, journal)
        for key, path in paths.items():
                metadata = path.with_suffix(path.suffix + ".json")
                if path.is_symlink() or metadata.is_symlink():
                    raise Failure(f"{key} payload or metadata is symlink")
                result[key] = {"path": str(path), "exists": path.exists(),
                               "sha256": hash_file(path)[0] if path.is_file() else None,
                               "metadata_exists": metadata.exists(),
                               "metadata_sha256": hash_file(metadata)[0] if metadata.is_file() else None}
        result["physical_reconciled"] = True
        return result

    def recover(self, op_id: str, owner: str = "recovery") -> dict:
        _validate_op_id(op_id)
        journal = self._journal(op_id)
        data = journal.read()
        lock = TaskLock(self.ops / op_id / "lock", op_id, owner)
        if lock.path.exists():
            current_lock = json.loads(lock.path.read_text(encoding="utf-8"))
            now = time.time()
            if current_lock.get("operation_id") != op_id:
                raise Failure("lock operation identity mismatch")
            if current_lock.get("owner") != owner and float(current_lock.get("expires", 0)) >= now:
                raise Failure("foreign unexpired lock")
            if float(current_lock.get("expires", 0)) < now:
                lock.reconcile_and_reclaim(lambda requested: self.inspect(requested), now=now)
            else:
                lock.bind_existing()
        else:
            lock.claim()
        try:
            return self._recover_physical(op_id, owner, _heartbeat_pulse(lock))
        finally:
            if lock.path.exists():
                try:
                    lock.release()
                except Failure:
                    # Never remove a lock that changed owner while recovery ran.
                    pass

    def _recover_physical(self, op_id: str, owner: str = "recovery",
                          progress: Callable[[], None] | None = None) -> dict:
        journal = self._journal(op_id)
        data = journal.read()
        physical = self._physical_paths(op_id, data)
        source = physical["source"]; target = physical["target"]; stage = physical["stage"]
        expected = data["source_identity"]
        source_trash = physical["source_trash"]
        replacement_trash = physical["replacement_trash"]
        if data["state"] == State.CANCELLED.value:
            source_ok = False
            if source.is_file():
                digest, size = hash_file(source, progress=progress)
                source_ok = digest == expected["sha256"] and size == expected["size"]
            unexpected = ((target.exists() and not data.get("replacement_required"))
                          or (data.get("replacement_required") and not target.exists())
                          or not source_ok or stage.exists()
                          or source_trash.exists() or replacement_trash.exists())
            if unexpected:
                journal.write(State.MANUAL, conflict="cancelled operation has physical residue")
            return journal.read()
        if source_trash.exists() and not data.get("source_trash"):
            data["source_trash"] = str(source_trash)
            journal.write(State(data["state"]), source_trash=str(source_trash))
        if replacement_trash.exists() and not data.get("replacement_trash"):
            data["replacement_trash"] = str(replacement_trash)
            journal.write(State(data["state"]), replacement_trash=str(replacement_trash))
        def matching(path: Path) -> bool:
            if not path.is_file(): return False
            digest, size = hash_file(path, progress=progress)
            return digest == expected["sha256"] and size == expected["size"]

        def valid_trash(path: Path, role: str, original_relative: str,
                        reason: str | None = None) -> bool:
            metadata = path.with_suffix(path.suffix + ".json")
            if path.is_symlink() or metadata.is_symlink() or not path.is_file() or not metadata.is_file():
                return False
            try:
                record = json.loads(metadata.read_text(encoding="utf-8"))
                root = self.source_root if role == "source" else self.target_root
                return (record.get("schema_version") == TRASH_SCHEMA
                        and record.get("operation_id") == op_id
                        and record.get("root_role") == role
                        and record.get("trash_relative_path") == str(path.relative_to(root))
                        and record.get("original_relative_path") == original_relative
                        and (reason is None or record.get("reason") == reason)
                        and hash_file(path, progress=progress) == (record.get("sha256"), record.get("size")))
            except (OSError, ValueError, KeyError):
                return False

        if data.get("operation_kind") == "delete":
            if source_trash.exists() and not valid_trash(source_trash, "source", data["source_relative"], "delete"):
                journal.write(State.MANUAL, conflict="delete trash metadata/hash invalid")
                return journal.read()
            if source.exists() and source_trash.exists():
                journal.write(State.MANUAL, conflict="delete has both source and trash")
                return journal.read()
            if source.exists() and not source_trash.exists():
                if not matching(source):
                    journal.write(State.CONFLICT, conflict="delete source changed before trash")
                    return journal.read()
                trash = self._trash_record(source, data["source_relative"], op_id, "delete",
                                           identity(source, progress=progress), role="source",
                                           progress=progress)
                journal.write(State.SOURCE_TRASHED, source_trash=str(trash))
            elif not source.exists() and not source_trash.exists():
                journal.write(State.MANUAL, conflict="delete source and trash both absent")
                return journal.read()
            journal.write(State.COMPLETE)
            return journal.read()

        if source_trash.exists() and not valid_trash(source_trash, "source", data["source_relative"]):
            journal.write(State.MANUAL, conflict="source trash metadata/hash invalid")
            return journal.read()
        if replacement_trash.exists() and not valid_trash(replacement_trash, "replacement", data["target_relative"], "replacement"):
            journal.write(State.MANUAL, conflict="replacement trash metadata/hash invalid")
            return journal.read()
        # A replacement crash can leave durable replacement metadata while the
        # old target is still present. Verify that metadata against the old
        # target before moving it; do not classify the differing old content as
        # a target conflict when an explicit replacement is in flight.
        if (target.exists() and data.get("replacement_required")
                and matching(source) and not replacement_trash.exists()):
            metadata = replacement_trash.with_suffix(replacement_trash.suffix + ".json")
            if metadata.is_symlink() or not metadata.is_file():
                journal.write(State.CONFLICT, conflict="replacement metadata missing")
                return journal.read()
            try:
                record = json.loads(metadata.read_text(encoding="utf-8"))
                expected_old = data.get("replacement_identity")
                old_id = identity(target, progress=progress)
                if (record.get("schema_version") != TRASH_SCHEMA
                        or record.get("operation_id") != op_id
                        or record.get("root_role") != "replacement"
                        or record.get("reason") != "replacement"
                        or record.get("original_relative_path") != data["target_relative"]
                        or record.get("trash_relative_path") != str(replacement_trash.relative_to(self.target_root))
                        or not expected_old or old_id != Identity(**expected_old)
                        or record.get("sha256") != old_id.sha256 or record.get("size") != old_id.size):
                    journal.write(State.CONFLICT, conflict="replacement metadata does not match old target")
                    return journal.read()
                _rename_noreplace(target, replacement_trash, source_root=self.target_root, target_root=self.target_root)
                journal.write(State(data["state"]), replacement_trash=str(replacement_trash))
            except (OSError, ValueError, KeyError):
                journal.write(State.CONFLICT, conflict="replacement metadata invalid")
                return journal.read()
        target_ok = matching(target)
        source_ok = matching(source)
        if target_ok and source_ok:
            # Duplicate is safe only after full-hash agreement; source is moved
            # to task trash, never unlinked.
            trash = self._trash_record(source, data["source_relative"], op_id,
                                       "move-source-recovery", identity(source, progress=progress),
                                       role="source", progress=progress)
            data["source_trash"] = str(trash)
            journal.write(State.SOURCE_TRASHED, source_trash=str(trash))
            self._hook("after_source_trashed_journal", journal=journal.path)
            if not matching(target):
                journal.write(State.CONFLICT, conflict="target changed after source trash")
                return journal.read()
        elif target_ok and not source.exists():
            pass
        elif target.exists() and not target_ok:
            journal.write(State.CONFLICT, conflict="target hash differs; manual attention")
            return journal.read()
        elif stage.exists() and source_ok and not target.exists():
            if not matching(stage):
                stage.unlink(); journal.write(State.MANUAL, conflict="stage hash differs")
                return journal.read()
            _rename_noreplace(stage, target, source_root=self.target_root, target_root=self.target_root)
            journal.write(State.TARGET_COMMITTED, target_hash=hash_file(target, progress=progress)[0])
            self._hook("after_target_committed_journal", journal=journal.path)
            if hash_file(target, progress=progress) != (expected["sha256"], expected["size"]):
                journal.write(State.CONFLICT, conflict="target changed before source trash")
                return journal.read()
            if not matching(source):
                journal.write(State.CONFLICT, conflict="source changed before source trash")
                return journal.read()
            trash = self._trash_record(source, data["source_relative"], op_id,
                                       "move-source-recovery", identity(source, progress=progress),
                                       role="source", progress=progress)
            journal.write(State.SOURCE_TRASHED, source_trash=str(trash))
            self._hook("after_source_trashed_journal", journal=journal.path)
            if not matching(target):
                journal.write(State.CONFLICT, conflict="target changed after source trash")
                return journal.read()
        elif source_ok and not target.exists():
            if data.get("same_device"):
                # A same-device physical commit may not have reached the
                # journal; retry only with noreplace, never overwrite.
                _rename_noreplace(source, target, source_root=self.source_root, target_root=self.target_root)
            else:
                # A crash before the first stage boundary leaves no stage.
                # Recreate it on the target volume with the same bounded copy
                # and verify protocol; never attempt a cross-device rename.
                _assert_internal_path(self.target_root, stage)
                stage.parent.mkdir(parents=True, exist_ok=True)
                with source.open("rb") as src, stage.open("wb") as dst:
                    while True:
                        block = src.read(CHUNK)
                        if not block:
                            break
                        dst.write(block)
                        if progress:
                            progress()
                    dst.flush(); os.fsync(dst.fileno())
                _fsync_dir(stage.parent)
                if not matching(stage):
                    stage.unlink(missing_ok=True)
                    journal.write(State.MANUAL, conflict="recovery stage hash mismatch")
                    return journal.read()
                _rename_noreplace(stage, target, source_root=self.target_root, target_root=self.target_root)
            journal.write(State.TARGET_COMMITTED, target_hash=hash_file(target, progress=progress)[0])
        elif not source.exists() and not target.exists():
            journal.write(State.MANUAL, conflict="neither source nor target is physically present")
            return journal.read()
        else:
            journal.write(State.MANUAL, conflict="physical state requires review")
            return journal.read()
        final = journal.read()
        if target.exists() and matching(target) and source.exists():
            # Recovery-created cross-device target still needs the final
            # source-to-trash physical step before it can be complete.
            if hash_file(target, progress=progress) != (expected["sha256"], expected["size"]) or not matching(source):
                journal.write(State.CONFLICT, conflict="source/target hash changed before source trash")
                return journal.read()
            trash = self._trash_record(source, data["source_relative"], op_id,
                                       "move-source-recovery", identity(source, progress=progress),
                                       role="source", progress=progress)
            journal.write(State.SOURCE_TRASHED, source_trash=str(trash))
            self._hook("after_source_trashed_journal", journal=journal.path)
            if not matching(target):
                journal.write(State.CONFLICT, conflict="target changed after source trash")
                return journal.read()
            final = journal.read()
        if target.exists() and matching(target):
            if data.get("replacement_required") and not replacement_trash.exists():
                journal.write(State.MANUAL, conflict="replacement trash missing")
            elif data.get("replacement_required") and not valid_trash(replacement_trash, "replacement", data["target_relative"], "replacement"):
                journal.write(State.MANUAL, conflict="replacement trash metadata/hash invalid")
            elif data.get("same_device") or (final.get("source_trash")
                                               and valid_trash(Path(final["source_trash"]), "source", data["source_relative"])):
                journal.write(State.COMPLETE)
            else:
                journal.write(State.MANUAL, conflict="source trash missing")
        return journal.read()

    def restore(self, trash_path: str | Path, *, target: str | None = None) -> str:
        trash = Path(trash_path)
        metadata = trash.with_suffix(trash.suffix + ".json")
        roots = tuple(dict.fromkeys((self.source_root / ".m006-trash", self.target_root / ".m006-trash")))
        candidates = []
        for root in roots:
            try:
                relative_payload = trash.relative_to(root)
            except ValueError:
                continue
            cursor = root
            for part in relative_payload.parts:
                cursor = cursor / part
                if cursor.is_symlink():
                    raise Failure("trash path contains symlink")
            resolved = trash.resolve(strict=False)
            if resolved != root and root not in resolved.parents:
                raise Failure("trash path escapes configured trash root")
            candidates.append(root)
        if len(candidates) != 1:
            raise Failure("trash payload is outside configured task-local trash")
        if metadata.is_symlink():
            raise Failure("trash metadata is a symlink")
        record = json.loads(metadata.read_text(encoding="utf-8"))
        if record.get("schema_version") != TRASH_SCHEMA:
            raise Failure("unknown trash metadata schema")
        role = record.get("root_role")
        root = self.source_root if role == "source" else self.target_root if role == "replacement" else None
        if root is None or candidates[0] != root / ".m006-trash":
            raise Failure("trash root role mismatch")
        relative_payload = trash.relative_to(root / ".m006-trash")
        if len(relative_payload.parts) < 2:
            raise Failure("invalid trash relative path")
        operation_id = relative_payload.parts[0]
        _validate_op_id(operation_id)
        expected_relative = str(trash.relative_to(root))
        if (record.get("operation_id") != operation_id
                or record.get("trash_relative_path") != expected_relative):
            raise Failure("trash metadata path or operation mismatch")
        relative = target or record["original_relative_path"]
        destination = self._path(root, relative)
        if trash.exists() and trash.stat().st_dev != destination.parent.stat().st_dev:
            raise UnsupportedPrimitive("cross-device restore requires staged restore adapter")
        if destination.exists():
            if hash_file(destination) == (record["sha256"], record["size"]):
                return "already_restored"
            raise Conflict("restore target differs")
        if not trash.exists() or hash_file(trash) != (record["sha256"], record["size"]):
            raise Failure("trash hash mismatch")
        destination.parent.mkdir(parents=True, exist_ok=True)
        _rename_noreplace(trash, destination, source_root=root, target_root=root)
        _fsync_dir(destination.parent)
        return "restored"

    def delete(self, relative: str, *, op_id: str | None = None,
               owner: str = "worker-1", policy: Policy | None = None) -> dict:
        """Ordinary delete uses the same physical trash and metadata contract."""
        policy = policy or Policy()
        op_id = op_id or f"delete-{secrets.token_hex(8)}"
        _validate_op_id(op_id)
        if relative in policy.protected_sources:
            raise Failure("protected path")
        if policy.permission_denied:
            raise PermissionError("injected permission denial")
        if policy.cancel:
            raise Failure("cancelled")
        source = self._path(self.source_root, relative)
        if not source.is_file():
            raise Failure("source missing or not regular file")
        directory = self.ops / op_id
        directory.mkdir(parents=True, exist_ok=False)
        lock = TaskLock(directory / "lock", op_id, owner)
        lock.claim()
        progress = _heartbeat_pulse(lock)
        try:
            ident = identity(source, progress=progress)
            journal = Journal(directory / "journal.json", op_id)
            journal.write(State.PREFLIGHT, operation_id=op_id, owner=owner,
                          operation_kind="delete", source_identity=asdict(ident),
                          source=str(source), source_relative=relative,
                          target=str(self.target_root / relative), target_relative=relative,
                          source_trash="", replacement_trash="", stage="", replace=False,
                          replacement_required=False, same_device=True)
            trash = self._trash_record(source, relative, op_id, "delete", ident, role="source",
                                       hook="after_source_physical_trash", progress=progress)
            journal.write(State.SOURCE_TRASHED, source_trash=str(trash))
            journal.write(State.COMPLETE)
            return journal.read()
        finally:
            lock.release()
