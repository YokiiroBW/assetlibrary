# V03-008 — Web 图片预览接入（准备阶段）

状态 partial。工作区 `C:/Users/Administrator/.codex/worktrees/a25c/AssetLibrary`；分支 `codex/v03-008-web-image-preview`；产品基线 `70ce45c743ba6d026e695c118d1927d291b37918`。

必读、现有 Web/会话/分页/虚拟化、V01-024 与 V03-001..004 交接已核查。具体调用链、实施清单、回归范围和冻结所需信息见 [implementation-plan.md](implementation-plan.md)。已向主协调任务 01a0801a-4c6c-7d92-b0be-ce0150f8b115 回报。

当前没有产品实现或可用图片预览。任务包标记 `awaiting_coordinator_freeze`，尚未收到已提交的唯一预览契约；按任务约定保持产品代码只读，不猜 endpoint/字段，不将已有 L0 详情称作预览。

已修正协调生成骨架中的结果枚举为 partial，并通过仓库基线校验。复用现有锁完成 SDK/Web 离线依赖准备，固定 Node24.20.0 的实际调用方式已核对。详情见 [tests.md](tests.md)。没有更改 SDK 源、共享契约、依赖锁、CI、后端、NAS 或个人资产。

后续收到主协调契约提交后，直接接入现有 AssetLink 请求入口、虚拟列表和导航/Modal；集中实现、diff 审查、Web 自测后由协调串接真实 Core/PG/HTTPS 合成样例。所有发布、Provider、原资产写入和完整 V0.3 门禁保持原状。
