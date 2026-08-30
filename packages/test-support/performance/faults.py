"""Data-driven, non-destructive failure scheduling for tests."""
from __future__ import annotations
from dataclasses import dataclass
from enum import StrEnum

class FaultKind(StrEnum):
    NETWORK_OFFLINE = "network_offline"
    PROCESS_CRASH = "process_crash"
    SERVICE_RESTART = "service_restart"
    DISK_FULL = "disk_full"
    NAME_COLLISION = "name_collision"
    HASH_CHANGED = "hash_changed"
    PERMISSION_DENIED = "permission_denied"
    PARTIAL_WRITE = "partial_write"
    RETRY_DUPLICATE = "retry_duplicate"

@dataclass(frozen=True)
class FaultPlan:
    schema_version: str
    plan_id: str
    events: tuple[dict[str, object], ...]

def build_fault_plan(seed: int = 0) -> FaultPlan:
    kinds = tuple(FaultKind)
    events = tuple({"sequence": i, "kind": kind.value, "trigger": "before_commit", "recoverable": kind not in (FaultKind.PERMISSION_DENIED, FaultKind.NAME_COLLISION)} for i, kind in enumerate(kinds))
    return FaultPlan("m0-007.fault-plan.v1", f"fault-plan-{seed:08x}", events)
