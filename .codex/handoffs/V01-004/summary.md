# V01-004 交接摘要

## 完成状态

`ready_for_review`

- 分支：`codex/v01-004-library-asset-scan-read-core`
- 基线：`main` / `056e318c547e669a1ef92ab59b2c53facf28dc29`
- 实现 commit：`71cb1494eb25903a9628cf1b076aa7b10b75012f`
- Worktree：`C:\YOKI\Codex\worktrees\V01-004`
- 交接 commit：本交接三件套提交后的分支 tip；精确值由最终 `git log -1` 和协调器合并记录给出，避免 commit 内容自引用。

## 完成内容

- 为 `LibraryStorage` 增加非空 ID、绝对规范根、存储可用性、同源根查询、根探测和相同/父子重叠预检；候选库更新时会跳过自身 ID。
- 为 `AssetIdentity` 增加稳定条目 ID、规范相对路径、文件/目录/reparse 观察合同，以及只能分批 stage、完整 complete 或 abort 的初扫会话端口。
- 为 `ScanReconciliation` 增加有界超时/取消、离线前置阻断、流式发现、固定批次、明确终态日志与完整性失败处理。发现或提交失败不会生成部分成功结果。
- 实现真实文件系统只读适配器：只读取目录项属性和普通文件长度；默认忽略系统/回收站/缓存/Sidecar/临时项；reparse 条目可见但不递归，reparse 文件不读取目标长度。
- 新增三个连续生产迁移：库根和同源重叠注册函数、稳定资产与初扫 staging/commit/abort、扫描生命周期。空库也写入显式快照，重复初扫和混库 staging 均失败关闭。
- 新增 `AssetLibrary.ReadCore.Tests` 并登记到解决方案；同时扩展 PostgreSQL 16.15 真实集成测试和迁移静态规则。

## 关键决策

- 首次扫描保持严格只读：核心没有写文件、移动、删除、改名、写流、哈希或媒体解码端口；数据库只接收观察事实。
- C# `LibraryRootOverlapPolicy` 提供注册前可解释反馈，SQL `register_library_root` 在同一存储源 advisory lock 下独立强制相同规则。这是针对并发信任边界的纵深防御，不抽成跨语言共享实现。
- 初扫采用 staging + 单次 commit；任何发现不完整、超时或取消都 abort。空扫描仍写 `library_index_snapshot`，避免“空库”和“从未扫描”混淆。
- 未选择 Npgsql、ORM 或生产组合根；本任务只冻结核心合同、真实文件系统只读适配器和数据库行为，具体数据库适配器留给后续组合任务。
- 资产提交成功后不再伪造回滚；若随后扫描日志终态落盘失败，会抛出明确错误并保留完整资产快照，等待后续持久任务/outbox 对账机制处理。

## 修改文件

- 核心：`services/core-server/Modules/{LibraryStorage,AssetIdentity,ScanReconciliation}/**`。
- 数据库：`database/migrations/production/0003_*.sql` 至 `0005_*.sql`、manifest 和迁移说明。
- 验证：`tests/dotnet/AssetLibrary.ReadCore.Tests/**`、`tests/database/test_read_core_migrations.py` 及现有 manifest/integration 测试增量。
- 接线与证据：`AssetLibrary.slnx`、`.codex/tasks/V01-004.md` 和本交接三件套。完整路径见 `result.json.changed_files`。

## 模块边界、依赖方向与复用

- `LibraryStorage` 唯一拥有库根、存储源和可用性；`AssetIdentity` 唯一拥有稳定身份与观察快照；`ScanReconciliation` 唯一拥有发现流程和扫描终态。
- 依赖保持 `Infrastructure -> Application -> Domain/Contracts`；`ScanReconciliation.Application` 只消费另外两个模块公开的 `Contracts` 端口，没有引用其 Infrastructure 或内部实体。
- 三个数据库迁移只写各自 schema，无跨 schema 外键或写入；跨模块关联使用 UUID 合同并由应用层协调。
- 复用 V01-003 的连续 manifest、模块 owner/default privilege、forward-only 迁移和任务本地 PostgreSQL 机制；复用现有 .NET 10、MSTest、日志抽象和仓库门禁。

## 新语言、框架或重大依赖

- 无。没有新增语言、框架、NuGet/NPM/Gradle 包、ORM、数据库或服务。
- 最终 Kotlin SDK 门禁使用任务沙盒内的 Temurin `21.0.12+8` 和固定 Gradle `9.3.1`；没有系统安装或永久 PATH 修改。

## 共享契约或数据库变化

- `contracts/**` 未修改；AssetLink 公共 wire contract 未变化。
- 新增模块内 C# 公开合同：库/存储源/根、稳定条目/相对路径/观察会话、初扫请求/结果与发现/日志端口。
- 生产 manifest 从 2 个迁移连续扩展到 5 个，新增 `library_storage`、`asset_identity`、`scan_reconciliation` 各自拥有的表、索引和函数；没有修改既有迁移。

## 测试结果

- 唯一自动测试计数：172 passed，0 failed，0 skipped（.NET 58、database 31、repository 29、SDK Python 14、AssetLink contract/spike 21、Node 5、architecture 14）。
- 其中 ReadCore MSTest 为 43/43，真实 PostgreSQL 16.15 integration 为 6/6；Kotlin 另有一个生成 SDK 验证任务通过，不重复计入上述用例数。
- Kotlin `build sdkTest` 在固定依赖校验严格模式下 `BUILD SUCCESSFUL`；TypeScript lint/typecheck/build/test 与 audit 均通过。
- 完整命令、故障路径和性能证据见 `tests.md`。

## 架构测试与质量门禁

- locked restore、format、Release build、全解决方案测试、依赖/源码策略、NuGet 漏洞检查全部通过，构建 0 warning / 0 error。
- migration manifest 为 5 个连续迁移且哈希一致；repository verifier、handoff、architecture baseline、`v0.1-start` release gate 与 `git diff --check` 均通过。
- 数据库真实验证覆盖同根/父子拒绝、路径前缀近似不误判、大小写策略、空库快照、重复初扫、abort、混库 staging 拒绝与保留、跨 schema 拒绝。

## 文件安全、权限与性能影响

- 未读取或修改真实资产/NAS，未触碰注册表、Explorer、系统服务或生产数据库。真实文件系统测试仅使用 `.runtime/sandbox-storage/V01-004`，前后文件路径、时间、长度和 SHA-256 强摘要一致。
- reparse 目录绝不递归；reparse 文件不读取目标长度。扫描失败、离线、超时和取消均不提交部分索引，也不据此推导缺失。
- PostgreSQL 16.15 只在 loopback 随机端口的任务临时集群运行；测试结束后进程为 0。可选 pgAdmin 子树已从解压运行时移除，原始校验通过的 ZIP 保留，源码和系统安装未改动。
- 发现使用 `IAsyncEnumerable`，批次上限 1024；10,000 项场景以 40 个不超过 256 项的批次完成。索引覆盖库+规范路径、稳定 ID、状态/时间；50 万资产完整 profile 留给计划内门禁。

## 技术债、已知问题与风险

- `AssetIdentity` 完整提交与 `ScanReconciliation` 终态日志属于两个模块端口，目前不是同一数据库事务；日志若在资产提交后失败会留下“完整快照 + running 日志”。代码不会错误 abort 已提交数据，后续 V01-005/V01-006 需用持久任务/outbox 和对账收敛。
- 尚无生产 PostgreSQL 端口适配器、依赖注入组合/API；这是任务范围内的有意延后，不能把迁移函数当作已接入产品运行时。
- Windows/网络文件系统的同步元数据调用无法在内核调用卡死时被托管取消即时打断；当前有应用级超时/取消与完整性失败合同，生产仍需 Worker 隔离和健康监管。
- 50 万资产真实 NAS、长期离线恢复、权限中途变化与生产级索引 profile 尚未执行；本任务仅证明有界内存和小规模真实文件系统安全。

## 建议合并顺序

V0.1 顺序 `4`。分支已基于激活 V01-004 的最新 `main`，可由主协调线程审查后 fast-forward 合并；V01-005 必须在其后消费已冻结的 module schema 和公开端口。

## 下一步

- 主协调线程检查最终 diff、交接和工作树后 fast-forward 合并，并更新 `.codex/project-state.json` / `.codex/task-registry.json`。
- 下一项计划任务为 V01-005（持久 TaskHealth/outbox/租约/取消）；需要显式处理上述跨模块终态对账，不得绕过 owner/schema 边界。
- 生产文件写入、Explorer、Provider、Web/API 和发布门禁继续保持阻断；V01-004 没有开放这些能力。

## Codex 线程链接（可选）

未填写；仓库 commit 与交接文件是权威证据。
