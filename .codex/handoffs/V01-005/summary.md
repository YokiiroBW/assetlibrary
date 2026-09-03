# V01-005 交接摘要

## 完成状态

`ready_for_review`

- 分支：`codex/v01-005-task-health-outbox-core`
- 基线：`main` / `0edf2152747f0ec3beab4b2379a8bd034af13eda`
- 任务包 commit：`9452235369c9c414dc7d166b70468e01045b9864`
- 实现 commit：`74bf6f213af8bafdb4eaabc36b02ac2b718d2caf`
- Worktree：`C:\YOKI\Codex\worktrees\V01-005`
- 交接 commit：本交接三件套提交后的分支 tip；精确值由最终 `git log -1` 和协调器合并记录给出，避免 commit 内容自引用。

## 完成内容

- 新增 TaskHealth 模块内合同、状态策略和应用端口，覆盖 durable task 的幂等入队、优先级 claim、心跳、完成/失败、协作取消和过期租约回收。
- 租约以 `owner + token + generation + 未过期时间` 完整围栏；应用层拒绝无效请求、越界批次、异常适配器结果和超时操作。
- 新增 durable outbox：稳定 event ID、批量 claim、发布确认、失败释放、过期回收和 dead-letter；语义明确为 at-least-once，不承诺 exactly-once。
- 新增系统、资源库、资产三级健康状态合同，约束六种状态、scope/resource ID 与 reason code，并忽略时间倒退的陈旧观测。
- 新增生产迁移 6，在 `task_health` schema 内创建 3 张表、索引和 12 个有界 `SECURITY DEFINER` 函数；运行角色仅可读表并调用函数，不能直接写表。
- 新增 32 项 TaskHealth MSTest、9 项迁移静态规则，并将真实 PostgreSQL 集成扩展到并发 claim、幂等冲突、围栏、取消竞态、重试/dead-letter、健康状态、重启持久性和最小权限。

## 关键决策

- durable task 的重放以 idempotency key 为身份；相同语义返回原任务。调用方新生成的 task ID、可用时间和写入时间不参与重放身份，重放不能悄悄重新排期。
- outbox 以稳定 event ID 作为发布端和消费者的幂等键。发布后崩溃仍可能重投，因此消费者必须按 event ID 去重。
- 成功完成与取消请求竞态时，已经取得当前有效租约的成功结果获胜；失败或取消确认在已有取消意图时收敛为 cancelled。过期且已请求取消的任务由回收器取消。
- C# 状态策略用于调用前反馈和适配器结果防御；SQL 在行锁事务内独立实施原子状态转移。这是跨信任边界的纵深防御，不是两套可替换业务实现。
- 未引入 ORM/Npgsql、生产数据库适配器、Worker 循环或网络 publisher；本任务冻结核心合同和数据库原子行为，组合接线留给后续任务。

## 修改文件

- 核心：`services/core-server/Modules/TaskHealth/{Contracts,Domain,Application}/**`。
- 数据库：`database/migrations/production/0006_task_health_core.sql`、manifest 和迁移说明。
- 验证：`tests/dotnet/AssetLibrary.TaskHealth.Tests/**`、`tests/database/test_task_health_migrations.py` 及既有 manifest/read-core/integration 测试增量。
- 接线与证据：`AssetLibrary.slnx`、`.github/workflows/handoff-quality.yml`、`.codex/tasks/V01-005.md` 和本交接三件套。完整清单见 `result.json.changed_files`。

## 模块边界、依赖方向与复用

- `TaskHealth` 唯一拥有 durable task、租约、取消意图、outbox 发布状态和健康状态；模块没有引用其他模块的 Infrastructure 或内部实体。
- 依赖保持 `Application -> Domain/Contracts`，Domain/Contracts 不依赖数据库、文件系统、网络、UI 或平台 API；生产适配器尚未加入。
- 迁移只创建和写 `task_health` schema，无跨 schema 外键、写入或直接共享实体。
- 复用 V01-003 的连续 manifest、模块 owner/default privilege、forward-only/备份恢复和 PostgreSQL 16.15 集成框架；复用 V01-004 的 .NET solution、MSTest、值对象/端口和测试模式。

## 新语言、框架或重大依赖

- 无。没有新增语言、框架、NuGet 包、ORM、消息队列、第二数据库或系统服务。
- 验证使用既有、校验过的任务本地 .NET 10.0.111 与 PostgreSQL 16.15；没有系统安装或永久环境变量修改。

## 共享契约或数据库变化

- `contracts/**` 和 AssetLink wire contract 未修改。
- 新增模块内 C# 公开合同：任务/outbox ID、租约身份、请求/结果、状态枚举、健康 scope 和存储端口。
- production manifest 从迁移 1–5 连续扩展到 1–6；只新增 module-owned `task_health` 对象，没有修改既有迁移。

## 测试结果

- 唯一自动测试计数：213 passed，0 failed，0 skipped（.NET 90、database 45、repository 29、SDK Python 14、AssetLink contract/spike 21、architecture 14）。
- 其中 TaskHealth MSTest 32/32；真实 PostgreSQL 16.15 integration 11/11，其中 5 项为 TaskHealth 真实行为、并发、容量和重启测试。
- Release build 为 0 warning / 0 error；NuGet 锁、依赖/许可证/source policy 和已知漏洞检查通过。
- 完整命令、故障路径和性能证据见 `tests.md`。

## 架构测试与质量门禁

- locked restore、format verify、Release build、全解决方案测试、依赖/源码策略、漏洞报告均通过。
- migration manifest 为 6 个连续迁移且 SHA-256 一致；repository verifier、architecture baseline、`v0.1-start` release gate 和 `git diff --check` 通过。
- 数据库 required 模式无跳过，覆盖运行角色跨 schema/直接 DML 拒绝、函数权限、并发 claim 不重叠和重启后数据保留。

## 文件安全、权限与性能影响

- 没有读取或修改真实资产/NAS，没有触碰注册表、Explorer、系统服务或生产数据库；核心没有文件 I/O 或网络投递路径。
- PostgreSQL 仅在 loopback 随机端口的任务临时集群运行，最终任务相关 postgres 进程为 0；仓库内 `__pycache__` 为 0。
- claim/reclaim 批次硬上限分别为 256/1024；10,000 个任务和 10,000 个 outbox 事件均命中对应部分索引，并验证单次只领取 256 项。
- JSON payload 上限 256 KiB，字符串、attempt、lease、retry、schema version 和应用操作超时均有硬限制。

## 技术债、已知问题与风险

- 尚无生产 PostgreSQL adapter、依赖注入组合、Worker 调度/监管或 outbox publisher；当前合同与迁移不能被描述为已接入产品运行时。
- at-least-once 允许发送成功但确认落库前崩溃导致重复投递；后续消费者必须实现稳定 event ID 去重并测试真实 Provider 失败。
- V01-004 的“资产快照已提交但扫描终态日志失败”对账尚未接入新的 TaskHealth/outbox；核心能力已具备，跨模块编排仍需后续单一 owner 任务。
- 10,000 行索引与有界批次测试不等于 50 万任务/事件正式 profile；长期积压、分区/归档、VACUUM 和生产容量仍需计划内验证。
- 过期回收当前立即重新排队；生产调度的退避、抖动、限流和 supervisor 生命周期尚未实现。

## 建议合并顺序

V0.1 顺序 `5`。分支基于激活 V01-005 的最新 `main`，可由主协调线程完成最终 diff 审查后 fast-forward 合并；生产文件写、Provider、Explorer 和发布门禁继续保持关闭。

## 下一步

- 主协调线程验证最终交接、工作树与分支差异后 fast-forward 合并，并更新 `.codex/project-state.json` / `.codex/task-registry.json`。
- 在冻结下一任务前先更新 V0.1 任务图；优先选择生产 PostgreSQL adapter/组合与 V01-004 终态对账，但不得在没有单一 owner 和任务包的情况下直接扩展共享契约。
- 真实网络 publisher、Provider、文件写操作、Explorer 集成和发布能力仍需各自门禁，不由 V01-005 自动开放。

## Codex 线程链接（可选）

未填写；仓库 commit 与交接文件是权威证据。
