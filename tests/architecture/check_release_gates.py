#!/usr/bin/env python3
"""Fail closed when an unresolved M0 decision blocks a named capability or release."""
from __future__ import annotations

import argparse
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DEFAULT_LEDGER = ROOT / "tests" / "architecture" / "m0-gates.json"


def load_ledger(path: Path = DEFAULT_LEDGER) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def blocking_gates(ledger: dict, target: str) -> list[dict]:
    return [
        gate
        for gate in ledger.get("gates", [])
        if gate.get("status") != "closed" and target in gate.get("blocked_targets", [])
    ]


def ledger_errors(ledger: dict) -> list[str]:
    errors: list[str] = []
    if ledger.get("version") != 1:
        errors.append("version must be 1")
    if ledger.get("decision_status") != "frozen":
        errors.append("decision_status must be frozen")
    gates = ledger.get("gates")
    if not isinstance(gates, list) or not gates:
        errors.append("gates must be a non-empty list")
    elif any(
        not isinstance(gate, dict)
        or not gate.get("id")
        or gate.get("status") not in {"open", "closed"}
        or not isinstance(gate.get("blocked_targets"), list)
        for gate in gates
    ):
        errors.append("every gate needs an id, valid status and blocked_targets list")
    return errors


def evaluate_target(ledger: dict, target: str) -> tuple[bool, list[dict]]:
    errors = ledger_errors(ledger)
    if errors:
        return False, [{"id": "LEDGER_INVALID", "decision": "; ".join(errors)}]
    if target == "v0.1-start" and ledger.get("v0_1_start_decision") != "authorized_for_scoped_implementation":
        return False, []
    blockers = blocking_gates(ledger, target)
    return not blockers, blockers


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--target", required=True, help="Capability or release target to evaluate.")
    parser.add_argument("--ledger", type=Path, default=DEFAULT_LEDGER)
    args = parser.parse_args()

    ledger = load_ledger(args.ledger)
    allowed, blockers = evaluate_target(ledger, args.target)
    if allowed:
        print(f"RELEASE_GATE_ALLOWED: {args.target}")
        return 0

    print(f"RELEASE_GATE_BLOCKED: {args.target}")
    if blockers:
        for gate in blockers:
            print(f"- {gate.get('id', '<missing-id>')}: {gate.get('decision', '<missing-decision>')}")
    else:
        print("- V0.1 scoped implementation has not been authorized by the M0 gate ledger.")
    return 3


if __name__ == "__main__":
    raise SystemExit(main())
