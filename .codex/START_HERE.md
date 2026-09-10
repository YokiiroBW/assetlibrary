# Codex 主协调线程启动页

## 角色

你是主协调线程。你的首要产出是一个可并行、可审查、可回滚的工程计划，而不是一次性写完所有模块。

## 权威来源

- 需求：`docs/02_已确认需求基线.md`
- 非目标：`docs/18_已砍除与延期清单.md`
- 架构：`docs/05_总体架构与技术框架.md`
- 版本：`docs/16_版本路线与验收门禁.md`
- 工作流：`docs/17_Codex并行开发工作流.md`
- 编码与架构原则：`docs/22_编码与架构开发原则.md`
- 质量策略：`.codex/policies/CODE_QUALITY.md`、`.codex/policies/LANGUAGE_BUDGET.md`、`.codex/policies/MODULE_BOUNDARIES.md`
- 决策：`docs/adr/`
- 契约：`contracts/`
- 状态：`.codex/project-state.json`
- 任务：`.codex/task-graph.json`、`.codex/task-registry.json`

## 先做什么

1. 运行交接包与架构基线校验。
2. 检查已有 Git 分支、远端和 worktree，保留未合并提交；只在新仓库首次初始化。
3. 检查需求、ADR、矩阵与 `.codex/project-state.json` 是否一致；当前 M0-009 已完成架构冻结，V0.1 发布仍受阻断。
4. 使用 `scripts/codex-new-task.py` 创建独立 worktree；任务包与 handoff 骨架写入对应任务 worktree，协调仓库只维护任务登记。
5. 阅读 `docs/releases/WEB_WORKSPACE.md`、`.codex/handoffs/V01-024/summary.md` 和 `docs/releases/V0.1_ALPHA_READINESS.md`：当前NAS Web浏览/库管理交互已交付，V01-021仅代表原部署与最小只读链路；Windows试用保留，完整Alpha与资产写入门禁仍独立。历史对齐结论见 `docs/audits/2026-09-05-alignment.md`。
   原生首版最新状态见 `docs/releases/NATIVE_CLIENT_TRIAL.md` 与 `.codex/handoffs/V03-001/summary.md`：Android 只读 APK 已交付；用户明确 Windows 必须嵌入原生 Explorer，V03-002 已完成协议组件与测试验证，但实际入口尚未接通，没有 Windows 安装包。按 ADR-0018 从真实 Explorer 发现/界面输入阻断继续，不能退回以独立应用代替交付。
   当前接续V03-005：Windows最高优先级。v2观测工具已修复查询权限并通过实际掩码回归，真实自有Explorer观察30.844秒后触及4096非匹配上限，三API官方匹配0；成功脱离/存活/注册和UI清理，入口仍未接通。下一步先验证独立MyComputer父绑定方法的代码定位器，再裁定宿主观察。最新完整证据见V03-005/current-windows-work.json，不重跑旧COM捕获或提高上限，不动原用户Explorer。G1..G4/生产Shell与NAS平台决定保持独立。
6. 复用 M0-009 冻结的依赖方向、技术栈和 CI 命令，持续保留未关闭的 M0 residual gates。
7. 从已登记未完成项继续 V0.1；Windows/Android/Explorer 分别遵循对应版本和平台门禁。

## 不要做什么

- 不要从 UI 开始反推数据模型。
- 不要在 Explorer 中直接实现尚未在独立客户端稳定的复杂视图。
- 不要把 AI、WebDAV、MCP 或某个 Provider 做成核心依赖。
- 不要让多个窗口共用一个写工作目录。
- 不要只相信聊天里的“完成了”；必须看 commit、diff、测试和交接文件。
- 不要合并违反模块边界、复制核心逻辑或引入无 ADR 技术栈的代码。
