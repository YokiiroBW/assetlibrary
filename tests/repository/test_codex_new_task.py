from __future__ import annotations

import importlib.util
import json
import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

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
        registry_path = repo / '.codex/task-registry.json'
        registry_path.write_text(json.dumps({'tasks': [
            {'id': 'M0-001', 'title': 'Foundation', 'module': 'repo-foundation',
             'status': 'completed', 'depends_on': [], 'handoff': '.codex/handoffs/M0-001/summary.md'},
            {'id': 'M0-002', 'title': 'Shell spike', 'module': 'shell', 'status': 'planned',
             'depends_on': ['M0-001'], 'handoff': '.codex/handoffs/M0-002/summary.md'},
        ]}), encoding='utf-8')
        registry_path.chmod(0o644)
        (repo / '.codex/prompts').mkdir(parents=True)
        (repo / '.codex/prompts/SUBTASK_TEMPLATE.md').write_text(
            '<TASK-ID> <TITLE> <MILESTONE> <OWNER> codex/<task-id>-<slug> ../worktrees/<task-id>',
            encoding='utf-8',
        )
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
        result = subprocess.run(
            ['python3', 'scripts/codex-new-task.py', '--activate', 'M0-002', '--worktrees-dir', 'worktrees'],
            cwd=repo, text=True, capture_output=True,
        )
        self.assertEqual(result.returncode, 0, result.stderr)
        registry = json.loads((repo / '.codex/task-registry.json').read_text())
        item = next(x for x in registry['tasks'] if x['id'] == 'M0-002')
        self.assertEqual(item['status'], 'ready')
        self.assertEqual(item['depends_on'], ['M0-001'])
        self.assertEqual(item['module'], 'shell')
        self.assertEqual(item['handoff'], '.codex/handoffs/M0-002/summary.md')
        self.assertTrue((repo / 'worktrees/M0-002/.codex/tasks/M0-002.md').exists())
        registry_path = repo / '.codex/task-registry.json'
        self.assertEqual(registry_path.stat().st_mode & 0o777, 0o644)

    def test_unmet_dependency_and_duplicate_activation_leave_no_worktree(self):
        temp, repo = self.setup_repo()
        self.addCleanup(temp.cleanup)
        data = json.loads((repo/'.codex/task-registry.json').read_text())
        data['tasks'][0]['status'] = 'ready'
        (repo/'.codex/task-registry.json').write_text(json.dumps(data), encoding='utf-8')
        result = subprocess.run(
            ['python3', 'scripts/codex-new-task.py', '--activate', 'M0-002', '--worktrees-dir', 'worktrees'],
            cwd=repo, text=True, capture_output=True,
        )
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse((repo/'worktrees/M0-002').exists())

    def test_new_id_cli_remains_available(self):
        temp, repo = self.setup_repo()
        self.addCleanup(temp.cleanup)
        result = subprocess.run(
            ['python3', 'scripts/codex-new-task.py', 'M0-010', 'A new spike', '--no-worktree'],
            cwd=repo, text=True, capture_output=True,
        )
        self.assertEqual(result.returncode, 0, result.stderr)

    def test_duplicate_activation_and_branch_collision_are_rejected(self):
        temp, repo = self.setup_repo()
        self.addCleanup(temp.cleanup)
        first = subprocess.run(
            ['python3', 'scripts/codex-new-task.py', '--activate', 'M0-002', '--worktrees-dir', 'worktrees'],
            cwd=repo, text=True, capture_output=True,
        )
        self.assertEqual(first.returncode, 0, first.stderr)
        second = subprocess.run(
            ['python3', 'scripts/codex-new-task.py', '--activate', 'M0-002', '--worktrees-dir', 'other'],
            cwd=repo, text=True, capture_output=True,
        )
        self.assertNotEqual(second.returncode, 0)
        self.assertFalse((repo/'other/M0-002').exists())

    def test_task_file_write_failure_removes_partial_artifacts(self):
        spec = importlib.util.spec_from_file_location('new_task', SCRIPT)
        module = importlib.util.module_from_spec(spec)
        assert spec.loader
        spec.loader.exec_module(module)
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        root = Path(temp.name)
        args = type('Args', (), {'task_id': 'M0-099', 'title': 'Failure', 'milestone': 'M0', 'owner': 'test'})()
        original = Path.write_text
        def fail_task(path, data, *call_args, **call_kwargs):
            if path.name == 'M0-099.md':
                raise OSError('injected task-file failure')
            return original(path, data, *call_args, **call_kwargs)
        with patch.object(module.Path, 'write_text', fail_task):
            with self.assertRaises(OSError):
                module.write_task_files(root, args, 'codex/m0-099-failure', root / 'worktree')
        self.assertFalse((root / '.codex/handoffs/M0-099').exists())
        self.assertFalse((root / '.codex/tasks').exists())

    def test_repository_verification_has_no_python_cache_residue(self):
        before = {path for path in ROOT.rglob('__pycache__')} | {path for path in ROOT.rglob('*.pyc')}
        self.assertEqual(before, set(), 'repository must start cache-free')
        result = subprocess.run(
            ['python3', str(ROOT / 'scripts/verify_repository.py')],
            cwd=ROOT, text=True, capture_output=True,
        )
        self.assertEqual(result.returncode, 0, result.stderr)
        after = {path for path in ROOT.rglob('__pycache__')} | {path for path in ROOT.rglob('*.pyc')}
        self.assertEqual(after, before)


if __name__ == '__main__':
    unittest.main()
