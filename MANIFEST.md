# Package Manifest

This list describes the original v2.1 handoff package. Its DOCX, delivery report and checksum file are historical artifacts. For the current source inventory, component status and checks, read `docs/audits/2026-09-05-alignment.md`, `docs/releases/V0.1_ALPHA_READINESS.md` and `tests/architecture/ci-tiers.json`.

## Core reading

- `DELIVERY_REPORT.md`
- `START_HERE.md`
- `AGENTS.md`
- `docs/00_交接总览.md` through `docs/22_编码与架构开发原则.md`
- `docs/资产管理系统_完整需求基线与架构交接_v2.1.docx`

## Visual and architecture assets

- `assets/diagrams/`: visual direction, overall architecture, domain framework and Codex workflow
- `assets/visuals/`: selected Windows, Web, Android, tablet, music and character UI concepts

## Codex orchestration

- `.codex/START_HERE.md`
- `.codex/project-state.json`
- `.codex/task-graph.json`
- `.codex/task-registry.json`
- `.codex/prompts/`
- `.codex/policies/`
- `.codex/checklists/ARCHITECTURE_REVIEW.md`
- `.codex/handoffs/_template/`
- `scripts/codex-start.ps1`
- `scripts/codex-start.sh`
- `scripts/codex-new-task.py`
- `scripts/validate_architecture_baseline.py`
- `tests/architecture/`
- `.github/workflows/handoff-quality.yml`

## Contract drafts

- `contracts/assetlink/`
- `contracts/events/`
- `contracts/operations/`
- `contracts/providers/`
- `contracts/handoffs/`

## Module skeleton

- `apps/`, `services/`, `packages/`, `providers/`, `gateways/`, `integrations/`, `infra/`

## Validation

```bash
python scripts/validate_handoff.py
python scripts/validate_architecture_baseline.py
python scripts/generate_checksums.py
```
