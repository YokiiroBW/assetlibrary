# V01-010 交接摘要

## 完成状态

`ready_for_review`

## 完成内容

- 为唯一 `AssetLibrary.CoreServer.Host` 增加显式启用的 PostgreSQL readiness 模式；连接秘密只从 `ASSETLIBRARY_DATABASE_READINESS_CONNECTION` 读取，不接受命令行连接串。
- 启动前使用固定、低权限、只读 auditor 核对 PostgreSQL 16、bootstrap 与完整 migration ledger；失败时在监听端口前以稳定低敏错误退出。
- 运行中 `/readyz` 实时检查数据库：正常返回 `host_database` 与 schema 版本，漂移、超时、权限拒绝或断连返回 503；`/healthz` 仍只表示进程存活。
- 将生产 migration manifest 嵌入 Host，并把生产 migration 目录纳入发行 provenance 输入，保证产物摘要覆盖运行时信任根。
- 保持无数据库变量时 V01-008 的 `host_only` payload 完全兼容，且任何模式都不声明业务 API 或生产文件写入就绪。
- 将 Host 构建接入现有 `fast-merge-database` 作业；未新增 CI tier、框架、数据库或依赖。

## 关键决策

- V01-010 只提供数据库 runtime 组件，不选择本地账号、OIDC 或混合认证方案；认证仍由后续 owner 任务决定。
- runtime 不应用迁移、不预配角色、不执行 DDL；迁移与登录预配仍必须通过现有显式运维入口完成。
- 登录角色必须是 `NOINHERIT`，不得拥有 superuser、createdb、createrole、replication 或 bypassrls，并必须直接/间接拥有固定 auditor membership；随后才允许 `SET LOCAL ROLE assetlibrary_database_auditor`。
- 连接和命令绝对上限为 5 秒，连接池最大 4，ledger 最多读取 1001 行；检查复杂度与资产数无关。
- 对外失败只返回 `database_not_ready`；启动 stderr 使用稳定分类码，但不输出 host、database、username、password、证书路径或异常详情。

## 修改文件

- Host 组合与 readiness：`services/core-server/Host/Hosting/**`、`services/core-server/Host/AssetLibrary.CoreServer.Host.csproj`
- 发行 provenance 与 CI：`eng/server-release-policy.json`、`scripts/{build_server_release.py,validate_server_release.py}`、`.github/workflows/handoff-quality.yml`
- 测试：`tests/dotnet/AssetLibrary.Packaging.Tests/{DatabaseReadinessTests.cs,HostOptionsTests.cs}`、`tests/database/test_migration_integration.py`
- 任务、发布说明与交接：`.codex/tasks/V01-010.md`、`docs/releases/V0.1_ALPHA_READINESS.md`、`.codex/handoffs/V01-010/**`

## 模块边界、依赖方向与复用

- Host 仍是 composition adapter；没有把数据库、文件系统或网络依赖引入 Domain/Application。
- 复用 V01-003 的 manifest、bootstrap checksum、ledger 与 `assetlibrary_database_auditor`，没有第二套 schema-version 表或迁移算法。
- 复用 V01-008 的 Host options、JSON、退出码、health/readiness 和发行 provenance 机制；没有平行 host。
- 数据库访问只限 `migration.bootstrap_state` 与 `migration.ledger`，没有访问任何业务 schema，也没有跨模块写表。

## 新语言、框架或重大依赖

无。使用仓库已有 C#/.NET、Python 测试工具与 Npgsql 10.0.3。

## 共享契约或数据库变化

- 新增仅在显式数据库模式出现的内部 readiness 合同 `v01-010/1`；既有 `v01-008/1` `host_only` JSON 保持精确兼容。
- 未修改 `contracts/**`、数据库 SQL、manifest、checksum、角色合同或生成 SDK。
- 未修改 `eng/v0.1-alpha-readiness.json` 或任何 release gate 状态。

## 测试结果

- 351 项唯一自动测试通过，0 失败、0 跳过：197 .NET、54 database、54 repository、14 architecture、32 release。
- PostgreSQL server/client 均为 16.15；完整套件在临时本机 cluster 上执行并强制 `ASSETLIBRARY_TEST_POSTGRES_REQUIRED=1`。
- Host 正向、ledger 漂移/恢复、5 秒锁等待超时/恢复、断库、非 auditor 与 superuser 拒绝、secret/数据库名不泄漏均通过。
- `v0.1-start` 允许；Alpha 默认审计有效但 decision 仍为 blocked；`--require-ready` 与 `v0.1-release` 保持预期阻断。
- 实现提交：`27e53182452b3b4ad5ed29a4ce236ed90b3e969c`。

## 架构测试与质量门禁

- locked restore、format verify、Release build（0 warning / 0 error）与完整 solution 通过。
- repository verifier、.NET source policy、migration manifest、server release definition、Alpha audit、architecture/repository/release suites 全部通过。
- 发行定义仍报告 Windows Service、Docker 与 Linux systemd 为 `blocked_missing_environment`；15 项 Alpha 能力、6 个 release target 和 V01-008 partial 均未被关闭。

## 文件安全、权限与性能影响

- runtime 全程只读；测试中的迁移、篡改与锁操作仅发生在临时测试数据库，均由既有测试工具创建和回收。
- 没有触碰生产数据库、真实资产、UAC、注册表、防火墙、系统服务、PATH 或持久凭据。
- 测试结束后无 PostgreSQL/CoreServer 子进程、监听端口、临时数据库或 V01-010 runtime 目录残留。
- 每次 readiness 为固定九条 migration 的 `O(migrations)` 检查，最多 1001 行、小连接池和 5 秒取消边界，不随 50 万资产规模增长。

## 技术债、已知问题与风险

- 数据库模式目前是显式 opt-in；发布 Host 仍允许 `host_only`，因此 `production-database-composition` 必须继续 blocked。
- 未实现生产登录、会话/身份源、TLS、证书/secret 生命周期、业务 API、生产写入、迁移编排或登录角色预配。
- 当前只执行了 Windows 本机 PostgreSQL 16.15 证据；CI 已接入 Linux PostgreSQL 16.15 service，但本线程没有伪造远端 CI 运行结果。
- 新迁移会改变嵌入 manifest 与 provenance，必须重新构建 Host 并通过完整 ledger 回归；这是预期的失败关闭行为。

## 建议合并顺序

在 V01-009 之后合并，本任务顺序为 9；先合并实现提交，再合并本交接提交。不要随本任务修改 Alpha capability 或 release target 状态。

## 下一步

先冻结生产认证方案并实现认证 runtime；随后由 `V01-READ-HOST-INTEGRATION` 组合认证后的只读业务用例，使数据库成为必需启动依赖。完成独立政策与端到端证据审查前，认证、数据库组合、业务 API、TLS、生产写和全部外部平台 release 均保持阻断。

## Codex 线程链接（可选）

仅作为导航，不是唯一交接依据。
