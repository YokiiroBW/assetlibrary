# M0-009 测试记录

## 执行环境

- Windows 11 x64
- Git 工作树：`C:\YOKI\Codex\AssetLibrary-worktrees\M0-009`
- 初始化基线：`6c00bbb`

## 执行命令

```text
python -m unittest tests.repository.test_codex_new_task -v
python scripts/validate_handoff.py
python scripts/validate_architecture_baseline.py
python scripts/verify_repository.py
git diff --check
```

## 架构与契约测试

- 初始化前：现有 handoff、架构基线和仓库验证均通过。
- M0-009 新增架构规则及正反夹具：尚未实现。

## 通过

- `tests.repository.test_codex_new_task`：8/8。
- 现有 handoff validator：通过。
- 现有 architecture baseline validator：通过。
- 现有 repository verification：通过。

## 失败 / 跳过

- 失败：0。
- 本记录未执行各平台外部门禁；不能将其视为跳过后通过。

## 故障注入与恢复验证

初始化阶段未执行；沿用 M0-001 至 M0-008 的既有证据作为只读输入。

## 性能数据

初始化阶段未新增性能数据。

## 尚未覆盖

- M0-009 任务包要求的证据裁决、架构门禁正反夹具和 CI 分层验证全部待执行。
