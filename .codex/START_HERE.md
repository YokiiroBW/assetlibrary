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
   当前接续V03-005：Web/Android真实图片与共同清理已验收，Android另完成旧服务图片404回退修复与候选。Windows已恢复真实GUI操作；独立Desktop根绑定通过，三类实际Explorer入口均未激活扩展，同源码静态CRT对照也未激活；测试现场已清理，用户已授权限时系统跟踪：首轮注册正控通过后触发PID筛选保护并清理，第二轮Windows权限启动返回取消；工具已补安全头字段诊断，现场已清理，等用户准备好系统提示后按原授权继续；原测试窗口操作授权持续有效。NAS包a12b0d1已构建并暂存，实测内核缺少seccomp，图片未启用；nas-platform-decision.md中的内部Linux解码服务方案待用户/ADR确认，不自行改安全策略。复用已有窗口与summary/tests，不重建任务或把开发Linux成功当NAS通过。
6. 复用 M0-009 冻结的依赖方向、技术栈和 CI 命令，持续保留未关闭的 M0 residual gates。
7. 从已登记未完成项继续 V0.1；Windows/Android/Explorer 分别遵循对应版本和平台门禁。

## 不要做什么

- 不要从 UI 开始反推数据模型。
- 不要在 Explorer 中直接实现尚未在独立客户端稳定的复杂视图。
- 不要把 AI、WebDAV、MCP 或某个 Provider 做成核心依赖。
- 不要让多个窗口共用一个写工作目录。
- 不要只相信聊天里的“完成了”；必须看 commit、diff、测试和交接文件。
- 不要合并违反模块边界、复制核心逻辑或引入无 ADR 技术栈的代码。
