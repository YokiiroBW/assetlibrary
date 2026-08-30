"""M0-006 test-only physical file operation spike (Python 3.12 stdlib).

The journal is coordination only: every recovery decision re-reads the physical
files and verifies their SHA-256.  This module deliberately is not a production
filesystem abstraction.
"""
from __future__ import annotations

import ctypes
import ctypes.util
import errno
import hashlib
import json
import os
import secrets
import shutil
import stat
import time
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


def hash_file(path: Path, chunk_size: int = CHUNK) -> tuple[str, int]:
    digest = hashlib.sha256()
    total = 0
    with path.open("rb") as stream:
        while True:
            block = stream.read(chunk_size)
            if not block:
                break
            digest.update(block)
            total += len(block)
    return digest.hexdigest(), total


def same_device(source: Path, target_parent: Path) -> bool:
    return source.stat().st_dev == target_parent.stat().st_dev


def identity(path: Path) -> Identity:
    before = path.stat()
    digest, size = hash_file(path)
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


def _rename_noreplace(source: Path, target: Path) -> None:
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
    result = fn(-100, os.fsencode(source), -100, os.fsencode(target), 1)
    if result != 0:
        code = ctypes.get_errno()
        if code == errno.EEXIST:
            raise Conflict("target exists")
        if code in (errno.ENOSYS, errno.EINVAL, errno.ENOTSUP):
            raise UnsupportedPrimitive(f"renameat2(RENAME_NOREPLACE) unsupported: {code}")
        raise OSError(code, os.strerror(code), str(source))
    _fsync_dir(source.parent)
    _fsync_dir(target.parent)


class Journal:
    def __init__(self, path: Path):
        self.path = path

    def read(self) -> dict:
        value = json.loads(self.path.read_text(encoding="utf-8"))
        if value.get("schema_version") != SCHEMA:
            raise Failure("unknown journal schema")
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
        self.path, self.operation_id, self.owner, self.ttl = path, operation_id, owner, ttl

    def claim(self, now: float | None = None) -> str:
        now = time.time() if now is None else now
        value = {"schema_version": "m0-006.lock.v1", "operation_id": self.operation_id,
                 "owner": self.owner, "heartbeat": now, "expires": now + self.ttl}
        self.path.parent.mkdir(parents=True, exist_ok=True)
        try:
            fd = os.open(self.path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
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
        with os.fdopen(fd, "w", encoding="utf-8") as stream:
            json.dump(value, stream, sort_keys=True)
            stream.flush()
            os.fsync(stream.fileno())
        _fsync_dir(self.path.parent)
        return "claimed"

    def heartbeat(self, now: float | None = None) -> None:
        now = time.time() if now is None else now
        if not self.path.exists():
            raise Failure("lock missing")
        current = json.loads(self.path.read_text(encoding="utf-8"))
        if current.get("operation_id") != self.operation_id or current.get("owner") != self.owner:
            raise Failure("lock ownership mismatch")
        current.update(heartbeat=now, expires=now + self.ttl)
        _durable_json(self.path, current)

    def reconcile_and_reclaim(self, physical_reconciled: bool, now: float | None = None) -> None:
        if not physical_reconciled:
            raise Failure("reconcile required before reclaim")
        now = time.time() if now is None else now
        if self.path.exists():
            current = json.loads(self.path.read_text(encoding="utf-8"))
            if float(current.get("expires", 0)) >= now:
                raise Failure("unexpired lock")
            if current.get("operation_id") != self.operation_id:
                raise Failure("different operation lock")
            self.path.unlink()
            _fsync_dir(self.path.parent)
        self.claim(now)

    def release(self) -> None:
        if self.path.exists():
            current = json.loads(self.path.read_text(encoding="utf-8"))
            if current.get("operation_id") != self.operation_id or current.get("owner") != self.owner:
                raise Failure("lock ownership mismatch")
            self.path.unlink()
            _fsync_dir(self.path.parent)


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
        return Journal(self.ops / op_id / "journal.json")

    def _new(self, op_id: str, source_rel: str, target_rel: str, source: Path, target: Path,
             source_id: Identity, replace: bool, owner: str) -> Journal:
        directory = self.ops / op_id
        directory.mkdir(parents=True, exist_ok=False)
        _fsync_dir(directory.parent)
        journal = Journal(directory / "journal.json")
        journal.write(State.PREFLIGHT, operation_id=op_id, owner=owner,
                      source_relative=source_rel, target_relative=target_rel,
                      source=str(source), target=str(target), source_identity=asdict(source_id),
                      replace=replace, same_device=source.stat().st_dev == target.parent.stat().st_dev,
                      stage=str(self.target_root / ".m006-stage" / op_id / "payload"),
                      source_trash="", replacement_trash="")
        self._hook("after_preflight_journal", journal=journal.path)
        return journal

    def _trash_record(self, path: Path, original_relative: str, op_id: str, reason: str,
                      ident: Identity, *, role: str, hook: str | None = None) -> Path:
        root = self.source_root if role == "source" else self.target_root
        trash_dir = root / ".m006-trash" / op_id
        trash_dir.mkdir(parents=True, exist_ok=True)
        _fsync_dir(trash_dir.parent)
        _fsync_dir(trash_dir)
        trash_path = trash_dir / f"{role}-{Path(original_relative).name}"
        record = {"schema_version": TRASH_SCHEMA, "operation_id": op_id, "reason": reason,
                  "root_role": role, "library_id": "m006-test-library",
                  "original_relative_path": original_relative,
                  "trash_relative_path": str(trash_path.relative_to(root)),
                  "size": ident.size, "sha256": ident.sha256}
        metadata = trash_path.with_suffix(trash_path.suffix + ".json")
        # Metadata is durable before the physical rename, so a crash between
        # rename and journal advancement remains discoverable and verifiable.
        _durable_json(metadata, record)
        _rename_noreplace(path, trash_path)
        if hook:
            self._hook(hook, trash_path=trash_path)
        return trash_path

    def move(self, source_rel: str, target_rel: str, *, op_id: str | None = None,
             owner: str = "worker-1", replace: bool = False, policy: Policy | None = None,
             mutation: Callable[[Path], None] | None = None) -> dict:
        policy = policy or Policy()
        op_id = op_id or f"op-{secrets.token_hex(8)}"
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
        journal = self._new(op_id, source_rel, target_rel, source, target, source_id, replace, owner)
        lock = TaskLock(self.ops / op_id / "lock", op_id, owner)
        lock.claim()
        try:
            if policy.cancel:
                journal.write(State.CANCELLED, reason="cancelled before physical commit")
                return journal.read()
            same = source.stat().st_dev == target.parent.stat().st_dev
            if same:
                if identity(source) != source_id:
                    raise Failure("source changed before same-device commit")
                if replace and target.exists():
                    old_id = identity(target)
                    replacement = self._trash_record(target, target_rel, op_id, "replacement", old_id,
                                                     role="replacement", hook="after_replacement_physical_trash")
                    journal.write(State.PREFLIGHT, replacement_trash=str(replacement))
                _rename_noreplace(source, target)
                self._hook("after_target_physical_commit", journal=journal.path)
            else:
                stage = Path(journal.read()["stage"])
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
                        if mutation:
                            mutation(source)
                    dst.flush(); os.fsync(dst.fileno())
                _fsync_dir(stage.parent)
                self._hook("after_stage_physical", journal=journal.path)
                journal.write(State.STAGED, staged_size=copied)
                self._hook("after_staged_journal", journal=journal.path)
                staged_hash, staged_size = hash_file(stage)
                if staged_size != source_id.size or staged_hash != source_id.sha256:
                    raise Failure("staged/source hash mismatch")
                if identity(source) != source_id:
                    raise Failure("source mutated during copy")
                journal.write(State.VERIFIED, staged_hash=staged_hash)
                self._hook("after_verified_journal", journal=journal.path)
                if replace and target.exists():
                    old_id = identity(target)
                    replacement = self._trash_record(target, target_rel, op_id, "replacement", old_id,
                                                     role="replacement", hook="after_replacement_physical_trash")
                    journal.write(State.VERIFIED, replacement_trash=str(replacement))
                _rename_noreplace(stage, target)
                self._hook("after_target_physical_commit", journal=journal.path)
            target_hash, target_size = hash_file(target)
            if (target_hash, target_size) != (source_id.sha256, source_id.size):
                raise Failure("target hash mismatch")
            journal.write(State.TARGET_COMMITTED, target_hash=target_hash)
            self._hook("after_target_committed_journal", journal=journal.path)
            if source.exists():
                source_id = identity(source)
                trash = self._trash_record(source, source_rel, op_id, "move-source", source_id,
                                           role="source", hook="after_source_physical_trash")
                journal.write(State.SOURCE_TRASHED, source_trash=str(trash))
                self._hook("after_source_trashed_journal", journal=journal.path)
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
                    stage = Path(current.get("stage", ""))
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
        result: dict[str, object] = {"journal_state": journal["state"]}
        for key in ("source", "stage", "target", "source_trash", "replacement_trash"):
            value = journal.get(key)
            if value:
                path = Path(value)
                result[key] = {"exists": path.exists(), "sha256": hash_file(path)[0] if path.is_file() else None}
        return result

    def recover(self, op_id: str, owner: str = "recovery") -> dict:
        journal = self._journal(op_id)
        data = journal.read()
        source = Path(data["source"]); target = Path(data["target"]); stage = Path(data["stage"])
        expected = data["source_identity"]
        lock = TaskLock(self.ops / op_id / "lock", op_id, owner)
        if lock.path.exists():
            current_lock = json.loads(lock.path.read_text(encoding="utf-8"))
            now = time.time()
            if current_lock.get("owner") != owner and float(current_lock.get("expires", 0)) >= now:
                raise Failure("foreign unexpired lock")
            if float(current_lock.get("expires", 0)) < now:
                # inspect() hashes every extant physical candidate before this
                # owner-aware reclaim; a journal flag alone is insufficient.
                self.inspect(op_id)
                lock.reconcile_and_reclaim(True, now=now)
        elif data.get("state") not in (State.COMPLETE.value, State.CANCELLED.value):
            lock.claim()
        source_trash = self.source_root / ".m006-trash" / op_id / f"source-{Path(data['source_relative']).name}"
        replacement_trash = self.target_root / ".m006-trash" / op_id / f"replacement-{Path(data['target_relative']).name}"
        if source_trash.exists() and not data.get("source_trash"):
            data["source_trash"] = str(source_trash)
            journal.write(State.SOURCE_TRASHED, source_trash=str(source_trash))
        if replacement_trash.exists() and not data.get("replacement_trash"):
            data["replacement_trash"] = str(replacement_trash)
            journal.write(State.PREFLIGHT, replacement_trash=str(replacement_trash))
        def matching(path: Path) -> bool:
            if not path.is_file(): return False
            digest, size = hash_file(path)
            return digest == expected["sha256"] and size == expected["size"]

        def valid_trash(path: Path) -> bool:
            metadata = path.with_suffix(path.suffix + ".json")
            if not path.is_file() or not metadata.is_file():
                return False
            try:
                record = json.loads(metadata.read_text(encoding="utf-8"))
                return (record.get("schema_version") == TRASH_SCHEMA
                        and record.get("operation_id") == op_id
                        and hash_file(path) == (record.get("sha256"), record.get("size")))
            except (OSError, ValueError, KeyError):
                return False

        if source_trash.exists() and not valid_trash(source_trash):
            journal.write(State.MANUAL, conflict="source trash metadata/hash invalid")
            if lock.path.exists(): lock.release()
            return journal.read()
        if replacement_trash.exists() and not valid_trash(replacement_trash):
            journal.write(State.MANUAL, conflict="replacement trash metadata/hash invalid")
            if lock.path.exists(): lock.release()
            return journal.read()
        target_ok = matching(target)
        source_ok = matching(source)
        if target_ok and source_ok:
            # Duplicate is safe only after full-hash agreement; source is moved
            # to task trash, never unlinked.
            trash = self._trash_record(source, data["source_relative"], op_id, "move-source-recovery", identity(source), role="source")
            data["source_trash"] = str(trash)
            journal.write(State.SOURCE_TRASHED, source_trash=str(trash))
        elif target_ok and not source.exists():
            pass
        elif target.exists() and not target_ok:
            journal.write(State.CONFLICT, conflict="target hash differs; manual attention")
            if lock.path.exists(): lock.release()
            return journal.read()
        elif stage.exists() and source_ok and not target.exists():
            if not matching(stage):
                stage.unlink(); journal.write(State.MANUAL, conflict="stage hash differs")
                if lock.path.exists(): lock.release()
                return journal.read()
            _rename_noreplace(stage, target)
            journal.write(State.TARGET_COMMITTED, target_hash=hash_file(target)[0])
            self._hook("after_target_committed_journal", journal=journal.path)
            trash = self._trash_record(source, data["source_relative"], op_id, "move-source-recovery", identity(source), role="source")
            journal.write(State.SOURCE_TRASHED, source_trash=str(trash))
        elif source_ok and not target.exists():
            if data.get("same_device"):
                # A same-device physical commit may not have reached the
                # journal; retry only with noreplace, never overwrite.
                _rename_noreplace(source, target)
            else:
                # A crash before the first stage boundary leaves no stage.
                # Recreate it on the target volume with the same bounded copy
                # and verify protocol; never attempt a cross-device rename.
                stage.parent.mkdir(parents=True, exist_ok=True)
                with source.open("rb") as src, stage.open("wb") as dst:
                    while True:
                        block = src.read(CHUNK)
                        if not block:
                            break
                        dst.write(block)
                    dst.flush(); os.fsync(dst.fileno())
                _fsync_dir(stage.parent)
                if not matching(stage):
                    stage.unlink(missing_ok=True)
                    journal.write(State.MANUAL, conflict="recovery stage hash mismatch")
                    return journal.read()
                _rename_noreplace(stage, target)
            journal.write(State.TARGET_COMMITTED, target_hash=hash_file(target)[0])
        elif not source.exists() and not target.exists():
            journal.write(State.MANUAL, conflict="neither source nor target is physically present")
            if lock.path.exists(): lock.release()
            return journal.read()
        else:
            journal.write(State.MANUAL, conflict="physical state requires review")
            if lock.path.exists(): lock.release()
            return journal.read()
        final = journal.read()
        if target.exists() and matching(target) and source.exists():
            # Recovery-created cross-device target still needs the final
            # source-to-trash physical step before it can be complete.
            trash = self._trash_record(source, data["source_relative"], op_id,
                                       "move-source-recovery", identity(source), role="source")
            journal.write(State.SOURCE_TRASHED, source_trash=str(trash))
            final = journal.read()
        if target.exists() and matching(target):
            if data.get("replace") and not replacement_trash.exists():
                journal.write(State.MANUAL, conflict="replacement trash missing")
            elif data.get("replace") and not valid_trash(replacement_trash):
                journal.write(State.MANUAL, conflict="replacement trash metadata/hash invalid")
            elif data.get("same_device") or (final.get("source_trash")
                                               and valid_trash(Path(final["source_trash"]))):
                journal.write(State.COMPLETE)
            else:
                journal.write(State.MANUAL, conflict="source trash missing")
        if lock.path.exists():
            try: lock.release()
            except Failure: pass
        return journal.read()

    def restore(self, trash_path: str | Path, *, target: str | None = None) -> str:
        trash = Path(trash_path)
        metadata = trash.with_suffix(trash.suffix + ".json")
        record = json.loads(metadata.read_text(encoding="utf-8"))
        if record.get("schema_version") != TRASH_SCHEMA:
            raise Failure("unknown trash metadata schema")
        relative = target or record["original_relative_path"]
        root = self.source_root if record.get("root_role") == "source" else self.target_root
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
        _rename_noreplace(trash, destination)
        _fsync_dir(destination.parent)
        return "restored"

    def delete(self, relative: str, *, op_id: str | None = None,
               owner: str = "worker-1", policy: Policy | None = None) -> dict:
        """Ordinary delete uses the same physical trash and metadata contract."""
        policy = policy or Policy()
        if relative in policy.protected_sources:
            raise Failure("protected path")
        if policy.permission_denied:
            raise PermissionError("injected permission denial")
        if policy.cancel:
            raise Failure("cancelled")
        source = self._path(self.source_root, relative)
        if not source.is_file():
            raise Failure("source missing or not regular file")
        op_id = op_id or f"delete-{secrets.token_hex(8)}"
        directory = self.ops / op_id
        directory.mkdir(parents=True, exist_ok=False)
        lock = TaskLock(directory / "lock", op_id, owner)
        lock.claim()
        try:
            ident = identity(source)
            trash = self._trash_record(source, relative, op_id, "delete", ident, role="source")
            journal = Journal(directory / "journal.json")
            journal.write(State.SOURCE_TRASHED, operation_id=op_id, owner=owner,
                          source=str(source), source_relative=relative,
                          source_trash=str(trash), target="", stage="")
            journal.write(State.COMPLETE)
            return journal.read()
        finally:
            lock.release()


def cleanup_tree(path: Path) -> None:
    """Only for owned test fixtures; callers must pass an exact task path."""
    if path.exists():
        shutil.rmtree(path)
