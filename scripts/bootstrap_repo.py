#!/usr/bin/env python3
from __future__ import annotations
from pathlib import Path
import subprocess, sys

root = Path(__file__).resolve().parents[1]

def run(*args: str) -> None:
    subprocess.run(args, cwd=root, check=True)

if not (root / '.git').exists():
    run('git', 'init', '-b', 'main')
    print('Initialized Git repository:', root)
else:
    print('Git repository already exists:', root)

runtime = root / '.runtime' / 'sandbox-storage'
runtime.mkdir(parents=True, exist_ok=True)
print('Created safe sandbox storage:', runtime)
print('Next: python scripts/validate_handoff.py')
