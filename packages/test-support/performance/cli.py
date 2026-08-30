from __future__ import annotations
import argparse, json
from .generator import GeneratorConfig, write_manifest
from .faults import build_fault_plan

def main() -> int:
    parser = argparse.ArgumentParser(description="M0-007 streaming asset fixture generator")
    parser.add_argument("--count", type=int, default=1000)
    parser.add_argument("--seed", type=int, default=0)
    parser.add_argument("--output", required=True)
    parser.add_argument("--fault-plan", action="store_true")
    args = parser.parse_args()
    if args.fault_plan:
        print(json.dumps(
            build_fault_plan(args.seed).__dict__,
            default=lambda x: x.value if hasattr(x, "value") else x,
            sort_keys=True,
        ))
    else:
        result = write_manifest(
            GeneratorConfig(count=args.count, seed=args.seed), args.output,
        )
        print(json.dumps(result, sort_keys=True))
    return 0

if __name__ == "__main__":
    raise SystemExit(main())
