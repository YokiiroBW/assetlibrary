from __future__ import annotations

import importlib.util
import json
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / 'scripts/codex-new-task.py'


class NewTaskTests(unittest.TestCase):
    def run_tool(self, repo: Path, *args: str) -> subprocess.CompletedProcess[str]:
        return subprocess.run([sys.executable, str(SCRIPT), *args], cwd=repo, text=True,
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
        subprocess.run(
            ['git', '-c', 'user.name=Test', '-c', 'user.email=test@example.invalid',
             'commit', '-qm', 'base'],
            cwd=repo, check=True,
        )
        return temp, repo

    def test_activate_updates_planned_entry_and_creates_worktree(self):
        temp, repo = self.setup_repo()
        self.addCleanup(temp.cleanup)
        result = subprocess.run(
            [sys.executable, 'scripts/codex-new-task.py', '--activate', 'M0-002', '--worktrees-dir', 'worktrees'],
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
        registry_mode = registry_path.stat().st_mode & 0o777
        if os.name == 'nt':
            self.assertTrue(registry_mode & 0o200)
            self.assertEqual(registry_mode & 0o111, 0)
        else:
            self.assertEqual(registry_mode, 0o644)

    def test_unmet_dependency_and_duplicate_activation_leave_no_worktree(self):
        temp, repo = self.setup_repo()
        self.addCleanup(temp.cleanup)
        data = json.loads((repo/'.codex/task-registry.json').read_text())
        data['tasks'][0]['status'] = 'ready'
        (repo/'.codex/task-registry.json').write_text(json.dumps(data), encoding='utf-8')
        result = subprocess.run(
            [sys.executable, 'scripts/codex-new-task.py', '--activate', 'M0-002', '--worktrees-dir', 'worktrees'],
            cwd=repo, text=True, capture_output=True,
        )
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse((repo/'worktrees/M0-002').exists())

    def test_new_id_cli_remains_available(self):
        temp, repo = self.setup_repo()
        self.addCleanup(temp.cleanup)
        result = subprocess.run(
            [sys.executable, 'scripts/codex-new-task.py', 'M0-010', 'A new spike', '--no-worktree'],
            cwd=repo, text=True, capture_output=True,
        )
        self.assertEqual(result.returncode, 0, result.stderr)

    def test_duplicate_activation_and_branch_collision_are_rejected(self):
        temp, repo = self.setup_repo()
        self.addCleanup(temp.cleanup)
        first = subprocess.run(
            [sys.executable, 'scripts/codex-new-task.py', '--activate', 'M0-002', '--worktrees-dir', 'worktrees'],
            cwd=repo, text=True, capture_output=True,
        )
        self.assertEqual(first.returncode, 0, first.stderr)
        second = subprocess.run(
            [sys.executable, 'scripts/codex-new-task.py', '--activate', 'M0-002', '--worktrees-dir', 'other'],
            cwd=repo, text=True, capture_output=True,
        )
        self.assertNotEqual(second.returncode, 0)
        self.assertFalse((repo/'other/M0-002').exists())

    def test_architecture_gate_can_record_explicit_partial_inputs(self):
        temp, repo = self.setup_repo()
        self.addCleanup(temp.cleanup)
        data = json.loads((repo / '.codex/task-registry.json').read_text())
        data['tasks'].append({
            'id': 'M0-008', 'title': 'Provider spike', 'module': 'provider-sandbox',
            'status': 'partial', 'depends_on': ['M0-001'],
            'handoff': '.codex/handoffs/M0-008/summary.md',
        })
        data['tasks'].append({
            'id': 'M0-009', 'title': 'Architecture gate',
            'module': 'architecture-quality-gate', 'status': 'planned',
            'depends_on': ['M0-001', 'M0-008'],
            'handoff': '.codex/handoffs/M0-009/summary.md',
        })
        (repo / '.codex/task-registry.json').write_text(json.dumps(data), encoding='utf-8')

        default_result = subprocess.run(
            [sys.executable, 'scripts/codex-new-task.py', '--activate', 'M0-009',
             '--worktrees-dir', 'worktrees'],
            cwd=repo, text=True, capture_output=True,
        )
        self.assertNotEqual(default_result.returncode, 0)
        self.assertFalse((repo / 'worktrees/M0-009').exists())

        accepted_result = subprocess.run(
            [sys.executable, 'scripts/codex-new-task.py', '--activate', 'M0-009',
             '--accept-partial-dependencies', '--worktrees-dir', 'worktrees'],
            cwd=repo, text=True, capture_output=True,
        )
        self.assertEqual(accepted_result.returncode, 0, accepted_result.stderr)
        registry = json.loads((repo / '.codex/task-registry.json').read_text())
        gate = next(item for item in registry['tasks'] if item['id'] == 'M0-009')
        provider = next(item for item in registry['tasks'] if item['id'] == 'M0-008')
        self.assertEqual(gate['accepted_partial_dependencies'], ['M0-008'])
        self.assertIn('blocking, deferred, or rejected', gate['partial_dependency_policy'])
        self.assertEqual(provider['status'], 'partial')

    def test_release_integration_gate_can_record_partial_packaging_input(self):
        temp, repo = self.setup_repo()
        self.addCleanup(temp.cleanup)
        data = json.loads((repo / '.codex/task-registry.json').read_text())
        data['tasks'].append({
            'id': 'V01-008', 'title': 'Packaging evidence',
            'module': 'packaging-release-evidence', 'status': 'partial',
            'depends_on': ['M0-001'],
            'handoff': '.codex/handoffs/V01-008/summary.md',
        })
        data['tasks'].append({
            'id': 'V01-009', 'title': 'Alpha release integration',
            'module': 'release-integration-gate', 'status': 'planned',
            'depends_on': ['V01-008'],
            'handoff': '.codex/handoffs/V01-009/summary.md',
        })
        (repo / '.codex/task-registry.json').write_text(json.dumps(data), encoding='utf-8')

        result = subprocess.run(
            [sys.executable, 'scripts/codex-new-task.py', '--activate', 'V01-009',
             '--accept-partial-dependencies', '--worktrees-dir', 'worktrees'],
            cwd=repo, text=True, capture_output=True,
        )
        self.assertEqual(result.returncode, 0, result.stderr)
        registry = json.loads((repo / '.codex/task-registry.json').read_text())
        gate = next(item for item in registry['tasks'] if item['id'] == 'V01-009')
        packaging = next(item for item in registry['tasks'] if item['id'] == 'V01-008')
        self.assertEqual(gate['accepted_partial_dependencies'], ['V01-008'])
        self.assertIn('v0.1-release gate blocking', gate['partial_dependency_policy'])
        self.assertEqual(packaging['status'], 'partial')

    def test_partial_dependency_override_is_restricted_to_architecture_gate(self):
        temp, repo = self.setup_repo()
        self.addCleanup(temp.cleanup)
        data = json.loads((repo / '.codex/task-registry.json').read_text())
        data['tasks'][0]['status'] = 'partial'
        (repo / '.codex/task-registry.json').write_text(json.dumps(data), encoding='utf-8')
        result = subprocess.run(
            [sys.executable, 'scripts/codex-new-task.py', '--activate', 'M0-002',
             '--accept-partial-dependencies', '--worktrees-dir', 'worktrees'],
            cwd=repo, text=True, capture_output=True,
        )
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('restricted to the architecture quality gate', result.stderr)
        self.assertFalse((repo / 'worktrees/M0-002').exists())

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
            [sys.executable, str(ROOT / 'scripts/verify_repository.py')],
            cwd=ROOT, text=True, capture_output=True,
        )
        self.assertEqual(result.returncode, 0, result.stderr)
        after = {path for path in ROOT.rglob('__pycache__')} | {path for path in ROOT.rglob('*.pyc')}
        self.assertEqual(after, before)


if __name__ == '__main__':
    unittest.main()
