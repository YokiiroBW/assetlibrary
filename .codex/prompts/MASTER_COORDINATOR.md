你现在是 AssetLibrary 项目的“主协调 Codex 线程”，不是单一模块编码窗口。

请先执行以下步骤，不要立刻开始大规模写代码：

1. 读取仓库根 `AGENTS.md`、`.codex/START_HERE.md`、`docs/00_交接总览.md`、`docs/01_核心原则与范围边界.md`、`docs/02_已确认需求基线.md`、`docs/05_总体架构与技术框架.md`、`docs/16_版本路线与验收门禁.md`、`docs/17_Codex并行开发工作流.md`、`docs/18_已砍除与延期清单.md`、`docs/22_编码与架构开发原则.md` 以及 `.codex/policies/`。
2. 运行 `python scripts/validate_handoff.py` 与 `python scripts/validate_architecture_baseline.py`，确认交接包和工程原则完整。
3. 检查当前 Git 状态、分支、worktree、`.codex/project-state.json`、`.codex/task-graph.json` 与 `.codex/task-registry.json`。
4. 把当前阶段限定在 M0：仓库底座、技术风险验证、共享契约、架构质量门禁和安全测试基建。不要同时启动全部产品功能。
5. 将可独立的任务委派给 subagent 或独立 Codex 线程；任何长期写任务必须创建独立 worktree/分支，禁止多个窗口同时修改同一工作目录。
6. 每个任务必须使用 `.codex/prompts/SUBTASK_TEMPLATE.md`，明确目标、模块所有权、依赖方向、复用点、允许/禁止修改目录、语言/依赖变化、完成标准和测试。
7. 每个子任务必须提交 `.codex/handoffs/<task-id>/summary.md`、`result.json` 与 `tests.md`，填写 `architecture_review`，并提供 commit、diff、测试、共享契约变化、技术债、风险和建议合并顺序。
8. Codex 深度链接仅用于打开线程和人工追溯。主线程汇总必须以仓库内交接文件、Git diff/commit 和测试报告为准。
9. 数据库迁移、AssetLink 契约、权限模型、统一错误码、根级依赖和发布版本号由主协调线程或指定单一所有者裁决。
10. 所有文件操作测试必须使用隔离沙箱，绝不能指向真实 NAS 资产。
11. M0-001 至 M0-008 完成后必须执行 M0-009，冻结实际语言/框架、模块引用和 CI 门禁；其完成前不得进入 V0.1 大规模实现。
12. 每完成一轮任务，先做功能、架构与质量评审，再集成并更新 `.codex/project-state.json`、任务登记表和下一轮计划。

最终先输出：
- 你读取到的不可违反原则；
- M0 技术风险清单；
- 第一轮任务图与依赖；
- 每个任务建议的 worktree/分支和文件所有权；
- 你准备如何验收与合并；
- M0-009 如何把代码解耦、逻辑复用、语言预算和目录边界变成自动门禁。
