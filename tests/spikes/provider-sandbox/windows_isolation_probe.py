"""Executed, test-only Windows Job Object and restricted-token probes.

The module uses only Python's standard library and Windows system APIs. Every
fixture is bounded and lives in a task-owned temporary directory.
"""
from __future__ import annotations

import ctypes
import json
import os
import platform
import subprocess
import sys
import tempfile
import time
from ctypes import wintypes
from pathlib import Path
from typing import Any


if os.name != "nt":
    raise ImportError("windows_isolation_probe is Windows-only")


kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
advapi32 = ctypes.WinDLL("advapi32", use_last_error=True)

CREATE_SUSPENDED = 0x00000004
CREATE_NO_WINDOW = 0x08000000
WAIT_OBJECT_0 = 0
WAIT_TIMEOUT = 258
STILL_ACTIVE = 259

PROCESS_TERMINATE = 0x0001
PROCESS_QUERY_LIMITED_INFORMATION = 0x1000
SYNCHRONIZE = 0x00100000

TOKEN_ASSIGN_PRIMARY = 0x0001
TOKEN_DUPLICATE = 0x0002
TOKEN_QUERY = 0x0008
DISABLE_MAX_PRIVILEGE = 0x00000001
SE_PRIVILEGE_ENABLED = 0x00000002
SE_GROUP_ENABLED = 0x00000004

JOB_OBJECT_LIMIT_ACTIVE_PROCESS = 0x00000008
JOB_OBJECT_LIMIT_PROCESS_TIME = 0x00000002
JOB_OBJECT_LIMIT_PROCESS_MEMORY = 0x00000100
JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000
STATUS_QUOTA_EXCEEDED = 0xC0000044

JOB_OBJECT_BASIC_ACCOUNTING_INFORMATION_CLASS = 1
JOB_OBJECT_EXTENDED_LIMIT_INFORMATION_CLASS = 9

TOKEN_USER_INFORMATION_CLASS = 1
TOKEN_GROUPS_INFORMATION_CLASS = 2
TOKEN_PRIVILEGES_INFORMATION_CLASS = 3
ERROR_INSUFFICIENT_BUFFER = 122


class STARTUPINFOW(ctypes.Structure):
    _fields_ = [
        ("cb", wintypes.DWORD),
        ("lpReserved", wintypes.LPWSTR),
        ("lpDesktop", wintypes.LPWSTR),
        ("lpTitle", wintypes.LPWSTR),
        ("dwX", wintypes.DWORD),
        ("dwY", wintypes.DWORD),
        ("dwXSize", wintypes.DWORD),
        ("dwYSize", wintypes.DWORD),
        ("dwXCountChars", wintypes.DWORD),
        ("dwYCountChars", wintypes.DWORD),
        ("dwFillAttribute", wintypes.DWORD),
        ("dwFlags", wintypes.DWORD),
        ("wShowWindow", wintypes.WORD),
        ("cbReserved2", wintypes.WORD),
        ("lpReserved2", ctypes.POINTER(wintypes.BYTE)),
        ("hStdInput", wintypes.HANDLE),
        ("hStdOutput", wintypes.HANDLE),
        ("hStdError", wintypes.HANDLE),
    ]


class PROCESS_INFORMATION(ctypes.Structure):
    _fields_ = [
        ("hProcess", wintypes.HANDLE),
        ("hThread", wintypes.HANDLE),
        ("dwProcessId", wintypes.DWORD),
        ("dwThreadId", wintypes.DWORD),
    ]


class IO_COUNTERS(ctypes.Structure):
    _fields_ = [
        ("ReadOperationCount", ctypes.c_ulonglong),
        ("WriteOperationCount", ctypes.c_ulonglong),
        ("OtherOperationCount", ctypes.c_ulonglong),
        ("ReadTransferCount", ctypes.c_ulonglong),
        ("WriteTransferCount", ctypes.c_ulonglong),
        ("OtherTransferCount", ctypes.c_ulonglong),
    ]


class JOBOBJECT_BASIC_LIMIT_INFORMATION(ctypes.Structure):
    _fields_ = [
        ("PerProcessUserTimeLimit", ctypes.c_longlong),
        ("PerJobUserTimeLimit", ctypes.c_longlong),
        ("LimitFlags", wintypes.DWORD),
        ("MinimumWorkingSetSize", ctypes.c_size_t),
        ("MaximumWorkingSetSize", ctypes.c_size_t),
        ("ActiveProcessLimit", wintypes.DWORD),
        ("Affinity", ctypes.c_size_t),
        ("PriorityClass", wintypes.DWORD),
        ("SchedulingClass", wintypes.DWORD),
    ]


class JOBOBJECT_EXTENDED_LIMIT_INFORMATION(ctypes.Structure):
    _fields_ = [
        ("BasicLimitInformation", JOBOBJECT_BASIC_LIMIT_INFORMATION),
        ("IoInfo", IO_COUNTERS),
        ("ProcessMemoryLimit", ctypes.c_size_t),
        ("JobMemoryLimit", ctypes.c_size_t),
        ("PeakProcessMemoryUsed", ctypes.c_size_t),
        ("PeakJobMemoryUsed", ctypes.c_size_t),
    ]


class JOBOBJECT_BASIC_ACCOUNTING_INFORMATION(ctypes.Structure):
    _fields_ = [
        ("TotalUserTime", ctypes.c_longlong),
        ("TotalKernelTime", ctypes.c_longlong),
        ("ThisPeriodTotalUserTime", ctypes.c_longlong),
        ("ThisPeriodTotalKernelTime", ctypes.c_longlong),
        ("TotalPageFaultCount", wintypes.DWORD),
        ("TotalProcesses", wintypes.DWORD),
        ("ActiveProcesses", wintypes.DWORD),
        ("TotalTerminatedProcesses", wintypes.DWORD),
    ]


class SID_AND_ATTRIBUTES(ctypes.Structure):
    _fields_ = [("Sid", wintypes.LPVOID), ("Attributes", wintypes.DWORD)]


class TOKEN_USER(ctypes.Structure):
    _fields_ = [("User", SID_AND_ATTRIBUTES)]


class TOKEN_GROUPS_HEADER(ctypes.Structure):
    _fields_ = [("GroupCount", wintypes.DWORD), ("Groups", SID_AND_ATTRIBUTES * 1)]


class LUID(ctypes.Structure):
    _fields_ = [("LowPart", wintypes.DWORD), ("HighPart", wintypes.LONG)]


class LUID_AND_ATTRIBUTES(ctypes.Structure):
    _fields_ = [("Luid", LUID), ("Attributes", wintypes.DWORD)]


kernel32.CreateJobObjectW.argtypes = [wintypes.LPVOID, wintypes.LPCWSTR]
kernel32.CreateJobObjectW.restype = wintypes.HANDLE
kernel32.SetInformationJobObject.argtypes = [wintypes.HANDLE, ctypes.c_int, wintypes.LPVOID, wintypes.DWORD]
kernel32.SetInformationJobObject.restype = wintypes.BOOL
kernel32.QueryInformationJobObject.argtypes = [wintypes.HANDLE, ctypes.c_int, wintypes.LPVOID, wintypes.DWORD, ctypes.POINTER(wintypes.DWORD)]
kernel32.QueryInformationJobObject.restype = wintypes.BOOL
kernel32.AssignProcessToJobObject.argtypes = [wintypes.HANDLE, wintypes.HANDLE]
kernel32.AssignProcessToJobObject.restype = wintypes.BOOL
kernel32.IsProcessInJob.argtypes = [wintypes.HANDLE, wintypes.HANDLE, ctypes.POINTER(wintypes.BOOL)]
kernel32.IsProcessInJob.restype = wintypes.BOOL
kernel32.CreateProcessW.argtypes = [wintypes.LPCWSTR, wintypes.LPWSTR, wintypes.LPVOID, wintypes.LPVOID, wintypes.BOOL, wintypes.DWORD, wintypes.LPVOID, wintypes.LPCWSTR, ctypes.POINTER(STARTUPINFOW), ctypes.POINTER(PROCESS_INFORMATION)]
kernel32.CreateProcessW.restype = wintypes.BOOL
kernel32.ResumeThread.argtypes = [wintypes.HANDLE]
kernel32.ResumeThread.restype = wintypes.DWORD
kernel32.WaitForSingleObject.argtypes = [wintypes.HANDLE, wintypes.DWORD]
kernel32.WaitForSingleObject.restype = wintypes.DWORD
kernel32.GetExitCodeProcess.argtypes = [wintypes.HANDLE, ctypes.POINTER(wintypes.DWORD)]
kernel32.GetExitCodeProcess.restype = wintypes.BOOL
kernel32.TerminateProcess.argtypes = [wintypes.HANDLE, wintypes.UINT]
kernel32.TerminateProcess.restype = wintypes.BOOL
kernel32.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
kernel32.OpenProcess.restype = wintypes.HANDLE
kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
kernel32.CloseHandle.restype = wintypes.BOOL
kernel32.GetCurrentProcess.argtypes = []
kernel32.GetCurrentProcess.restype = wintypes.HANDLE
kernel32.LocalFree.argtypes = [wintypes.HLOCAL]
kernel32.LocalFree.restype = wintypes.HLOCAL

advapi32.OpenProcessToken.argtypes = [wintypes.HANDLE, wintypes.DWORD, ctypes.POINTER(wintypes.HANDLE)]
advapi32.OpenProcessToken.restype = wintypes.BOOL
advapi32.GetTokenInformation.argtypes = [wintypes.HANDLE, ctypes.c_int, wintypes.LPVOID, wintypes.DWORD, ctypes.POINTER(wintypes.DWORD)]
advapi32.GetTokenInformation.restype = wintypes.BOOL
advapi32.CreateRestrictedToken.argtypes = [wintypes.HANDLE, wintypes.DWORD, wintypes.DWORD, ctypes.POINTER(SID_AND_ATTRIBUTES), wintypes.DWORD, wintypes.LPVOID, wintypes.DWORD, wintypes.LPVOID, ctypes.POINTER(wintypes.HANDLE)]
advapi32.CreateRestrictedToken.restype = wintypes.BOOL
advapi32.IsTokenRestricted.argtypes = [wintypes.HANDLE]
advapi32.IsTokenRestricted.restype = wintypes.BOOL
advapi32.ConvertSidToStringSidW.argtypes = [wintypes.LPVOID, ctypes.POINTER(wintypes.LPWSTR)]
advapi32.ConvertSidToStringSidW.restype = wintypes.BOOL
advapi32.CreateProcessAsUserW.argtypes = [wintypes.HANDLE, wintypes.LPCWSTR, wintypes.LPWSTR, wintypes.LPVOID, wintypes.LPVOID, wintypes.BOOL, wintypes.DWORD, wintypes.LPVOID, wintypes.LPCWSTR, ctypes.POINTER(STARTUPINFOW), ctypes.POINTER(PROCESS_INFORMATION)]
advapi32.CreateProcessAsUserW.restype = wintypes.BOOL
advapi32.CreateProcessWithTokenW.argtypes = [wintypes.HANDLE, wintypes.DWORD, wintypes.LPCWSTR, wintypes.LPWSTR, wintypes.DWORD, wintypes.LPVOID, wintypes.LPCWSTR, ctypes.POINTER(STARTUPINFOW), ctypes.POINTER(PROCESS_INFORMATION)]
advapi32.CreateProcessWithTokenW.restype = wintypes.BOOL


def _raise_last_error() -> None:
    raise ctypes.WinError(ctypes.get_last_error())


def _close_handle(handle: wintypes.HANDLE | None) -> None:
    if handle:
        kernel32.CloseHandle(handle)


class NativeProcess:
    def __init__(self, information: PROCESS_INFORMATION):
        self.process_handle = information.hProcess
        self.thread_handle = information.hThread
        self.pid = int(information.dwProcessId)

    def resume(self) -> None:
        if kernel32.ResumeThread(self.thread_handle) == 0xFFFFFFFF:
            _raise_last_error()

    def wait(self, timeout_ms: int) -> bool:
        result = kernel32.WaitForSingleObject(self.process_handle, timeout_ms)
        if result == WAIT_OBJECT_0:
            return True
        if result == WAIT_TIMEOUT:
            return False
        _raise_last_error()
        return False

    def terminate(self) -> None:
        if self.exit_code() == STILL_ACTIVE:
            kernel32.TerminateProcess(self.process_handle, 97)
            self.wait(2000)

    def exit_code(self) -> int:
        code = wintypes.DWORD()
        if not kernel32.GetExitCodeProcess(self.process_handle, ctypes.byref(code)):
            _raise_last_error()
        return int(code.value)

    def close(self) -> None:
        _close_handle(self.thread_handle)
        _close_handle(self.process_handle)
        self.thread_handle = None
        self.process_handle = None


class Job:
    def __init__(self) -> None:
        self.handle = kernel32.CreateJobObjectW(None, None)
        if not self.handle:
            _raise_last_error()

    def set_extended_limits(
        self,
        *,
        active_process_limit: int | None = None,
        process_cpu_ms: int | None = None,
        process_memory_limit: int | None = None,
        kill_on_close: bool = True,
    ) -> None:
        information = JOBOBJECT_EXTENDED_LIMIT_INFORMATION()
        flags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE if kill_on_close else 0
        if active_process_limit is not None:
            flags |= JOB_OBJECT_LIMIT_ACTIVE_PROCESS
            information.BasicLimitInformation.ActiveProcessLimit = active_process_limit
        if process_cpu_ms is not None:
            flags |= JOB_OBJECT_LIMIT_PROCESS_TIME
            information.BasicLimitInformation.PerProcessUserTimeLimit = process_cpu_ms * 10_000
        if process_memory_limit is not None:
            flags |= JOB_OBJECT_LIMIT_PROCESS_MEMORY
            information.ProcessMemoryLimit = process_memory_limit
        information.BasicLimitInformation.LimitFlags = flags
        if not kernel32.SetInformationJobObject(
            self.handle,
            JOB_OBJECT_EXTENDED_LIMIT_INFORMATION_CLASS,
            ctypes.byref(information),
            ctypes.sizeof(information),
        ):
            _raise_last_error()

    def assign(self, process: NativeProcess) -> tuple[bool, int]:
        ctypes.set_last_error(0)
        assigned = bool(kernel32.AssignProcessToJobObject(self.handle, process.process_handle))
        return assigned, 0 if assigned else ctypes.get_last_error()

    def accounting(self) -> JOBOBJECT_BASIC_ACCOUNTING_INFORMATION:
        information = JOBOBJECT_BASIC_ACCOUNTING_INFORMATION()
        if not kernel32.QueryInformationJobObject(
            self.handle,
            JOB_OBJECT_BASIC_ACCOUNTING_INFORMATION_CLASS,
            ctypes.byref(information),
            ctypes.sizeof(information),
            None,
        ):
            _raise_last_error()
        return information

    def extended_limits(self) -> JOBOBJECT_EXTENDED_LIMIT_INFORMATION:
        information = JOBOBJECT_EXTENDED_LIMIT_INFORMATION()
        if not kernel32.QueryInformationJobObject(
            self.handle,
            JOB_OBJECT_EXTENDED_LIMIT_INFORMATION_CLASS,
            ctypes.byref(information),
            ctypes.sizeof(information),
            None,
        ):
            _raise_last_error()
        return information

    def close(self) -> None:
        if self.handle:
            _close_handle(self.handle)
            self.handle = None


def _worker_path() -> Path:
    return Path(__file__).with_name("windows_worker_fixture.py")


def _launch_suspended(arguments: list[str], cwd: Path) -> NativeProcess:
    startup = STARTUPINFOW(cb=ctypes.sizeof(STARTUPINFOW))
    information = PROCESS_INFORMATION()
    command_line = ctypes.create_unicode_buffer(subprocess.list2cmdline(arguments))
    if not kernel32.CreateProcessW(
        None,
        command_line,
        None,
        None,
        False,
        CREATE_SUSPENDED | CREATE_NO_WINDOW,
        None,
        str(cwd),
        ctypes.byref(startup),
        ctypes.byref(information),
    ):
        _raise_last_error()
    return NativeProcess(information)


def _wait_json(path: Path, process: NativeProcess, timeout: float) -> dict[str, Any]:
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if path.exists():
            try:
                return json.loads(path.read_text(encoding="utf-8"))
            except (OSError, json.JSONDecodeError):
                time.sleep(0.005)
                continue
        if process.wait(0):
            break
        time.sleep(0.02)
    if path.exists():
        return json.loads(path.read_text(encoding="utf-8"))
    raise RuntimeError("bounded worker did not produce a report")


def _assign_or_raise(job: Job, process: NativeProcess) -> None:
    assigned, error = job.assign(process)
    if not assigned:
        raise OSError(error, "AssignProcessToJobObject failed")


def probe_active_process_limit(runtime: Path) -> dict[str, Any]:
    job = Job()
    first: NativeProcess | None = None
    second: NativeProcess | None = None
    first_dead_after_close = False
    try:
        job.set_extended_limits(active_process_limit=1)
        first = _launch_suspended([sys.executable, "-B", str(_worker_path()), "sleep", "30"], runtime)
        _assign_or_raise(job, first)
        first.resume()
        active_before = int(job.accounting().ActiveProcesses)
        second = _launch_suspended([sys.executable, "-B", str(_worker_path()), "sleep", "30"], runtime)
        second_assigned, second_error = job.assign(second)
        if second_assigned:
            second.resume()
        limits = job.extended_limits()
        job.close()
        first_dead_after_close = first.wait(5000)
        return {
            "executed": True,
            "passed": active_before == 1 and not second_assigned and first_dead_after_close,
            "configured_limit": int(limits.BasicLimitInformation.ActiveProcessLimit),
            "active_before_limit_attempt": active_before,
            "second_assignment_denied": not second_assigned,
            "second_assignment_winerror": int(second_error),
            "first_dead_after_job_close": first_dead_after_close,
        }
    finally:
        job.close()
        for process in (second, first):
            if process is not None:
                process.terminate()
                process.close()


def probe_process_memory_limit(runtime: Path) -> dict[str, Any]:
    cap = 64 * 1024 * 1024
    report_path = runtime / "memory.json"
    job = Job()
    process: NativeProcess | None = None
    try:
        job.set_extended_limits(process_memory_limit=cap)
        process = _launch_suspended([sys.executable, "-B", str(_worker_path()), "memory", str(report_path)], runtime)
        _assign_or_raise(job, process)
        process.resume()
        report = _wait_json(report_path, process, 8.0)
        exited = process.wait(5000)
        limits = job.extended_limits()
        peak = int(limits.PeakProcessMemoryUsed)
        allocated = int(report.get("allocated_bytes", 0))
        return {
            "executed": True,
            "passed": bool(report.get("memory_error")) and exited and 0 < allocated < cap and 0 < peak <= cap,
            "configured_bytes": cap,
            "allocated_before_error_bytes": allocated,
            "peak_process_memory_bytes": peak,
            "worker_memory_error": bool(report.get("memory_error")),
            "worker_exited": exited,
        }
    finally:
        job.close()
        if process is not None:
            process.terminate()
            process.close()


def probe_cpu_time_limit(runtime: Path) -> dict[str, Any]:
    cpu_limit_ms = 500
    report_path = runtime / "cpu.json"
    job = Job()
    process: NativeProcess | None = None
    try:
        job.set_extended_limits(process_cpu_ms=cpu_limit_ms)
        configured = job.extended_limits()
        process = _launch_suspended(
            [sys.executable, "-B", str(_worker_path()), "cpu", str(report_path), "30"],
            runtime,
        )
        _assign_or_raise(job, process)
        started = time.monotonic()
        process.resume()
        exited = process.wait(8000)
        deadline = time.monotonic() + 2.0
        accounting = job.accounting()
        while accounting.ActiveProcesses and time.monotonic() < deadline:
            time.sleep(0.02)
            accounting = job.accounting()
        elapsed_ms = round((time.monotonic() - started) * 1000)
        exit_code = process.exit_code()
        report_created = report_path.exists()
        accounting = job.accounting()
        job_cpu_ms = round((int(accounting.TotalUserTime) + int(accounting.TotalKernelTime)) / 10_000)
        configured_100ns = int(configured.BasicLimitInformation.PerProcessUserTimeLimit)
        flags = int(configured.BasicLimitInformation.LimitFlags)
        return {
            "executed": True,
            "passed": (
                exited
                and int(accounting.ActiveProcesses) == 0
                and exit_code not in (0, STILL_ACTIVE)
                and not report_created
                and flags & JOB_OBJECT_LIMIT_PROCESS_TIME != 0
                and configured_100ns == cpu_limit_ms * 10_000
                and exit_code == STATUS_QUOTA_EXCEEDED
                and job_cpu_ms > 0
                and elapsed_ms < 8000
            ),
            "configured_process_cpu_ms": configured_100ns // 10_000,
            "process_time_limit_flag": bool(flags & JOB_OBJECT_LIMIT_PROCESS_TIME),
            "wall_until_termination_ms": elapsed_ms,
            "job_accounted_cpu_ms": job_cpu_ms,
            "total_processes": int(accounting.TotalProcesses),
            "active_processes_after_limit": int(accounting.ActiveProcesses),
            "worker_exit_code": exit_code,
            "completion_report_created": report_created,
            "worker_exited": exited,
        }
    finally:
        job.close()
        if process is not None:
            process.terminate()
            process.close()


def probe_kill_on_close_descendant(runtime: Path) -> dict[str, Any]:
    report_path = runtime / "descendant.json"
    job = Job()
    root: NativeProcess | None = None
    child_handle: wintypes.HANDLE | None = None
    try:
        job.set_extended_limits()
        root = _launch_suspended([sys.executable, "-B", str(_worker_path()), "descendant", str(report_path)], runtime)
        _assign_or_raise(job, root)
        root.resume()
        report = _wait_json(report_path, root, 5.0)
        child_handle = kernel32.OpenProcess(
            SYNCHRONIZE | PROCESS_TERMINATE | PROCESS_QUERY_LIMITED_INFORMATION,
            False,
            int(report["child_pid"]),
        )
        if not child_handle:
            _raise_last_error()
        child_in_job = wintypes.BOOL()
        if not kernel32.IsProcessInJob(child_handle, job.handle, ctypes.byref(child_in_job)):
            _raise_last_error()
        deadline = time.monotonic() + 2.0
        active_before_close = int(job.accounting().ActiveProcesses)
        while active_before_close < 2 and time.monotonic() < deadline:
            time.sleep(0.02)
            active_before_close = int(job.accounting().ActiveProcesses)
        job.close()
        root_dead = root.wait(5000)
        child_dead = kernel32.WaitForSingleObject(child_handle, 5000) == WAIT_OBJECT_0
        return {
            "executed": True,
            "passed": active_before_close >= 2 and bool(child_in_job.value) and root_dead and child_dead,
            "active_processes_before_close": active_before_close,
            "descendant_in_job": bool(child_in_job.value),
            "root_dead_after_close": root_dead,
            "descendant_dead_after_close": child_dead,
        }
    finally:
        job.close()
        if root is not None:
            root.terminate()
            root.close()
        if child_handle:
            if kernel32.WaitForSingleObject(child_handle, 0) == WAIT_TIMEOUT:
                kernel32.TerminateProcess(child_handle, 98)
            _close_handle(child_handle)


def _token_information(token: wintypes.HANDLE, information_class: int) -> ctypes.Array[Any]:
    required = wintypes.DWORD()
    ctypes.set_last_error(0)
    advapi32.GetTokenInformation(token, information_class, None, 0, ctypes.byref(required))
    if ctypes.get_last_error() != ERROR_INSUFFICIENT_BUFFER or required.value == 0:
        _raise_last_error()
    buffer = ctypes.create_string_buffer(required.value)
    if not advapi32.GetTokenInformation(token, information_class, buffer, required, ctypes.byref(required)):
        _raise_last_error()
    return buffer


def _token_privileges(token: wintypes.HANDLE) -> tuple[int, int]:
    buffer = _token_information(token, TOKEN_PRIVILEGES_INFORMATION_CLASS)
    count = wintypes.DWORD.from_buffer_copy(buffer.raw[: ctypes.sizeof(wintypes.DWORD)]).value
    offset = ctypes.sizeof(wintypes.DWORD)
    item_size = ctypes.sizeof(LUID_AND_ATTRIBUTES)
    enabled = 0
    for index in range(count):
        item = LUID_AND_ATTRIBUTES.from_buffer_copy(buffer.raw[offset + index * item_size : offset + (index + 1) * item_size])
        enabled += bool(item.Attributes & SE_PRIVILEGE_ENABLED)
    return int(count), int(enabled)


def _sid_string(sid: wintypes.LPVOID) -> str:
    pointer = wintypes.LPWSTR()
    if not advapi32.ConvertSidToStringSidW(sid, ctypes.byref(pointer)):
        _raise_last_error()
    try:
        return pointer.value
    finally:
        kernel32.LocalFree(ctypes.cast(pointer, wintypes.HLOCAL))


def _create_restricted_token() -> tuple[wintypes.HANDLE, str, dict[str, int]]:
    original = wintypes.HANDLE()
    if not advapi32.OpenProcessToken(
        kernel32.GetCurrentProcess(),
        TOKEN_ASSIGN_PRIMARY | TOKEN_DUPLICATE | TOKEN_QUERY,
        ctypes.byref(original),
    ):
        _raise_last_error()
    try:
        user_buffer = _token_information(original, TOKEN_USER_INFORMATION_CLASS)
        token_user = ctypes.cast(user_buffer, ctypes.POINTER(TOKEN_USER)).contents
        current_sid = _sid_string(token_user.User.Sid)
        groups_buffer = _token_information(original, TOKEN_GROUPS_INFORMATION_CLASS)
        group_count = wintypes.DWORD.from_buffer_copy(groups_buffer.raw[: ctypes.sizeof(wintypes.DWORD)]).value
        groups_offset = TOKEN_GROUPS_HEADER.Groups.offset
        group_size = ctypes.sizeof(SID_AND_ATTRIBUTES)
        enabled_group_sids: list[wintypes.LPVOID] = []
        for index in range(group_count):
            group = SID_AND_ATTRIBUTES.from_address(ctypes.addressof(groups_buffer) + groups_offset + index * group_size)
            if group.Attributes & SE_GROUP_ENABLED:
                enabled_group_sids.append(group.Sid)
        restricting_sids = (SID_AND_ATTRIBUTES * (1 + len(enabled_group_sids)))(
            SID_AND_ATTRIBUTES(token_user.User.Sid, 0),
            *(SID_AND_ATTRIBUTES(pointer, 0) for pointer in enabled_group_sids),
        )
        restricted = wintypes.HANDLE()
        if not advapi32.CreateRestrictedToken(
            original,
            DISABLE_MAX_PRIVILEGE,
            0,
            None,
            0,
            None,
            len(restricting_sids),
            restricting_sids,
            ctypes.byref(restricted),
        ):
            _raise_last_error()
        try:
            original_total, original_enabled = _token_privileges(original)
            restricted_total, restricted_enabled = _token_privileges(restricted)
            return restricted, current_sid, {
                "original_total": original_total,
                "original_enabled": original_enabled,
                "restricted_total": restricted_total,
                "restricted_enabled": restricted_enabled,
                "restricting_sid_count": len(restricting_sids),
            }
        except Exception:
            _close_handle(restricted)
            raise
    finally:
        _close_handle(original)


def _set_test_acl(path: Path, sid: str) -> None:
    completed = subprocess.run(
        ["icacls.exe", str(path), "/inheritance:r", "/grant:r", f"*{sid}:(OI)(CI)F"],
        capture_output=True,
        text=True,
        timeout=5,
        check=False,
    )
    if completed.returncode != 0:
        raise RuntimeError(f"icacls failed with return code {completed.returncode}")


def _launch_restricted(token: wintypes.HANDLE, arguments: list[str], cwd: Path) -> tuple[NativeProcess, str]:
    command_line_text = subprocess.list2cmdline(arguments)
    startup = STARTUPINFOW(cb=ctypes.sizeof(STARTUPINFOW))
    information = PROCESS_INFORMATION()
    command_line = ctypes.create_unicode_buffer(command_line_text)
    if advapi32.CreateProcessAsUserW(
        token,
        None,
        command_line,
        None,
        None,
        False,
        CREATE_SUSPENDED | CREATE_NO_WINDOW,
        None,
        str(cwd),
        ctypes.byref(startup),
        ctypes.byref(information),
    ):
        return NativeProcess(information), "CreateProcessAsUserW"
    first_error = ctypes.get_last_error()
    startup = STARTUPINFOW(cb=ctypes.sizeof(STARTUPINFOW))
    information = PROCESS_INFORMATION()
    command_line = ctypes.create_unicode_buffer(command_line_text)
    if advapi32.CreateProcessWithTokenW(
        token,
        0,
        None,
        command_line,
        CREATE_SUSPENDED | CREATE_NO_WINDOW,
        None,
        str(cwd),
        ctypes.byref(startup),
        ctypes.byref(information),
    ):
        return NativeProcess(information), "CreateProcessWithTokenW"
    second_error = ctypes.get_last_error()
    raise OSError(second_error, f"restricted process launch failed; CreateProcessAsUserW={first_error}")


def probe_restricted_token(runtime: Path) -> dict[str, Any]:
    token: wintypes.HANDLE | None = None
    process: NativeProcess | None = None
    job = Job()
    try:
        job.set_extended_limits()
        token, current_sid, privilege_counts = _create_restricted_token()
        token_is_restricted = bool(advapi32.IsTokenRestricted(token))
        allowed = runtime / "allowed"
        group_only = runtime / "group-only"
        same_user_escape = runtime / "same-user-escape"
        for directory in (allowed, group_only, same_user_escape):
            directory.mkdir()
        _set_test_acl(allowed, current_sid)
        _set_test_acl(group_only, "S-1-5-4")
        _set_test_acl(same_user_escape, current_sid)

        allowed_input = allowed / "input.bin"
        group_only_input = group_only / "input.bin"
        same_user_escape_input = same_user_escape / "input.bin"
        allowed_input.write_bytes(b"allowed")
        group_only_input.write_bytes(b"group-only")
        same_user_escape_input.write_bytes(b"same-user")
        report_path = allowed / "report.json"

        system32 = Path(os.environ["SystemRoot"]) / "System32"
        cmd_executable = system32 / "cmd.exe"
        ping_executable = system32 / "ping.exe"
        if not cmd_executable.is_file() or not ping_executable.is_file():
            raise RuntimeError("Windows cmd.exe or ping.exe is unavailable")
        batch_path = allowed / "restricted-worker.cmd"

        def append_result(name: str, command: str) -> list[str]:
            return [
                command,
                f'if errorlevel 1 (>>"{report_path}" echo {name}=0) else (>>"{report_path}" echo {name}=1)',
            ]

        batch_lines = ["@echo off", f'>"{report_path}" echo started=1']
        batch_lines += append_result("allowed_user_acl_read", f'type "{allowed_input}" >nul 2>&1')
        batch_lines += append_result("allowed_user_acl_write", f'(echo bounded)>"{allowed / "output.bin"}" 2>nul')
        batch_lines += append_result("group_only_acl_read", f'type "{group_only_input}" >nul 2>&1')
        batch_lines += append_result("group_only_acl_write", f'(echo bounded)>"{group_only / "output.bin"}" 2>nul')
        batch_lines += append_result("same_user_escape_read", f'type "{same_user_escape_input}" >nul 2>&1')
        batch_lines += append_result("same_user_escape_write", f'(echo bounded)>"{same_user_escape / "output.bin"}" 2>nul')
        batch_lines += append_result(
            "network_loopback",
            f'"{ping_executable}" -n 1 -w 1000 127.0.0.1 >nul 2>&1',
        )
        batch_lines.append("exit /b 0")
        batch_path.write_text("\r\n".join(batch_lines) + "\r\n", encoding="utf-8")
        arguments = [
            str(cmd_executable),
            "/d",
            "/q",
            "/c",
            str(batch_path),
        ]
        process, launch_api = _launch_restricted(token, arguments, allowed)
        _assign_or_raise(job, process)
        process.resume()
        exited = process.wait(6000)
        if not exited:
            process.terminate()
        exit_code = process.exit_code()
        report: dict[str, bool] = {}
        if report_path.exists():
            for line in report_path.read_text(encoding="utf-8").splitlines():
                name, separator, value = line.partition("=")
                if separator:
                    report[name] = value == "1"
        privilege_reduced = (
            privilege_counts["restricted_total"] < privilege_counts["original_total"]
            or privilege_counts["restricted_enabled"] < privilege_counts["original_enabled"]
        )
        file_capability_observed = (
            report.get("allowed_user_acl_read") is True
            and report.get("allowed_user_acl_write") is True
            and report.get("group_only_acl_read") is True
            and report.get("group_only_acl_write") is True
        )
        same_user_escape_accessible = (
            report.get("same_user_escape_read") is True
            and report.get("same_user_escape_write") is True
        )
        loopback_accessible = report.get("network_loopback") is True
        return {
            "executed": True,
            "passed": (
                token_is_restricted
                and privilege_reduced
                and exited
                and exit_code == 0
                and file_capability_observed
                and same_user_escape_accessible
                and loopback_accessible
            ),
            "launch_api": launch_api,
            "token_is_restricted": token_is_restricted,
            "disable_max_privilege_observed": privilege_reduced,
            "privilege_counts": privilege_counts,
            "allowed_user_acl_read_write": report.get("allowed_user_acl_read") is True and report.get("allowed_user_acl_write") is True,
            "group_only_acl_accessible": report.get("group_only_acl_read") is True and report.get("group_only_acl_write") is True,
            "same_user_escape_accessible": same_user_escape_accessible,
            "loopback_network_accessible": loopback_accessible,
            "filesystem_confinement_proven": False,
            "network_denial_proven": False,
            "worker_exited": exited,
            "worker_exit_code": exit_code,
        }
    finally:
        job.close()
        if process is not None:
            process.terminate()
            process.close()
        _close_handle(token)


def _failed_probe(exc: Exception) -> dict[str, Any]:
    return {
        "attempted": True,
        "executed": False,
        "passed": False,
        "error_type": type(exc).__name__,
        "winerror": int(getattr(exc, "winerror", 0) or 0),
    }


def collect_windows_isolation_evidence() -> dict[str, Any]:
    evidence: dict[str, Any] = {
        "executed": True,
        "platform": platform.platform(),
        "python": platform.python_version(),
    }
    with tempfile.TemporaryDirectory(prefix="m0-008-provider-windows-") as temporary:
        runtime = Path(temporary)
        probes = {
            "active_process_limit": probe_active_process_limit,
            "process_memory_limit": probe_process_memory_limit,
            "cpu_time_limit": probe_cpu_time_limit,
            "kill_on_close_descendant": probe_kill_on_close_descendant,
            "restricted_token": probe_restricted_token,
        }
        for name, probe in probes.items():
            probe_runtime = runtime / name
            probe_runtime.mkdir(parents=True, exist_ok=True)
            try:
                evidence[name] = probe(probe_runtime)
            except Exception as exc:
                evidence[name] = _failed_probe(exc)
    evidence["job_object_all_passed"] = all(
        evidence[name].get("passed") is True
        for name in ("active_process_limit", "process_memory_limit", "cpu_time_limit", "kill_on_close_descendant")
    )
    evidence["restricted_token_candidate_executed"] = evidence["restricted_token"].get("passed") is True
    evidence["windows_network_filesystem_sandbox_proven"] = False
    return evidence


def main() -> int:
    evidence = collect_windows_isolation_evidence()
    print(json.dumps(evidence, ensure_ascii=False, indent=2, sort_keys=True))
    return 0 if evidence["job_object_all_passed"] and evidence["restricted_token_candidate_executed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
