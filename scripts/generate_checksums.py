#!/usr/bin/env python3
from __future__ import annotations
import hashlib
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'SHA256SUMS.txt'
rows=[]
for p in sorted(ROOT.rglob('*')):
    if not p.is_file() or p == OUT or '.git' in p.parts or '.runtime' in p.parts or '.docx-render' in p.parts:
        continue
    h=hashlib.sha256()
    with p.open('rb') as f:
        for chunk in iter(lambda:f.read(1024*1024), b''):
            h.update(chunk)
    rows.append(f'{h.hexdigest()}  {p.relative_to(ROOT).as_posix()}')
OUT.write_text('\n'.join(rows)+'\n', encoding='utf-8')
print(f'Wrote {len(rows)} checksums to {OUT}')
