# M0-001 交接摘要

## 完成状态

`ready_for_review`

## 完成内容

- 为 `scripts/codex-new-task.py` 增加 `--activate TASK_ID`，原地激活 registry 中的 `planned` 任务，保留依赖、模块和 handoff 路径。
- 增加依赖、重复激活、分支/worktree 冲突检查，并对 worktree、任务文件、handoff 文件和 registry 更新提供精确回滚。
- 增加技术栈中立的 `scripts/verify_repository.py`、CI 调用和临时 Git 仓库自动化测试。
- 补充 M0-002/M0-003/M0-004/M0-007 首轮并行文件所有权说明。

## 关键决策

保持新任务创建 CLI 兼容；预置任务使用显式 `--activate`，只有依赖已完成的 `planned` 项可激活。M0-009 仍负责语言级架构、完整安全和性能门禁。

## 修改文件

`scripts/codex-new-task.py`、`scripts/verify_repository.py`、`tests/repository/test_codex_new_task.py`、`.github/workflows/handoff-quality.yml`、`.codex/policies/FILE_OWNERSHIP.md`、`README.md` 及本交接文件。

## 模块边界、依赖方向与复用

## 新语言、框架或重大依赖

## 共享契约或数据库变化

## 测试结果

4 个 repository workflow tests 通过；handoff、architecture baseline、repository verification、JSON 解析、Python 编译和 `git diff --check` 均通过。

## 架构测试与质量门禁

仅新增仓库脚本与验证入口，无产品模块、依赖方向、共享契约或技术栈变化。当前验证明确不声称 M0-009 已完成。

## 文件安全、权限与性能影响

任务激活回滚只删除本次创建的 handoff/task 文件和本次 worktree/分支；测试全部使用临时 Git 仓库。无运行时或资产文件影响。

## 技术债、已知问题与风险

## 建议合并顺序

主协调线程评审并合并 M0-001 后，再激活并行的 M0-002、M0-003、M0-004、M0-007。

## 下一步

## Codex 线程链接（可选）

仅作为导航，不是唯一交接依据。
