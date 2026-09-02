#!/usr/bin/env python3
"""Fast, dependency-free repository checks used by the M0-009 merge gate."""
from __future__ import annotations

import json
import ast
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

def main() -> int:
    commands = [
        [sys.executable, str(ROOT / 'scripts/validate_handoff.py')],
        [sys.executable, str(ROOT / 'scripts/validate_architecture_baseline.py')],
        [sys.executable, str(ROOT / 'scripts/validate_dotnet_source.py')],
        [
            sys.executable,
            '-B',
            '-m',
            'unittest',
            'discover',
            '-s',
            'tests/architecture',
            '-p',
            'test_*.py',
            '-v',
        ],
    ]
    for command in commands:
        subprocess.run(command, cwd=ROOT, check=True)
    for path in (ROOT / '.codex').glob('**/*.json'):
        json.loads(path.read_text(encoding='utf-8'))
    for path in (*((ROOT / 'scripts').glob('*.py')), *((ROOT / 'tests').glob('**/*.py'))):
        ast.parse(path.read_text(encoding='utf-8'), filename=str(path))
    print('Repository verification passed (M0-009 fast architecture gate).')
    return 0

if __name__ == '__main__':
    raise SystemExit(main())
