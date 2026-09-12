# AGENTS.md — AssetLibrary 项目级工作约定

## 1. 项目目标

构建一套以真实物理目录为权威、面向 NAS 的跨端资产预览与管理系统。官方端包括 Web、Windows 独立客户端、Windows Explorer 集成、Android/平板与 Chromium 浏览器扩展；服务端提供 Docker、Windows x64 原生和 Linux x86-64 原生发行。

## 2. 不可违反的产品原则

- 真实物理文件与目录是事实来源；禁止用数据库逻辑文件夹替代物理归属。
- 文件名只用于展示与搜索，不能独立判断身份、版本、项目或关系。
- 首次扫描必须只读；AI、OCR、预览、查重、元数据匹配默认不得修改原文件。
- 任何真实写操作都必须经过统一权限、预检、锁、冲突、空间、校验、审计与垃圾桶规则。
- AI 和在线 Provider 只做增强；没有它们时基础功能必须完整可用。
- Explorer 进程内不得加载第三方或重型 Provider，不得进行网络传输、哈希或媒体解码。
- 软件备份不包含资产原文件。

## 3. 架构与依赖方向

- 采用领域模块化单体核心 + 端口适配器 + 隔离 Worker，不默认拆成大量微服务。
- 强制依赖方向：`Adapters/UI/Gateways -> Application -> Domain`；Infrastructure 只实现核心定义的端口。
- Domain 不得依赖 UI、数据库、文件系统、网络、操作系统或具体 Provider。
- 模块只能通过公开 Command/Query、端口、版本化契约、领域事件或批准的只读投影交互。
- 禁止跨模块写表、直接引用其他模块 Infrastructure/内部实体、循环依赖和共享可变全局状态。
- Web、Windows、Android、Explorer、WebDAV、Developer API 和 MCP 只做适配；权限、路径、哈希、传输、同步、垃圾桶、保护、审计等业务逻辑只能在核心实现一次。
- 完整规则见 `docs/22_编码与架构开发原则.md` 与 `.codex/policies/`。

## 4. 语言与框架预算

每个平台只允许一个主语言与主框架。默认候选为：服务端 C#/.NET、Windows C#/WinUI、Shell C++/WinRT、Web/扩展 TypeScript、Android Kotlin/Compose、工具 Python、数据库 PostgreSQL SQL。最终版本由 M0 Spike 与 ADR 冻结。

未经批准 ADR，禁止新增主语言、第二套同职责框架、第二数据库、独立搜索/消息集群或额外服务端。重大依赖必须说明许可证、安全更新、跨平台、包体、维护和退出策略。

## 5. 代码开发原则

- KISS、YAGNI、单一职责、组合优先、显式状态与明确错误类型。
- 禁止 God Service、万能 Repository、无边界 `CommonUtils`/`Helpers`、魔法字符串和空 `catch`。
- 所有 I/O 必须支持取消、超时、结构化日志以及明确的幂等/重试边界。
- ID、状态、路径和哈希使用明确类型或值对象；生成代码不得手工修改。
- 注释解释“为什么”；TODO 必须关联任务 ID、责任人和清理条件。
- 不为尚未发生的需求提前建设万能抽象。

## 6. 开发前必读

按顺序阅读：

1. `.codex/START_HERE.md`
2. `docs/01_核心原则与范围边界.md`
3. `docs/02_已确认需求基线.md`
4. `docs/05_总体架构与技术框架.md`
5. `docs/16_版本路线与验收门禁.md`
6. `docs/17_Codex并行开发工作流.md`
7. `docs/22_编码与架构开发原则.md`
8. `.codex/policies/CODE_QUALITY.md`
9. 当前任务包和相关 ADR / contracts。

不要用聊天记忆替代仓库文件。发现需求或架构冲突时，停止扩展实现并提交决策问题。

## 7. 并行开发规则

- 每个长期写任务使用独立 Git worktree 和分支。
- 禁止多个窗口直接同时修改同一工作目录。
- 任务包必须声明模块所有权、允许/禁止修改目录、依赖、复用点、共享契约和完成标准。
- 数据库迁移、AssetLink 契约、权限模型、统一错误码、根级依赖锁和版本号必须有单一所有者。
- 工作线程不能自行修改共享契约；需要变更时提交 Contract Change Proposal，由主协调线程批准。
- 主协调线程主要负责任务图、契约裁决、架构评审、合并与集成，不包办所有模块编码。

## 8. 每个任务的完成定义

完成任务前必须：

- 提交可构建代码或明确的技术验证结果；
- 增加或更新自动测试，并运行架构、契约和修改范围相关测试；
- 说明复用了哪些既有用例/契约，是否存在重复逻辑；
- 说明文件安全、权限、性能、依赖方向和兼容性影响；
- 写入 `.codex/handoffs/<task-id>/summary.md`、`result.json` 与 `tests.md`；
- `result.json` 必须填写与 diff 一致的 `architecture_review`；
- 提供分支、commit、测试、风险、技术债和建议合并顺序；
- 更新任务状态，但不要擅自宣布整个里程碑完成。

## 9. 测试与资产安全

- 任何破坏性测试只能使用 `.runtime/sandbox-storage/` 或系统临时目录。
- 测试必须覆盖断网、进程崩溃、服务重启、空间不足、同名冲突、哈希变化和权限拒绝。
- 文件操作成功不能只依赖返回码；必须验证目标可读和完整强哈希。
- 出错时优先保留源文件，禁止静默覆盖。
- 不把真实用户路径、凭证、Token、GPS、人脸或私密角色正文写进测试夹具和日志。

## 10. 性能与 CI 纪律

- 所有大列表分页或虚拟化；所有大文件流式处理，禁止整体读入内存。
- 后台 Worker 限并发、可暂停、可取消并服从前台优先级。
- 任何网络或 Provider 超时都不能冻结 Explorer。
- 新功能必须说明 50 万资产下的复杂度和索引策略。
- CI 必须执行格式化、静态分析、类型、依赖/循环、架构、契约、迁移、单元、集成、安全、故障与性能门禁。
- M0-009 完成前不得进入 V0.1 大规模实现。

## 11. 文档与图示

- 长篇说明必须使用 Markdown / DOCX 文本。
- 图片只用于视觉方向、架构图、框架图和必要 UI 原型。
- 需求改变必须同步更新基线、ADR、矩阵和废弃清单。
- 不得把实现猜测写成已经确认的事实。

## 12. 当前验证入口

M0-009 已冻结架构，当前处于 V0.1 受控实现。先运行 `python -I -B scripts/verify_repository.py`；`python -I -B scripts/validate_v0_1_alpha.py` 应通过有效性审计并保持发布 blocked，`python -B tests/architecture/check_release_gates.py --target v0.1-start` 允许受控开发。

原生 .NET 使用 `global.json` 的精确 SDK：`dotnet restore AssetLibrary.slnx --locked-mode`、`dotnet format AssetLibrary.slnx --verify-no-changes --no-restore`、`dotnet build AssetLibrary.slnx --configuration Release --no-restore`、`dotnet test AssetLibrary.slnx --configuration Release --no-build --no-restore`。Web/SDK、依赖、数据库和平台的真实命令统一见 `tests/architecture/ci-tiers.json`、对应 workflow 及 `eng/README.md`，不得自造测试层级或将平台缺证据记为通过。

Android 原生只读首版的实际 Gradle/SDK、lint、协议、仪器与依赖审计命令见 `apps/android/README.md`；Windows 只读适配的独立 solution 命令见 `apps/windows-client/README.md`，原生C++验证见 `tests/windows-shell/README.md` 与 `tests/windows-gallery/README.md`。用户已明确Windows必须以原生Explorer为入口（ADR-0018/0021），独立适配测试不能宣称内嵌完成；当前已交付原生Explorer图库浏览preview.6，实际Core/Explorer图片、选择、自有计数和资源清理范围见 `docs/releases/WINDOWS_EXPLORER_PREVIEW.md`。G1/G2/G3沿用实测，G4按用户要求豁免未执行，不再安排20轮/8小时。NAS图片引擎、完整预览/同步、人工认证/系统卸载UI和签名仍分别记录；Windows11旧底部计数以图库工具栏摘要为准。Android API36模拟器证据不能替代HyperOS/Android11真机或完整V0.3发布证据。
