#!/usr/bin/env python3
from __future__ import annotations

import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
ERRORS: list[str] = []

required = [
    'docs/22_编码与架构开发原则.md',
    '.codex/policies/CODE_QUALITY.md',
    '.codex/policies/LANGUAGE_BUDGET.md',
    '.codex/policies/MODULE_BOUNDARIES.md',
    '.codex/checklists/ARCHITECTURE_REVIEW.md',
    'tests/architecture/README.md',
    'tests/architecture/architecture-rules.json',
    'docs/adr/ADR-0011_模块化单体与端口适配器.md',
    'docs/adr/ADR-0012_语言与框架预算.md',
    'docs/adr/ADR-0013_架构质量门禁.md',
]
for rel in required:
    p = ROOT / rel
    if not p.is_file() or p.stat().st_size == 0:
        ERRORS.append(f'missing or empty: {rel}')

rules_path = ROOT / 'tests/architecture/architecture-rules.json'
if rules_path.exists():
    try:
        rules = json.loads(rules_path.read_text(encoding='utf-8'))
        if rules.get('architecture_style') != 'modular-monolith-ports-adapters-isolated-workers':
            ERRORS.append('unexpected architecture_style')
        if rules.get('full_enforcement_task') != 'M0-009':
            ERRORS.append('architecture rules must be finalized by M0-009')
    except Exception as exc:
        ERRORS.append(f'invalid architecture rules JSON: {exc}')

registry_path = ROOT / '.codex/task-registry.json'
graph_path = ROOT / '.codex/task-graph.json'
if registry_path.exists():
    registry = json.loads(registry_path.read_text(encoding='utf-8'))
    ids = {task.get('id') for task in registry.get('tasks', [])}
    if 'M0-009' not in ids:
        ERRORS.append('M0-009 missing from task registry')
if graph_path.exists():
    graph = json.loads(graph_path.read_text(encoding='utf-8'))
    node = next((n for n in graph.get('nodes', []) if n.get('id') == 'M0-009'), None)
    if node is None:
        ERRORS.append('M0-009 missing from task graph')
    else:
        expected = {f'M0-{i:03d}' for i in range(1, 9)}
        if not expected.issubset(set(node.get('depends_on', []))):
            ERRORS.append('M0-009 must converge all M0-001..M0-008 results')

agents = (ROOT / 'AGENTS.md').read_text(encoding='utf-8') if (ROOT / 'AGENTS.md').exists() else ''
for marker in ['模块化单体', '依赖方向', '语言与框架预算', 'architecture_review']:
    if marker not in agents:
        ERRORS.append(f'AGENTS.md marker missing: {marker}')

result_schema_path = ROOT / 'contracts/handoffs/task-result.schema.json'
result_template_path = ROOT / '.codex/handoffs/_template/result.json'
if result_schema_path.exists():
    schema = json.loads(result_schema_path.read_text(encoding='utf-8'))
    if 'architecture_review' not in schema.get('required', []):
        ERRORS.append('task result schema must require architecture_review')
if result_template_path.exists():
    template = json.loads(result_template_path.read_text(encoding='utf-8'))
    if 'architecture_review' not in template:
        ERRORS.append('task result template missing architecture_review')

for forbidden in ['common', 'utils', 'helpers']:
    if (ROOT / forbidden).exists():
        ERRORS.append(f'forbidden unbounded top-level directory: {forbidden}')

if ERRORS:
    for error in ERRORS:
        print('ERROR:', error)
    print(f'Architecture baseline validation failed: {len(ERRORS)} issue(s).')
    raise SystemExit(1)

print('Architecture baseline validation passed.')
print('Policy: modular monolith + ports/adapters + isolated workers')
print('Final enforcement task: M0-009')
