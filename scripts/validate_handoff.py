#!/usr/bin/env python3
from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
REQUIRED = [
    'README.md', 'START_HERE.md', 'AGENTS.md', 'CODEX_HANDOFF_PROMPT.txt',
    '.codex/START_HERE.md', '.codex/project-state.json', '.codex/task-graph.json', '.codex/task-registry.json',
    '.codex/policies/CODE_QUALITY.md', '.codex/policies/LANGUAGE_BUDGET.md', '.codex/policies/MODULE_BOUNDARIES.md',
    '.codex/checklists/ARCHITECTURE_REVIEW.md',
    'docs/00_交接总览.md', 'docs/01_核心原则与范围边界.md', 'docs/02_已确认需求基线.md',
    'docs/05_总体架构与技术框架.md', 'docs/16_版本路线与验收门禁.md',
    'docs/17_Codex并行开发工作流.md', 'docs/18_已砍除与延期清单.md',
    'docs/22_编码与架构开发原则.md',
    'docs/adr/ADR-0011_模块化单体与端口适配器.md',
    'docs/adr/ADR-0012_语言与框架预算.md',
    'docs/adr/ADR-0013_架构质量门禁.md',
    'docs/matrices/FORMAT_SUPPORT_MATRIX.csv', 'docs/matrices/CLIENT_CAPABILITY_MATRIX.csv',
    'tests/architecture/README.md', 'tests/architecture/architecture-rules.json',
    'scripts/validate_architecture_baseline.py', '.github/workflows/handoff-quality.yml',
    'assets/diagrams/02_系统总体架构图.svg', 'assets/diagrams/03_领域模块框架图.svg',
    'assets/diagrams/04_Codex并行开发框架图.svg',
    'contracts/assetlink/handshake.schema.json', 'contracts/operations/operation-plan.schema.json',
    'contracts/providers/provider-manifest.schema.json', 'contracts/events/domain-event.schema.json',
    'contracts/handoffs/task-result.schema.json',
]

errors: list[str] = []
for rel in REQUIRED:
    p = ROOT / rel
    if not p.exists() or (p.is_file() and p.stat().st_size == 0):
        errors.append(f'missing or empty: {rel}')

for rel in [
    '.codex/project-state.json', '.codex/task-graph.json', '.codex/task-registry.json',
    'tests/architecture/architecture-rules.json', 'contracts/handoffs/task-result.schema.json',
    '.codex/handoffs/_template/result.json',
]:
    p = ROOT / rel
    if p.exists():
        try:
            json.loads(p.read_text(encoding='utf-8'))
        except Exception as exc:
            errors.append(f'invalid JSON {rel}: {exc}')

baseline = (ROOT / 'docs/02_已确认需求基线.md').read_text(encoding='utf-8') if (ROOT / 'docs/02_已确认需求基线.md').exists() else ''
rejected = (ROOT / 'docs/18_已砍除与延期清单.md').read_text(encoding='utf-8') if (ROOT / 'docs/18_已砍除与延期清单.md').exists() else ''
engineering = (ROOT / 'docs/22_编码与架构开发原则.md').read_text(encoding='utf-8') if (ROOT / 'docs/22_编码与架构开发原则.md').exists() else ''
for phrase in ['Komga集成', '项目依赖自动收集', 'DWG首期内置预览']:
    if phrase not in rejected:
        errors.append(f'rejected scope marker missing: {phrase}')
for phrase in ['50 万资产', '100GB', 'Windows x64 原生', 'Linux x86-64']:
    if phrase not in baseline:
        errors.append(f'confirmed baseline marker missing: {phrase}')
for phrase in ['模块化单体', '端口与适配器', '业务逻辑只实现一次', '语言与框架预算', 'CI 合并门禁']:
    if phrase not in engineering:
        errors.append(f'engineering principle marker missing: {phrase}')

try:
    result = subprocess.run(
        [sys.executable, str(ROOT / 'scripts/validate_architecture_baseline.py')],
        cwd=ROOT,
        text=True,
        capture_output=True,
        check=False,
    )
    if result.returncode != 0:
        errors.append('architecture baseline validation failed:\n' + result.stdout + result.stderr)
except Exception as exc:
    errors.append(f'cannot run architecture baseline validator: {exc}')

if errors:
    for error in errors:
        print('ERROR:', error)
    print(f'Validation failed: {len(errors)} issue(s).')
    raise SystemExit(1)

print('Handoff validation passed.')
print('Root:', ROOT)
print('Required artifacts:', len(REQUIRED))
print('Architecture quality gate: M0-009')
