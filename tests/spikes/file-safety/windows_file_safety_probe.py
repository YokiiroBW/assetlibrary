"""Windows-only M0-006 candidate primitive probe (test code, not production).

The payload commit uses one SetFileInformationByHandle(FileRenameInfo) call
with ReplaceIfExists=FALSE.  It never checks whether the destination exists
before asking Windows to perform the rename.  Executed behavior is evidence
for this Windows 11/NTFS host only; it is not a claim about undocumented
power-loss or filesystem guarantees.
"""
from __future__ import annotations

import argparse
import ctypes
import hashlib
import json
import os
import secrets
import time
from pathlib import Path

WINDOWS = os.name == "nt"
SCHEMA = "m0-006.windows-recovery.v1"
REPOSITORY_ROOT = Path(__file__).resolve().parents[3]
SANDBOX_BASE = REPOSITORY_ROOT / ".runtime" / "sandbox-storage" / "M0-006"

ERROR_ACCESS_DENIED = 5
ERROR_INVALID_HANDLE = 6
ERROR_FILE_EXISTS = 80
ERROR_ALREADY_EXISTS = 183
INVALID_HANDLE_VALUE = ctypes.c_void_p(-1).value

GENERIC_READ = 0x80000000
GENERIC_WRITE = 0x40000000
DELETE = 0x00010000
SYNCHRONIZE = 0x00100000
FILE_SHARE_READ = 0x00000001
FILE_SHARE_WRITE = 0x00000002
FILE_SHARE_DELETE = 0x00000004
CREATE_NEW = 1
OPEN_EXISTING = 3
FILE_ATTRIBUTE_NORMAL = 0x00000080
FILE_ATTRIBUTE_REPARSE_POINT = 0x00000400
FILE_FLAG_WRITE_THROUGH = 0x80000000
FILE_FLAG_BACKUP_SEMANTICS = 0x02000000
FILE_RENAME_INFO_CLASS = 3
MOVEFILE_REPLACE_EXISTING = 0x00000001
MOVEFILE_WRITE_THROUGH = 0x00000008
DRIVE_FIXED = 3

if WINDOWS:
    from ctypes import wintypes

    _kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    _CreateFileW = _kernel32.CreateFileW
    _CreateFileW.argtypes = (
        wintypes.LPCWSTR,
        wintypes.DWORD,
        wintypes.DWORD,
        wintypes.LPVOID,
        wintypes.DWORD,
        wintypes.DWORD,
        wintypes.HANDLE,
    )
    _CreateFileW.restype = wintypes.HANDLE
    _WriteFile = _kernel32.WriteFile
    _WriteFile.argtypes = (
        wintypes.HANDLE,
        wintypes.LPCVOID,
        wintypes.DWORD,
        ctypes.POINTER(wintypes.DWORD),
        wintypes.LPVOID,
    )
    _WriteFile.restype = wintypes.BOOL
    _FlushFileBuffers = _kernel32.FlushFileBuffers
    _FlushFileBuffers.argtypes = (wintypes.HANDLE,)
    _FlushFileBuffers.restype = wintypes.BOOL
    _SetFileInformationByHandle = _kernel32.SetFileInformationByHandle
    _SetFileInformationByHandle.argtypes = (
        wintypes.HANDLE,
        ctypes.c_int,
        wintypes.LPVOID,
        wintypes.DWORD,
    )
    _SetFileInformationByHandle.restype = wintypes.BOOL
    _MoveFileExW = _kernel32.MoveFileExW
    _MoveFileExW.argtypes = (wintypes.LPCWSTR, wintypes.LPCWSTR, wintypes.DWORD)
    _MoveFileExW.restype = wintypes.BOOL
    _CloseHandle = _kernel32.CloseHandle
    _CloseHandle.argtypes = (wintypes.HANDLE,)
    _CloseHandle.restype = wintypes.BOOL
    _GetFileAttributesW = _kernel32.GetFileAttributesW
    _GetFileAttributesW.argtypes = (wintypes.LPCWSTR,)
    _GetFileAttributesW.restype = wintypes.DWORD
    _GetVolumePathNameW = _kernel32.GetVolumePathNameW
    _GetVolumePathNameW.argtypes = (wintypes.LPCWSTR, wintypes.LPWSTR, wintypes.DWORD)
    _GetVolumePathNameW.restype = wintypes.BOOL
    _GetVolumeInformationW = _kernel32.GetVolumeInformationW
    _GetVolumeInformationW.argtypes = (
        wintypes.LPCWSTR,
        wintypes.LPWSTR,
        wintypes.DWORD,
        ctypes.POINTER(wintypes.DWORD),
        ctypes.POINTER(wintypes.DWORD),
        ctypes.POINTER(wintypes.DWORD),
        wintypes.LPWSTR,
        wintypes.DWORD,
    )
    _GetVolumeInformationW.restype = wintypes.BOOL
    _GetDriveTypeW = _kernel32.GetDriveTypeW
    _GetDriveTypeW.argtypes = (wintypes.LPCWSTR,)
    _GetDriveTypeW.restype = wintypes.UINT


class ProbeFailure(RuntimeError):
    pass


class ProbeConflict(ProbeFailure):
    pass


def _require_windows() -> None:
    if not WINDOWS:
        raise ProbeFailure("Windows probe requires Windows")


def _last_error(action: str) -> OSError:
    code = ctypes.get_last_error()
    return OSError(code, f"{action} failed with Win32 error {code}")


def _safe_operation_id(operation_id: str) -> str:
    if (
        not operation_id
        or Path(operation_id).name != operation_id
        or operation_id in (".", "..")
        or any(part in ("", ".", "..") for part in Path(operation_id).parts)
    ):
        raise ProbeFailure("unsafe operation id")
    return operation_id


def _is_reparse_point(path: Path) -> bool:
    _require_windows()
    attributes = _GetFileAttributesW(str(path))
    if attributes == 0xFFFFFFFF:
        raise _last_error("GetFileAttributesW")
    return bool(attributes & FILE_ATTRIBUTE_REPARSE_POINT)


def volume_capabilities(path: Path) -> dict[str, object]:
    """Return only non-identifying filesystem facts required by this probe."""
    _require_windows()
    resolved = path.resolve(strict=True)
    volume_path = ctypes.create_unicode_buffer(260)
    if not _GetVolumePathNameW(str(resolved), volume_path, len(volume_path)):
        raise _last_error("GetVolumePathNameW")
    filesystem = ctypes.create_unicode_buffer(64)
    serial = wintypes.DWORD()
    max_component = wintypes.DWORD()
    flags = wintypes.DWORD()
    if not _GetVolumeInformationW(
        volume_path.value,
        None,
        0,
        ctypes.byref(serial),
        ctypes.byref(max_component),
        ctypes.byref(flags),
        filesystem,
        len(filesystem),
    ):
        raise _last_error("GetVolumeInformationW")
    return {
        "filesystem": filesystem.value,
        "drive_type": int(_GetDriveTypeW(volume_path.value)),
        "is_fixed": int(_GetDriveTypeW(volume_path.value)) == DRIVE_FIXED,
    }


def validate_probe_root(root: Path) -> Path:
    """Require an existing task-local directory on fixed local NTFS."""
    _require_windows()
    resolved = root.resolve(strict=True)
    base = SANDBOX_BASE.resolve(strict=True)
    if resolved == base or base not in resolved.parents:
        raise ProbeFailure("probe root must be a child of the M0-006 sandbox")
    if _is_reparse_point(base):
        raise ProbeFailure("M0-006 sandbox base is a reparse point")
    cursor = base
    for part in resolved.relative_to(base).parts:
        cursor = cursor / part
        if _is_reparse_point(cursor):
            raise ProbeFailure("probe root contains a reparse point")
    capabilities = volume_capabilities(resolved)
    if capabilities["filesystem"].upper() != "NTFS" or not capabilities["is_fixed"]:
        raise ProbeFailure("probe root must be on fixed local NTFS")
    return resolved


def _validate_inside(root: Path, candidate: Path) -> Path:
    root = validate_probe_root(root)
    resolved = candidate.resolve(strict=False)
    if resolved == root or root not in resolved.parents:
        raise ProbeFailure("candidate escapes probe root")
    cursor = root
    relative = resolved.relative_to(root)
    for part in relative.parts[:-1]:
        cursor = cursor / part
        if cursor.exists() and _is_reparse_point(cursor):
            raise ProbeFailure("candidate parent contains a reparse point")
    if candidate.exists() and _is_reparse_point(candidate):
        raise ProbeFailure("candidate is a reparse point")
    return resolved


def _open_handle(
    path: Path,
    desired_access: int,
    creation_disposition: int,
    flags: int,
    share_mode: int = 0,
) -> int:
    handle = _CreateFileW(
        str(path),
        desired_access,
        share_mode,
        None,
        creation_disposition,
        flags,
        None,
    )
    if handle == INVALID_HANDLE_VALUE:
        raise _last_error("CreateFileW")
    return handle


def _close_handle(handle: int) -> None:
    if not _CloseHandle(handle):
        raise _last_error("CloseHandle")


def _flush_handle(handle: int) -> None:
    if not _FlushFileBuffers(handle):
        raise _last_error("FlushFileBuffers")


def write_file_durable(path: Path, payload: bytes, *, chunk_size: int = 64 * 1024) -> dict[str, object]:
    """Create a new file with write-through and an explicit FlushFileBuffers."""
    _require_windows()
    path.parent.mkdir(parents=True, exist_ok=True)
    handle = _open_handle(
        path,
        GENERIC_READ | GENERIC_WRITE,
        CREATE_NEW,
        FILE_ATTRIBUTE_NORMAL | FILE_FLAG_WRITE_THROUGH,
    )
    digest = hashlib.sha256()
    written_total = 0
    try:
        for offset in range(0, len(payload), chunk_size):
            block = payload[offset : offset + chunk_size]
            buffer = ctypes.create_string_buffer(block)
            written = wintypes.DWORD()
            if not _WriteFile(handle, buffer, len(block), ctypes.byref(written), None):
                raise _last_error("WriteFile")
            if written.value != len(block):
                raise ProbeFailure("short WriteFile")
            digest.update(block)
            written_total += written.value
        _flush_handle(handle)
    finally:
        _close_handle(handle)
    return {
        "bytes": written_total,
        "sha256": digest.hexdigest(),
        "create_flags": "FILE_FLAG_WRITE_THROUGH",
        "flush_primitive": "FlushFileBuffers(file)",
    }


def flush_existing_file(path: Path) -> None:
    handle = _open_handle(
        path,
        GENERIC_READ | GENERIC_WRITE,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL | FILE_FLAG_WRITE_THROUGH,
        FILE_SHARE_READ,
    )
    try:
        _flush_handle(handle)
    finally:
        _close_handle(handle)


def hash_file(path: Path, chunk_size: int = 64 * 1024) -> tuple[str, int]:
    digest = hashlib.sha256()
    size = 0
    with path.open("rb") as stream:
        while True:
            block = stream.read(chunk_size)
            if not block:
                break
            digest.update(block)
            size += len(block)
    return digest.hexdigest(), size


def rename_noreplace(root: Path, source: Path, target: Path) -> dict[str, object]:
    """Rename through one no-replace Win32 operation; never pre-check target."""
    root = validate_probe_root(root)
    source = _validate_inside(root, source)
    target = _validate_inside(root, target)
    if not source.is_file():
        raise ProbeFailure("source is not a regular file")
    if source.stat().st_dev != target.parent.stat().st_dev:
        raise ProbeFailure("candidate rename must remain on one volume")

    target_name = str(target)

    class FileRenameInfo(ctypes.Structure):
        _fields_ = (
            # The public header exposes a BOOLEAN/DWORD union. DWORD preserves
            # the union width; zero is ReplaceIfExists=FALSE for FileRenameInfo.
            ("ReplaceIfExistsOrFlags", wintypes.DWORD),
            ("RootDirectory", wintypes.HANDLE),
            ("FileNameLength", wintypes.DWORD),
            ("FileName", wintypes.WCHAR * (len(target_name) + 1)),
        )

    info = FileRenameInfo()
    info.ReplaceIfExistsOrFlags = 0
    info.RootDirectory = None
    info.FileNameLength = len(target_name.encode("utf-16-le"))
    info.FileName = target_name
    buffer_size = FileRenameInfo.FileName.offset + info.FileNameLength
    handle = _open_handle(
        source,
        GENERIC_READ | GENERIC_WRITE | DELETE | SYNCHRONIZE,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL | FILE_FLAG_WRITE_THROUGH,
        FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
    )
    try:
        _flush_handle(handle)
        if not _SetFileInformationByHandle(
            handle,
            FILE_RENAME_INFO_CLASS,
            ctypes.byref(info),
            buffer_size,
        ):
            code = ctypes.get_last_error()
            if code in (ERROR_FILE_EXISTS, ERROR_ALREADY_EXISTS):
                raise FileExistsError(code, "target already exists")
            raise OSError(code, f"SetFileInformationByHandle failed with Win32 error {code}")
        _flush_handle(handle)
    finally:
        _close_handle(handle)
    return {
        "primitive": "SetFileInformationByHandle(FileRenameInfo)",
        "replace_if_exists": False,
        "handle_flags": "FILE_FLAG_WRITE_THROUGH",
        "pre_rename_flush": True,
        "post_rename_flush": True,
    }


def directory_flush_probe(path: Path) -> dict[str, object]:
    """Execute, but do not assume support for, FlushFileBuffers(directory)."""
    _require_windows()
    try:
        handle = _open_handle(
            path,
            GENERIC_WRITE,
            OPEN_EXISTING,
            FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_WRITE_THROUGH,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
        )
    except OSError as exc:
        return {"supported": False, "stage": "open", "winerror": int(exc.errno or 0)}
    try:
        if _FlushFileBuffers(handle):
            return {"supported": True, "stage": "flush", "winerror": 0}
        return {
            "supported": False,
            "stage": "flush",
            "winerror": int(ctypes.get_last_error()),
        }
    finally:
        _close_handle(handle)


def _durable_json(path: Path, value: dict[str, object]) -> None:
    payload = (json.dumps(value, sort_keys=True, separators=(",", ":")) + "\n").encode("utf-8")
    temp = path.with_name(f".{path.name}.{os.getpid()}.{secrets.token_hex(4)}.tmp")
    write_file_durable(temp, payload)
    flags = MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH
    if not _MoveFileExW(str(temp), str(path), flags):
        code = ctypes.get_last_error()
        temp.unlink(missing_ok=True)
        raise OSError(code, f"MoveFileExW journal replace failed with Win32 error {code}")


def _operation_paths(root: Path, operation_id: str) -> tuple[Path, Path, Path]:
    root = validate_probe_root(root)
    operation_id = _safe_operation_id(operation_id)
    operation = root / "operations" / operation_id
    stage = operation / "stage.bin"
    target = root / "targets" / f"{operation_id}.bin"
    for candidate in (operation, stage, target):
        _validate_inside(root, candidate)
    return operation, stage, target


def create_staged_operation(root: Path, operation_id: str, payload: bytes) -> dict[str, object]:
    operation, stage, target = _operation_paths(root, operation_id)
    operation.mkdir(parents=True, exist_ok=False)
    target.parent.mkdir(parents=True, exist_ok=True)
    evidence = write_file_durable(stage, payload)
    journal = {
        "schema_version": SCHEMA,
        "operation_id": operation_id,
        "state": "staged",
        "stage_relative": str(stage.relative_to(root)),
        "target_relative": str(target.relative_to(root)),
        "size": evidence["bytes"],
        "sha256": evidence["sha256"],
    }
    _durable_json(operation / "journal.json", journal)
    return journal


def crash_fixture(root: Path, operation_id: str, boundary: str) -> None:
    payload = (f"m0-006-windows-{operation_id}\n".encode("utf-8") * 4096) + b"tail"
    create_staged_operation(root, operation_id, payload)
    if boundary == "after_stage_flush":
        os._exit(77)
    if boundary == "after_target_rename":
        _, stage, target = _operation_paths(root, operation_id)
        rename_noreplace(root, stage, target)
        os._exit(77)
    raise ProbeFailure("unknown crash boundary")


def recover_operation(root: Path, operation_id: str) -> dict[str, object]:
    operation, stage, target = _operation_paths(root, operation_id)
    journal_path = operation / "journal.json"
    journal = json.loads(journal_path.read_text(encoding="utf-8"))
    expected_stage = str(stage.relative_to(root))
    expected_target = str(target.relative_to(root))
    if (
        journal.get("schema_version") != SCHEMA
        or journal.get("operation_id") != operation_id
        or journal.get("stage_relative") != expected_stage
        or journal.get("target_relative") != expected_target
        or journal.get("state") not in ("staged", "complete", "conflict")
    ):
        raise ProbeFailure("invalid recovery journal")
    expected = (journal["sha256"], journal["size"])

    if target.exists():
        if hash_file(target) != expected:
            journal.update(state="conflict", reason="target_hash_mismatch")
            _durable_json(journal_path, journal)
            return {"state": "conflict", "target_preserved": True, "stage_preserved": stage.exists()}
        if stage.exists():
            journal.update(state="conflict", reason="target_and_stage_both_present")
            _durable_json(journal_path, journal)
            return {"state": "conflict", "target_preserved": True, "stage_preserved": True}
    elif stage.exists():
        if hash_file(stage) != expected:
            journal.update(state="conflict", reason="stage_hash_mismatch")
            _durable_json(journal_path, journal)
            return {"state": "conflict", "target_preserved": False, "stage_preserved": True}
        try:
            rename_noreplace(root, stage, target)
        except FileExistsError:
            journal.update(state="conflict", reason="target_collision_during_recovery")
            _durable_json(journal_path, journal)
            return {"state": "conflict", "target_preserved": True, "stage_preserved": True}
    else:
        journal.update(state="conflict", reason="stage_and_target_absent")
        _durable_json(journal_path, journal)
        return {"state": "conflict", "target_preserved": False, "stage_preserved": False}

    flush_existing_file(target)
    if hash_file(target) != expected:
        journal.update(state="conflict", reason="target_reopen_hash_mismatch")
        _durable_json(journal_path, journal)
        return {"state": "conflict", "target_preserved": True, "stage_preserved": stage.exists()}
    journal.update(state="complete", reason="physical_target_reopened_and_hashed")
    _durable_json(journal_path, journal)
    return {
        "state": "complete",
        "target_reopened_and_hashed": True,
        "stage_absent": not stage.exists(),
    }


def _wait_for_marker(marker: Path, timeout_seconds: float = 10.0) -> None:
    deadline = time.monotonic() + timeout_seconds
    while not marker.exists() and time.monotonic() < deadline:
        time.sleep(0.001)
    if not marker.exists():
        raise ProbeFailure("race start marker timeout")


def _relative_path(root: Path, relative: str) -> Path:
    candidate = root / Path(relative)
    return _validate_inside(root, candidate)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="M0-006 Windows test-only probe")
    subparsers = parser.add_subparsers(dest="command", required=True)

    crash = subparsers.add_parser("crash-fixture")
    crash.add_argument("--root", required=True)
    crash.add_argument("--operation", required=True)
    crash.add_argument("--boundary", required=True)

    recover = subparsers.add_parser("recover")
    recover.add_argument("--root", required=True)
    recover.add_argument("--operation", required=True)

    race = subparsers.add_parser("race-contender")
    race.add_argument("--root", required=True)
    race.add_argument("--source", required=True)
    race.add_argument("--target", required=True)
    race.add_argument("--start-marker", required=True)

    hash_command = subparsers.add_parser("hash")
    hash_command.add_argument("--root", required=True)
    hash_command.add_argument("--relative", required=True)

    args = parser.parse_args(argv)
    root = validate_probe_root(Path(args.root))
    if args.command == "crash-fixture":
        crash_fixture(root, args.operation, args.boundary)
    if args.command == "recover":
        print(json.dumps(recover_operation(root, args.operation), sort_keys=True))
        return 0
    if args.command == "race-contender":
        source = _relative_path(root, args.source)
        target = _relative_path(root, args.target)
        marker = _relative_path(root, args.start_marker)
        _wait_for_marker(marker)
        try:
            rename_noreplace(root, source, target)
            outcome = "success"
        except FileExistsError:
            outcome = "collision"
        print(json.dumps({"outcome": outcome}, sort_keys=True))
        return 0
    if args.command == "hash":
        digest, size = hash_file(_relative_path(root, args.relative))
        print(json.dumps({"sha256": digest, "size": size}, sort_keys=True))
        return 0
    return 2


if __name__ == "__main__":
    raise SystemExit(main())
