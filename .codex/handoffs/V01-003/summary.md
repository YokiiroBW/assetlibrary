# V01-003 交接摘要

## 完成状态

`ready_for_review`

- 分支：`codex/v01-003-database-ownership`
- 基线：`main` / `a3e95527b170a2e18e7138b1d2647e65768d40ee`
- 实现 commit：`ab188be5cf467d1b95c95bbfff8d22cbc9d05dfe`
- Worktree：`C:\YOKI\Codex\worktrees\V01-003`
- 交接 commit：本交接三件套提交后的分支 tip；其精确值由最终 `git log -1` 和主协调线程合并记录给出，避免 commit 内容自引用。

## 完成内容

- 新增生产唯一入口 `database/migrations/production/manifest.json`，固定 PostgreSQL `16.15`、11 个模块的 schema/owner/runtime 映射、迁移顺序、owner 与 LF 字节 SHA-256。
- 新增 26 个固定 `NOLOGIN NOINHERIT` 角色的显式预配：migration owner/executor、backup、auditor，以及 11 组 module owner/runtime；拒绝危险属性与任何清单外成员关系。
- 两个生产迁移只建立 `migration` ledger、11 个空模块 schema、ownership 清单和只读权限审计投影，不创建资源库、资产、任务、outbox 或其他业务表。
- 新增 forward-only 工具：连接与数据库名双重确认、同 major 客户端检查、ledger 前缀/哈希漂移阻断、事务 advisory lock、按 owner 执行、有限超时和确定性错误。
- 每次有 pending migration 时，先以 backup role 创建 custom-format `pg_dump`，用 `pg_restore --list` 和 SHA-256 校验，再将 archive 与 metadata 通过同文件系统目录重命名一次性发布；备份失败时不发布半成品且不执行 DDL。
- 恢复只接受与当前 manifest/role 哈希精确匹配的 archive，且只进入显式确认的空数据库；使用单事务恢复，随后逐字段读回 ledger，再以前向迁移重放到当前状态。
- 新增 PostgreSQL 16.15 真实集成测试和独立 Ubuntu `fast-merge-database` job；数据库 job 属于既有 `fast-merge` 概念层级，没有新增第四个 CI tier。

## 关键决策

- M0-005 的 SQL 与 runner 继续作为只读 Spike 证据；生产 manifest 不引用也不复制其中的候选业务表。
- 所有登录账号与凭证仍属于部署配置。仓库只定义不可登录角色，工具不接受命令行密码，连接密钥只通过 libpq 机制传递。
- module owner/runtime 按冻结模块一一对应；runtime 只有本 schema 的 `USAGE` 与 owner 后续创建对象的默认 DML 权限，不能创建对象、读取 migration ledger、跨 schema 访问或切换到 owner。
- 固定角色的角色属性均保持 `NOINHERIT`。唯一成员选项例外是无登录 migration owner 对 11 个无登录 module owner 的 `INHERIT TRUE, SET TRUE, ADMIN FALSE`，仅为建立对应 default ACL；deployment/runtime 边界仍不继承。
- 生产迁移不提供 destructive rollback。恢复合同是“保留原库 + 验证备份 + 显式空目标恢复 + forward replay”。

## 修改文件

- 迁移合同与工具：`database/migrations/README.md`、`production/manifest.json`、`roles.sql`、`bootstrap.sql`、`0001_*.sql`、`0002_*.sql`、`migration_tool.py`。
- 验证：`tests/database/test_migration_manifest.py`、`test_migration_integration.py`、`tests/repository/test_database_migration_foundation.py`。
- 门禁：`.github/workflows/handoff-quality.yml`、`scripts/verify_repository.py`、`tests/architecture/architecture-rules.json`、`ci-tiers.json`、`.gitattributes`。
- 任务证据：`.codex/tasks/V01-003.md` 与本交接三件套。完整列表见 `result.json.changed_files`。

## 模块边界、依赖方向与复用

- 变更只属于 `database-migration-foundation`，未触及 `services/**`、`apps/**`、`gateways/**`、`providers/**`、`contracts/**` 或任何业务模块实现。
- 复用 M0-005 已证明的 PostgreSQL 16.15、事务 DDL、advisory lock、checksum 与任务本地临时集群；复用 M0-009 的 handoff、架构、CI tier 和 `v0.1-start` 门禁。
- 复用 PostgreSQL 原生 role/schema/default privileges、`pg_dump`/`pg_restore` 与 Python 3.12 标准库；没有复制权限业务规则、文件操作规则或跨模块写逻辑。
- 无跨模块内部引用；跨模块业务读取仍要求后续 owning module 提供显式只读投影。

## 新语言、框架或重大依赖

- 无。SQL 与 Python 均已在项目语言预算内，未增加 ORM、Npgsql、第二数据库、服务或第三方 Python 包。
- PostgreSQL 16.15 是 ADR-0003/M0-005 已批准的数据库版本线；本任务只把它固化为生产迁移和测试合同。

## 共享契约或数据库变化

- 公共 AssetLink/API/Provider/文件合同无变化，`contracts/**` 未修改。
- 新增内部生产数据库合同：26 个固定角色、`migration` schema、11 个模块 schema、`migration.bootstrap_state`、`migration.ledger`、`migration.module_ownership` 与 `migration.module_privilege_projection`。
- 新增内部 CI 合同：Windows/Ubuntu 既有 fast-merge 执行连接无关的静态检查，Ubuntu 独立 PostgreSQL 16.15 service job 执行完整数据库集成测试。

## 测试结果

- 最终唯一自动测试计数：69 passed，0 failed，0 skipped（database static 21、PostgreSQL integration 5、repository 29、architecture 14）。
- PostgreSQL 16.15 全套 26/26 在 `ASSETLIBRARY_TEST_POSTGRES_REQUIRED=1` 下通过，证明没有以 skip 伪造真实证据。
- 真实验证覆盖：预配/重跑、11/11 权限投影 clean、runtime 允许与拒绝路径、并发串行、失败迁移事务回滚、已应用文件/名称/ledger 缺口漂移、备份失败、截断 archive、非空目标拒绝、空库恢复及 forward replay。
- `scripts/verify_repository.py`、全部 repository tests、handoff/architecture baseline、`v0.1-start`、manifest validator 与 `git diff --check` 均纳入最终门禁；命令和逐项证据见 `tests.md`。

## 架构测试与质量门禁

- manifest 对目录外路径、额外/缺失 SQL、非连续顺序、错误 owner、CRLF、哈希漂移、角色 DDL、数据库 DDL、事务命令、psql meta-command、身份切换与密码材料失败关闭。
- repository 夹具固定 M0-005 四个输入文件的原始 SHA-256，验证本任务没有通过改写 Spike 证据获得通过。
- CI 测试要求精确 `postgres:16.15-bookworm`、required integration、隔离 runtime、完整 test glob 且禁止 `continue-on-error`。
- 生产 SQL/manifest 由 `.gitattributes` 固定 LF，保证 Windows 与 Ubuntu 校验同一字节哈希。

## 文件安全、权限与性能影响

- 未读取或修改真实资产/NAS，未接触注册表、Explorer、系统服务、Provider 或生产数据库；没有全局安装、PATH 修改或 PostgreSQL 服务注册。
- Windows 证据只使用任务 `.runtime/sandbox-storage/V01-003` 下的 EDB no-install ZIP，监听 loopback 随机端口。测试前后任务内 PostgreSQL 进程均为 0。
- 最终已删除整个 V01-003 临时运行时，包括两个早期异常运行遗留集群、当次数据、下载 ZIP 与解压工具；源码和系统 PostgreSQL 未受影响。
- 迁移 metadata、ledger 和 catalog 审计仅随固定 migration/schema 数量线性增长，与 50 万资产规模无关；本任务没有新增资产查询或内存加载路径。

## 技术债、已知问题与风险

- 新增 Ubuntu PostgreSQL job 尚未在远程 GitHub runner 实际执行；本地 Windows 证据覆盖相同 SQL、CLI、备份/恢复和错误路径，首份远程证据由合并检查产生。
- backup role 保持 `NOBYPASSRLS`。未来业务表若启用 RLS，所属任务必须明确备份策略并新增全量恢复测试，不能假设当前 default SELECT 自动等于可恢复的全量数据。
- archive SHA-256 检测损坏与错配，不提供来源真实性；运维必须把 archive 与 metadata 作为同一受信、受访问控制的备份单元保存。
- PostgreSQL patch 升级必须显式更新 manifest、CI image 和精确集成证据；工具只允许同 major 客户端/服务端，不会静默跨 major 执行。

## 建议合并顺序

V0.1 顺序 `3`。V01-003 已变基到包含激活提交的最新 `main`，可在 V01-004/V01-005 创建首批业务表之前由主协调线程审查并 fast-forward 合并。

## 下一步

- 主协调线程检查实现 commit、交接、最终 diff 与远程 fast-merge，随后合并并更新 `.codex/project-state.json` / `.codex/task-registry.json`；本任务不自行改写协调状态。
- V01-004/V01-005 必须消费本任务冻结的 schema/owner 和 manifest，不得直接复用 M0-005 Spike 业务表。
- V0.1 release、生产文件写入、Provider、Explorer 和平台发布门禁仍保持阻断；本任务没有关闭这些门禁。

## Codex 线程链接（可选）

未填写；仓库 commit 与交接文件是权威证据。
