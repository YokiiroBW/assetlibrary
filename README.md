# AssetLibrary 资产管理系统

**交接包版本：v2.1**  
**需求冻结日期：2026-08-31**

这是一个面向自建 NAS、Windows、Android 和 Web 的资产预览与管理系统仓库。它将真实物理文件、跨格式预览、搜索、同步、整理、查重、角色资料与外部系统连接统一到一套可维护的资产内核中。

本包包含：

- 已确认需求基线；
- 已砍除、延期和明确不做的范围；
- 文本化的总体架构、领域边界、协议与数据设计；
- 视觉方向总览、总体架构图、领域框架图和 Codex 并行开发框架图；
- 格式支持矩阵、客户端功能矩阵、版本路线和验收门禁；
- Codex 主协调线程、worktree、任务登记和标准交接模板；
- 已冻结的 AssetLink 契约与 .NET、TypeScript、Kotlin 生成 SDK；
- .NET 核心、18 条生产迁移、可部署的 HTTPS Web 只读试用、认证、沙箱文件操作与 Windows/Linux/Docker 共用宿主；
- 强制编码/架构原则、语言预算和已激活的原生 CI 门禁。

当前已完成 **NAS Docker 服务端与第一版 Web**，并保留 Windows x64 只读试用：登录、管理员初始化/恢复、登记真实物理目录、首次扫描、进度/取消/失败重试、浏览和名称/路径搜索。运行包使用显式试用入口；默认健康宿主行为兼容。NAS 使用入口见 [NAS Web 交付说明](docs/releases/NAS_READ_ONLY_WEB.md)，Windows 入口见 [只读试用说明](docs/releases/READ_ONLY_TRIAL.md)。

完整 V0.1 Alpha 仍不可发布。V01-008 平台发行证据保持 partial；预览、增量/通用重扫、资产写入、Windows 独立客户端、Android、浏览器扩展及生产 Explorer 集成仍待完成。NAS Web 已完成局域网浏览器验收；图片与文档已只读登记，首次扫描由用户手动启动。Windows 包保持本机浏览器边界。

当前事实以 [项目状态](.codex/project-state.json)、[NAS 集成验收](.codex/handoffs/V01-021/summary.md)、[Alpha 就绪记录](docs/releases/V0.1_ALPHA_READINESS.md) 和 [代码与架构对齐审查](docs/audits/2026-09-05-alignment.md) 为准。v2.1 DOCX、`DELIVERY_REPORT.md` 和 `SHA256SUMS.txt` 是原始交接快照，不代表当前代码状态。

## 先从这里开始

1. 阅读 [`START_HERE.md`](START_HERE.md)。
2. 主协调 Codex 线程读取 [`.codex/START_HERE.md`](.codex/START_HERE.md)。
3. 依次阅读：
   - `docs/00_交接总览.md`
   - `docs/01_核心原则与范围边界.md`
   - `docs/02_已确认需求基线.md`
   - `docs/05_总体架构与技术框架.md`
   - `docs/16_版本路线与验收门禁.md`
   - `docs/17_Codex并行开发工作流.md`
   - `docs/22_编码与架构开发原则.md`
4. 运行 `python -I -B scripts/verify_repository.py`，再按修改范围执行原生检查。
5. 读取现有任务登记与交接，从未完成项创建隔离 worktree，保留仍开放的发布门禁。

快速入口检查交接、架构、Alpha 判定、迁移清单、SDK 生成一致性和源码/依赖策略。完整 `fast-merge` 还包含 .NET、TypeScript/Web 和 Kotlin 构建与测试；真实命令见 [构建说明](eng/README.md) 和 [CI 合同](tests/architecture/ci-tiers.json)。平台缺失或测试跳过不等于发布通过。

## 最重要的六条约束

1. **真实物理目录是权威来源。** 收藏夹、保存视图、人物、时间轴等只能是明确标识的逻辑视图。
2. **原文件默认非破坏。** 首次扫描、预览、AI、OCR、查重、元数据候选都不得擅自改动资产。
3. **文件名不承担身份判断。** 稳定 ID、文件系统证据、快速指纹与完整强哈希共同维护身份。
4. **AI 只做增强。** 没有 AI 时，索引、搜索、预览、整理、同步、查重和元数据仍必须完整可用。
5. **性能和文件安全是发布门禁。** 首版以 50 万资产、100GB 单文件、Explorer 不被拖崩为硬性目标。
6. **架构边界由 CI 执行。** 业务逻辑只实现一次；模块依赖、语言预算和契约兼容不得靠开发者自觉维持。
