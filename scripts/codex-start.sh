#!/usr/bin/env bash
set -euo pipefail
REPO="$(cd "$(dirname "$0")/.." && pwd)"
python3 - "$REPO" <<'PYCODE'
import sys, urllib.parse
repo=sys.argv[1]
prompt='请作为本仓库的主协调Codex线程，先读取 .codex/START_HERE.md 和 AGENTS.md，运行 python scripts/validate_handoff.py；不要立即大规模编码。'
print('codex://new?path='+urllib.parse.quote(repo, safe='')+'&prompt='+urllib.parse.quote(prompt, safe=''))
PYCODE
