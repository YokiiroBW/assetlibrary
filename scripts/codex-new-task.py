#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
import urllib.parse
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
REGISTRY = ROOT / '.codex' / 'task-registry.json'
TEMPLATE = ROOT / '.codex' / 'prompts' / 'SUBTASK_TEMPLATE.md'
TASK_ID_RE = re.compile(r'^[A-Za-z0-9][A-Za-z0-9._-]{1,63}$')


def slugify(value: str) -> str:
    value = value.strip().lower()
    value = re.sub(r'[^a-z0-9\u4e00-\u9fff]+', '-', value).strip('-')
    return value[:48] or 'task'


def run(*args: str, cwd: Path = ROOT, capture: bool = False) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        args,
        cwd=cwd,
        check=True,
        text=True,
        capture_output=capture,
    )


def git_ref_exists(ref: str) -> bool:
    result = subprocess.run(
        ('git', 'rev-parse', '--verify', '--quiet', f'{ref}^{{commit}}'),
        cwd=ROOT,
        check=False,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
    )
    return result.returncode == 0


def write_task_files(task_root: Path, args: argparse.Namespace, branch: str, worktree: Path) -> tuple[Path, Path]:
    handoff_dir = task_root / '.codex' / 'handoffs' / args.task_id
    task_file = task_root / '.codex' / 'tasks' / f'{args.task_id}.md'

    if handoff_dir.exists() or task_file.exists():
        raise FileExistsError(f'Task files already exist for {args.task_id} in {task_root}')

    handoff_dir.mkdir(parents=True, exist_ok=False)
    task_file.parent.mkdir(parents=True, exist_ok=True)

    summary_template = (ROOT / '.codex/handoffs/_template/summary.md').read_text(encoding='utf-8')
    tests_template = (ROOT / '.codex/handoffs/_template/tests.md').read_text(encoding='utf-8')
    result_template = json.loads((ROOT / '.codex/handoffs/_template/result.json').read_text(encoding='utf-8'))

    (handoff_dir / 'summary.md').write_text(
        summary_template.replace('<TASK-ID>', args.task_id),
        encoding='utf-8',
    )
    (handoff_dir / 'tests.md').write_text(
        tests_template.replace('<TASK-ID>', args.task_id),
        encoding='utf-8',
    )

    result_template.update(
        {
            'task_id': args.task_id,
            'title': args.title,
            'milestone': args.milestone,
            'owner': args.owner,
            'branch': branch,
            'worktree': str(worktree),
            'handoff_summary': f'.codex/handoffs/{args.task_id}/summary.md',
        }
    )
    (handoff_dir / 'result.json').write_text(
        json.dumps(result_template, ensure_ascii=False, indent=2) + '\n',
        encoding='utf-8',
    )

    task_prompt = TEMPLATE.read_text(encoding='utf-8')
    replacements = {
        '<TASK-ID>': args.task_id,
        '<TITLE>': args.title,
        '<MILESTONE>': args.milestone,
        '<OWNER>': args.owner,
        'codex/<task-id>-<slug>': branch,
        '../worktrees/<task-id>': str(worktree),
        '<task-id>': args.task_id,
    }
    for old, new in replacements.items():
        task_prompt = task_prompt.replace(old, new)
    task_file.write_text(task_prompt, encoding='utf-8')
    return handoff_dir, task_file


def main() -> int:
    parser = argparse.ArgumentParser(description='Create an isolated Codex task worktree and handoff skeleton.')
    parser.add_argument('task_id')
    parser.add_argument('title')
    parser.add_argument('--milestone', default='M0')
    parser.add_argument('--base', default='main')
    parser.add_argument('--owner', default='unassigned')
    parser.add_argument('--worktrees-dir', default='../worktrees')
    parser.add_argument('--no-worktree', action='store_true', help='Only create registry/task files in the coordinator repository.')
    args = parser.parse_args()

    if not TASK_ID_RE.fullmatch(args.task_id):
        print('Invalid task ID. Use 2-64 letters, digits, dots, underscores or hyphens.', file=sys.stderr)
        return 2

    if not REGISTRY.is_file() or not TEMPLATE.is_file():
        print('Handoff registry or task template is missing.', file=sys.stderr)
        return 2

    if not args.no_worktree and not (ROOT / '.git').exists():
        print('Git repository is not initialized. Run scripts/bootstrap_repo.py first.', file=sys.stderr)
        return 2

    registry = json.loads(REGISTRY.read_text(encoding='utf-8'))
    if any(item.get('id') == args.task_id for item in registry.get('tasks', [])):
        print(f'Task ID already exists in registry: {args.task_id}', file=sys.stderr)
        return 3

    slug = slugify(args.title)
    branch = f'codex/{args.task_id.lower()}-{slug}'
    worktree = ROOT if args.no_worktree else (ROOT / args.worktrees_dir / args.task_id).resolve()

    if not args.no_worktree:
        if not git_ref_exists(args.base):
            print(f'Base Git ref does not exist: {args.base}', file=sys.stderr)
            print('Run scripts/bootstrap_repo.py and commit the baseline, or pass --base <ref>.', file=sys.stderr)
            return 4
        if worktree.exists():
            print(f'Worktree path already exists: {worktree}', file=sys.stderr)
            return 3
        worktree.parent.mkdir(parents=True, exist_ok=True)
        run('git', 'worktree', 'add', '-b', branch, str(worktree), args.base)

    task_root = ROOT if args.no_worktree else worktree
    try:
        handoff_dir, task_file = write_task_files(task_root, args, branch, worktree)
    except Exception as exc:
        if not args.no_worktree:
            subprocess.run(('git', 'worktree', 'remove', '--force', str(worktree)), cwd=ROOT, check=False)
            subprocess.run(('git', 'branch', '-D', branch), cwd=ROOT, check=False)
        print(f'Failed to create task files: {exc}', file=sys.stderr)
        return 5

    registry.setdefault('tasks', []).append(
        {
            'id': args.task_id,
            'title': args.title,
            'module': slug,
            'status': 'ready',
            'depends_on': [],
            'owner': args.owner,
            'branch': branch,
            'worktree': str(worktree),
            'task_file': f'.codex/tasks/{args.task_id}.md',
            'handoff': f'.codex/handoffs/{args.task_id}/summary.md',
            'thread_deep_link': '',
        }
    )
    REGISTRY.write_text(json.dumps(registry, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')

    prompt = (
        f'请执行任务 {args.task_id}：{args.title}。先读取 AGENTS.md、docs/22_编码与架构开发原则.md、'
        f'.codex/policies/、.codex/tasks/{args.task_id}.md 和相关需求/ADR；仅在该 worktree 修改；'
        '完成后提交代码、架构/契约测试和含 architecture_review 的标准交接文件。'
    )
    link = 'codex://new?path=' + urllib.parse.quote(str(worktree), safe='') + '&prompt=' + urllib.parse.quote(prompt, safe='')

    print('Task created:', args.task_id)
    print('Branch:', branch)
    print('Worktree:', worktree if not args.no_worktree else '(coordinator repository; no separate worktree)')
    print('Task file:', task_file)
    print('Handoff:', handoff_dir)
    print('Deep link:', link)
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
