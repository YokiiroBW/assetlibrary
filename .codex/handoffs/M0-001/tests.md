# M0-001 测试记录

## 执行环境

Linux worktree，Python 3，临时目录 Git 仓库测试。

## 执行命令

- `python3 -m unittest discover -s tests/repository -p 'test_*.py'`：4 passed
- `python3 scripts/validate_handoff.py`：passed
- `python3 scripts/validate_architecture_baseline.py`：passed
- `python3 scripts/verify_repository.py`：passed
- `python3 -m json.tool .codex/task-registry.json`：passed
- `git diff --check`：passed

## 架构与契约测试

## 通过

覆盖新 ID 创建、planned 任务激活、保留依赖/模块/handoff、未满足依赖、重复激活、分支冲突和无 worktree 残留。

## 失败 / 跳过

## 故障注入与恢复验证

## 性能数据

## 尚未覆盖

M0-009 前不执行语言特定依赖图、完整安全和真实性能门禁。
