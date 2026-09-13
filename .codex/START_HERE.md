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

V03-033已交付原生Explorer跨页版preview.9，来源1003157，V03-034/035/036集成。实际Core120图片100/20页、双向跨图、缩放复位/Esc选中/宽窄/退出清图通过；540安装hash匹配。258合成原件/6角色/临时Core与PG、自有2窗口/连接/Host全部清理，无活跃夹具，原Explorer和Gallery200保持。读V03-033 handoff及最新发布说明，不复用旧ready/PID。无限追加瀑布流和超宽缩略图预算仍后续优化；G4不做。


V03-030已交付原生Explorer缩放版preview.8：V03-031/032集成，最终67affa7包已安装，滚轮/鼠标平移/键盘/100%/fit/窄窗/切图清理及真实Core图片验证通过。540文件hash匹配；148合成原件、两轮Core/PG及自有窗口/连接均已清，无活跃夹具。原Explorer未重启，G4不做。读V03-030 handoff和最新Windows发布说明，不复用旧ready/PID或重建旧实例。

V03-026已完成同NAS图片交付：V03-027/028/029集成，最终四镜像包7f09290已冷备升级原NAS，原2库/195792扫描记录保持；38项真实Core请求、桌面/手机显示、中断恢复和正式库图片通过。NAS测试容器/5卷/网络/合成目录已清，无活跃夹具。读V03-026 handoff及docs/releases/NAS_IMAGE_PREVIEW.md；运行图片不依赖外置计算主机。不要重建旧测试或恢复被替代的外置主机建议。

此前交付：V03-022已完成ADR-0022大图与浏览偏好，V03-023/024/025集成，preview.7已安装并通过真实Core/Explorer验收。148合成原件未变、临时服务与自有窗口已清理；没有活跃夹具。恢复时读V03-022 handoff，不重建旧任务或重跑G4。

1. 运行交接包与架构基线校验。
2. 检查已有 Git 分支、远端和 worktree，保留未合并提交；只在新仓库首次初始化。
3. 检查需求、ADR、矩阵与 `.codex/project-state.json` 是否一致；当前 M0-009 已完成架构冻结，V0.1 发布仍受阻断。
4. 使用 `scripts/codex-new-task.py` 创建独立 worktree；任务包与 handoff 骨架写入对应任务 worktree，协调仓库只维护任务登记。
5. 阅读 `docs/releases/WEB_WORKSPACE.md`、`.codex/handoffs/V01-024/summary.md` 和 `docs/releases/V0.1_ALPHA_READINESS.md`：当前NAS Web浏览/库管理交互已交付，V01-021仅代表原部署与最小只读链路；Windows试用保留，完整Alpha与资产写入门禁仍独立。历史对齐结论见 `docs/audits/2026-09-05-alignment.md`。
   原生首版最新状态见 `docs/releases/WINDOWS_EXPLORER_PREVIEW.md` 与 `docs/releases/NATIVE_CLIENT_TRIAL.md`：Windows11 x64原生Explorer图库与大图浏览preview.7及Android只读APK已交付。Windows采用进程外Host和辅助WinUI连接设置；没有独立资产浏览客户端。
   V03-005本轮图库工作已完成：V03-019/020/021集成，实际图片/分页/中文导航/选择/自有计数摘要/宽窄/退出清理通过，证据 `.codex/handoffs/V03-005/windows-gallery-delivery/README.md`。测试Core148原件/6角色与服务清理，所有自有可见窗口关闭，测试配置移除，正式安装保留。Windows11底部旧计数以图库工具栏摘要为准；NAS图片引擎后由V03-026同机交付；缩放/原件编辑/同步、手工认证/系统卸载UI、签名及Android真机仍独立。
   `.codex/handoffs/V03-005/gallery-progress.json`仅用于恢复交付状态；无活跃图片夹具，不重建worktree或重跑旧门禁。G1/G2/G3沿用实测，G4用户豁免未执行，不得安排20轮/8小时。原Explorer未重启，完整V0.3未宣布完成。
6. 复用 M0-009 冻结的依赖方向、技术栈和 CI 命令，持续保留未关闭的 M0 residual gates。
7. 从已登记未完成项继续 V0.1；Windows/Android/Explorer 分别遵循对应版本和平台门禁。

## 不要做什么

- 不要从 UI 开始反推数据模型。
- 不要在 Explorer 中直接实现尚未在独立客户端稳定的复杂视图。
- 不要把 AI、WebDAV、MCP 或某个 Provider 做成核心依赖。
- 不要让多个窗口共用一个写工作目录。
- 不要只相信聊天里的“完成了”；必须看 commit、diff、测试和交接文件。
- 不要合并违反模块边界、复制核心逻辑或引入无 ADR 技术栈的代码。
