from __future__ import annotations

import json
import subprocess
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / 'scripts/codex-new-task.py'


class NewTaskTests(unittest.TestCase):
    def run_tool(self, repo: Path, *args: str) -> subprocess.CompletedProcess[str]:
        return subprocess.run(['python3', str(SCRIPT), *args], cwd=repo, text=True,
                              capture_output=True)

    def setup_repo(self):
        temp = tempfile.TemporaryDirectory()
        repo = Path(temp.name)
        subprocess.run(['git', 'init', '-q', '-b', 'main'], cwd=repo, check=True)
        (repo / '.codex').mkdir()
        # Point the script's repository-relative inputs at a tiny disposable fixture.
        (repo / '.codex/task-registry.json').write_text(json.dumps({'tasks': [
            {'id': 'M0-001', 'title': 'Foundation', 'module': 'repo-foundation',
             'status': 'completed', 'depends_on': [], 'handoff': '.codex/handoffs/M0-001/summary.md'},
            {'id': 'M0-002', 'title': 'Shell spike', 'module': 'shell', 'status': 'planned',
             'depends_on': ['M0-001'], 'handoff': '.codex/handoffs/M0-002/summary.md'},
        ]}), encoding='utf-8')
        (repo / '.codex/prompts').mkdir(parents=True)
        (repo / '.codex/prompts/SUBTASK_TEMPLATE.md').write_text('<TASK-ID> <TITLE> <MILESTONE> <OWNER> codex/<task-id>-<slug> ../worktrees/<task-id>', encoding='utf-8')
        (repo / '.codex/handoffs/_template').mkdir(parents=True)
        for name, body in [('summary.md', '<TASK-ID>'), ('tests.md', '<TASK-ID>')]:
            (repo / '.codex/handoffs/_template' / name).write_text(body, encoding='utf-8')
        (repo / '.codex/handoffs/_template/result.json').write_text('{}', encoding='utf-8')
        (repo / 'scripts').mkdir()
        (repo / 'scripts/codex-new-task.py').write_text(SCRIPT.read_text(encoding='utf-8'), encoding='utf-8')
        (repo / '.gitignore').write_text('', encoding='utf-8')
        subprocess.run(['git', 'add', '.'], cwd=repo, check=True)
        subprocess.run(['git', 'commit', '-qm', 'base'], cwd=repo, check=True)
        return temp, repo

    def test_activate_updates_planned_entry_and_creates_worktree(self):
        temp, repo = self.setup_repo()
        self.addCleanup(temp.cleanup)
        result = subprocess.run(['python3', 'scripts/codex-new-task.py', '--activate', 'M0-002', '--worktrees-dir', 'worktrees'], cwd=repo, text=True, capture_output=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        item = next(x for x in json.loads((repo/'.codex/task-registry.json').read_text())['tasks'] if x['id'] == 'M0-002')
        self.assertEqual(item['status'], 'ready')
        self.assertEqual(item['depends_on'], ['M0-001'])
        self.assertEqual(item['module'], 'shell')
        self.assertTrue((repo/'worktrees/M0-002/.codex/tasks/M0-002.md').exists())

    def test_unmet_dependency_and_duplicate_activation_leave_no_worktree(self):
        temp, repo = self.setup_repo()
        self.addCleanup(temp.cleanup)
        data = json.loads((repo/'.codex/task-registry.json').read_text())
        data['tasks'][0]['status'] = 'ready'
        (repo/'.codex/task-registry.json').write_text(json.dumps(data), encoding='utf-8')
        result = subprocess.run(['python3', 'scripts/codex-new-task.py', '--activate', 'M0-002', '--worktrees-dir', 'worktrees'], cwd=repo, text=True, capture_output=True)
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse((repo/'worktrees/M0-002').exists())

    def test_new_id_cli_remains_available(self):
        temp, repo = self.setup_repo()
        self.addCleanup(temp.cleanup)
        result = subprocess.run(['python3', 'scripts/codex-new-task.py', 'M0-010', 'A new spike', '--no-worktree'], cwd=repo, text=True, capture_output=True)
        self.assertEqual(result.returncode, 0, result.stderr)

    def test_duplicate_activation_and_branch_collision_are_rejected(self):
        temp, repo = self.setup_repo()
        self.addCleanup(temp.cleanup)
        first = subprocess.run(['python3', 'scripts/codex-new-task.py', '--activate', 'M0-002', '--worktrees-dir', 'worktrees'], cwd=repo, text=True, capture_output=True)
        self.assertEqual(first.returncode, 0, first.stderr)
        second = subprocess.run(['python3', 'scripts/codex-new-task.py', '--activate', 'M0-002', '--worktrees-dir', 'other'], cwd=repo, text=True, capture_output=True)
        self.assertNotEqual(second.returncode, 0)
        self.assertFalse((repo/'other/M0-002').exists())


if __name__ == '__main__':
    unittest.main()
