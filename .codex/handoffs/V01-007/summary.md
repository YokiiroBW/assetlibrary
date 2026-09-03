# V01-007 交接摘要

## 完成状态

`ready_for_review`

- 分支：`codex/v01-007-transfer-operation-sandbox`
- 实现 commit：`7fb7de2d65ea9b87117ba6430fc7df496f5ed941`
- Worktree：`C:\YOKI\Codex\worktrees\V01-007`
- 交接 commit：本交接三件套提交后的分支 tip；精确值由最终 `git log -1` 和协调器合并记录给出，避免 commit 内容自引用。

## 完成内容

- 新增 `TransferSync` 公开合同、纯领域状态规则与应用服务：不透明源/目标 token、期望长度和 SHA-256、幂等会话、不可倒退状态、调用方取消、绝对截止时间、fencing 执行权与稳定失败类型。
- 新增 `OperationTrash` 公开合同、计划政策与应用服务：有界 copy/move/rename/trash/restore 计划、只读预检、确认摘要绑定、保护/权限/容量/冲突结论、部分成功和逐项稳定结果。
- 新增仅位于测试程序集的 V01-007 文件沙箱适配器：真实字节流式复制、目标重开强哈希、no-replace 提交、move 后源入垃圾桶、普通 trash/restore、相对 journal 与物理事实恢复。
- 覆盖源变化、并发目标、哈希错误、空间不足、取消、超时、过期 fencing、路径逃逸、reparse point、故障点恢复与同目标并发竞争。
- 增加仓库负向门禁，机器检查生产源码没有接入磁盘写 API、生产启用开关、V01-007 固定沙箱路径或测试适配器。

## 关键决策

- 这是验证核心语义与物理安全的沙箱纵向切片，不是生产写能力；没有生产文件适配器、持久 store、组合根、API、Worker 或配置开关。
- 完成只以目标重新打开后的长度和完整 SHA-256 为准；端口/store 返回值视为不可信输入，默认 `record struct`、越界进度、错误会话/计划和异常取消结果都失败关闭。
- 预检和确认绑定同一计划、项目、源物理事实、目标意图、权限/保护、容量与冲突结论；已完成计划的幂等重放也必须使用原确认摘要。
- 目标提交始终 no-replace；move 仅在目标强校验后把源移入垃圾桶；trash/restore 从不永久删除或覆盖。
- 恢复先观察 source/stage/target/trash 的当前物理事实，再结合相对 journal 收敛；证据不唯一时报告人工冲突，不按旧日志盲目重放。
- 操作截止时间从调用开始计时，preflight/store/port 花费不能延长预算；调用方取消与内部超时保持可区分。

## 修改文件

- 核心：`services/core-server/Modules/TransferSync/{Contracts,Domain,Application}/**` 与 `services/core-server/Modules/OperationTrash/{Contracts,Domain,Application}/**`。
- 测试：`tests/dotnet/AssetLibrary.TransferOperation.Tests/**`，共 64 个不跳过测试；物理适配器只存在于该测试项目。
- 门禁：`tests/repository/test_transfer_operation_foundation.py`；`AssetLibrary.slnx` 只登记新测试项目。
- 协调：`.codex/tasks/V01-007.md` 与 `.codex/handoffs/V01-007/**`。精确清单见 `result.json.changed_files`。

## 模块边界、依赖方向与复用

- 保持 `test adapter -> OperationTrash/TransferSync Application -> Contracts/Domain`；Domain 不依赖文件系统、数据库、网络、平台 API 或其他模块 Infrastructure。
- `OperationTrash` 只引用 `TransferSync.Contracts` 的公开 token/物理事实类型；两模块没有互相访问 Infrastructure、内部实体、表或共享可变状态。
- 复用 M0-006 的 physical-facts-first、strong-hash、no-replace、冲突保留和幂等恢复证据；没有把 Python Spike 复制为生产实现。
- 复用 V01-004/V01-005 的 .NET 值对象、应用端口、显式状态、超时/取消、隐私安全日志和 solution 质量门禁。
- 复用冻结的 canonical operation-plan schema 作为只读输入；未修改 `contracts/**`、数据库迁移、统一错误码或生成 SDK。

## 新语言、框架或重大依赖

- 无。继续使用已批准的 C#/.NET 测试栈；未新增 NuGet 包、语言、框架、数据库或系统组件。

## 共享契约或数据库变化

- canonical `contracts/operations/operation-plan.schema.json` 未修改，其 SHA-256 仍为 `f2e29c8c750032409b69056cb0e8fe1733135d3b4eea5e9ac238956872575f7e`。
- 新合同均为 `TransferSync` / `OperationTrash` 模块内 C# 公开合同；没有外部 wire schema、数据库对象、迁移、AssetLink 或 SDK 变化。

## 测试结果

- 唯一自动测试计数：317 passed，0 failed，0 skipped（.NET 167、database 53、repository 42、architecture 14、SDK Python 14、AssetLink contract/spike 21、Chromium 6）。
- V01-007 定向 MSTest 64/64；完整 .NET solution 167/167；Release build 0 warning / 0 error。
- TypeScript 生成 SDK 另为 5/5，Kotlin/JVM 生成 SDK build 通过；它们验证同一冻结合同，未重复计入 317。

## 架构测试与质量门禁

- .NET locked restore、format、Release build、完整 solution、NuGet 漏洞/许可证/锁文件与源码复杂度/重复/敏感日志门禁通过；9 个项目、18 个锁定包和 136 个 C# 文件受检。
- repository 42/42、architecture 14/14、数据库 required 53/53（含 PostgreSQL 16.15 integration 14/14）、SDK/AssetLink/Web 全链通过。
- `v0.1-start` 允许；`production-file-writes` 和 `v0.1-release` 继续按 M0-006-G1/G2/G3 返回阻断，生产物理写默认关闭。
- 最终 `git diff --check`、任务沙箱残留与进程检查通过。

## 文件安全、权限与性能影响

- 没有访问真实资产、NAS、用户目录、注册表、Explorer、服务或系统配置；破坏性 fixture 只在 `C:\YOKI\Codex\worktrees\V01-007\.runtime\sandbox-storage\V01-007` 的带 marker 真后代内。
- 沙箱拒绝根本身、相对/UNC/设备/其他卷路径、越界 canonical 路径和逐段 reparse point；测试后 `fixture-*` 为 0、Python cache 为 0、相关进程为 0。
- 传输使用 4 KiB–1 MiB 有界缓冲，测试包含 2 MiB+17 bytes 多块文件；计划最多 128 项，端口结果和累计进度均有上界。
- 日志只保留 ID、状态、计数、耗时和稳定错误，不记录绝对路径、文件名、内容、完整哈希、用户信息或凭据。

## 技术债、已知问题与风险

- 尚无生产 durable 文件适配器、持久 operation/session store、数据库 journal、DI composition、API、权限入口、审计或后台 Worker；不得把本切片描述为可部署写功能。
- 测试 store/journal 证明本进程与新实例恢复语义，但不等同于生产数据库事务、跨进程/多节点锁或分布式 fencing。
- 尚未执行跨卷、真实 NAS/SMB、非协作外部修改、断电、ACL 组合、空间临界和 1/20/100 GiB 发布容量演练。
- M0-006-G1/G2/G3 仍开放；在独立任务补齐生产 durable adapter、权限/审计入口及发布级灾难恢复证据前，生产文件写必须保持关闭。

## 建议合并顺序

V0.1 建议顺序 `6`；依赖 V01-004、V01-005、V01-006 均已在 `main`。建议在实现 commit 后合并本交接 commit，再由主协调线程更新任务图和项目状态。

## 下一步

- 协调器审查并快进合并 V01-007，保持所有生产写门禁原判定不变。
- 按冻结路线规划 V01-008 打包与发布证据；为生产 durable adapter、operation/session 持久化、统一权限/审计入口及真实 NAS/断电/容量测试保留独立受控任务。只有 M0-006-G1/G2/G3 的证据和批准齐备后才能讨论生产写入口。

## Codex 线程链接（可选）

仅作为导航，不是唯一交接依据。
