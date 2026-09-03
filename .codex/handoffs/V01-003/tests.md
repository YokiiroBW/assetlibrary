# V01-003 测试记录

## 执行环境

- Windows x64 隔离 worktree：`C:\YOKI\Codex\worktrees\V01-003`
- 基线：`main` `a3e95527b170a2e18e7138b1d2647e65768d40ee`
- 实现：`ab188be5cf467d1b95c95bbfff8d22cbc9d05dfe`
- Python：任务随附运行时 `3.12.13`，所有测试使用 `-B`；警告敏感单测额外使用 `-W error`。
- PostgreSQL server、`psql`、`pg_dump`、`pg_restore`：`16.15`。
- Windows binary：EDB no-install archive `postgresql-16.15-3-windows-x64-binaries.zip`，来源 `https://get.enterprisedb.com/postgresql/postgresql-16.15-3-windows-x64-binaries.zip`，大小 `333048048` bytes，SHA-256 `5e8afffe67daf949aeeb03b74951f1ec2324e1888f73fbd036ab0e567ab004d9`。
- 自托管测试只监听 `127.0.0.1` 的随机端口，trust 认证仅存在于随测试删除的临时集群；未注册服务、未改 PATH、未连接外部数据库。

## 执行命令

```text
python -B database/migrations/production/migration_tool.py validate
python -B -W error -m unittest discover -s tests/database -p test_migration_manifest.py -v
ASSETLIBRARY_TEST_POSTGRES_BIN=<task-local-16.15-bin>
ASSETLIBRARY_TEST_POSTGRES_REQUIRED=1
ASSETLIBRARY_TEST_RUNTIME=<worktree>/.runtime/sandbox-storage/V01-003
python -B -W error -m unittest discover -s tests/database -p test_*.py
python -B scripts/verify_repository.py
python -B -W error -m unittest discover -s tests/repository -p test_*.py -v
python -B scripts/validate_handoff.py
python -B scripts/validate_architecture_baseline.py
python -B tests/architecture/check_release_gates.py --target v0.1-start
git diff --check
```

另执行未配置 PostgreSQL 的同一 database discovery，确认它只跳过 integration class、不会启动进程；CI 和最终本机证据均设置 `ASSETLIBRARY_TEST_POSTGRES_REQUIRED=1`，缺少或版本错误时会失败而不是跳过。

## 架构与契约测试

- 21 个连接无关 database tests：manifest/哈希/顺序/owner/LF/SQL 安全、备份发布失败清理、版本/确认参数、metadata 路径与精确 manifest/role hash、M0 Spike 隔离。
- 5 个真实 PostgreSQL tests：应用/重跑/最小权限、并发、角色成员关系漂移、失败原子性与 ledger 漂移、备份恢复/forward replay。
- 29 个 repository tests：包含 5 个 V01-003 CI/输入不变/无凭证/唯一入口/静态接线合同，以及既有 .NET、SDK、任务激活和缓存清洁回归。
- 14 个 architecture tests：三层 CI 概念合同、依赖方向、语言预算、模块边界、合同摘要和 M0 gate ledger。
- 唯一测试用例总数：69；`verify_repository.py` 对其中 database static 与 architecture 再执行一次，重复运行不重复计数。

## 通过

- `MIGRATION_MANIFEST_OK migrations=2 modules=11 postgresql=16.15`。
- 最终 database 全套：26/26 passed，0 failed，0 skipped；其中 integration 5/5 在 8.676 秒完成。
- repository：29/29 passed；architecture：14/14 passed。
- 权限投影返回 `11|11` clean；26 个固定角色全部满足非登录、非管理员属性和精确成员关系。
- 并发两次 apply 均完成且 ledger 只有版本 1/2 各一行；重跑输出 no pending。
- 所有最终测试结束后任务内 PostgreSQL 进程 `0 -> 0`，新临时集群自动删除；最终 V01-003 runtime 目录不存在。

## 失败 / 跳过

- 最终必需链路：0 failed，0 skipped。
- 一次清理前 repository 运行按预期失败：EDB 发行包内置大量 `pgAdmin 4/python/**/__pycache__`，触发“仓库必须 cache-free”门禁。确认无任务进程后删除整个精确 V01-003 runtime，再运行 repository 得到 29/29；未删除源码或系统文件。
- 无 PostgreSQL 配置的安全模式运行记录为 `OK (skipped=1)`；此结果只证明默认无副作用，不计入真实集成通过数。

## 故障注入与恢复验证

- 故意在创建 sentinel 后除零：迁移事务失败，sentinel 与新 ledger 行均不存在，原 2 个版本和预迁移备份保留。
- 已应用 SQL 变更并同步伪造 manifest hash、ledger 名称变更、删除 ledger 前缀均在新备份/DDL 前阻断。
- 两个 runner 并发执行由固定 advisory lock 串行；重复版本只输出 already-applied，不产生重复 ledger。
- 注入 `pg_dump` 失败不发布目录；archive 路径穿越、hash 漂移、截断但重新计算 hash 的无效 custom archive均被拒绝。
- 对已有 schema/object 的数据库拒绝恢复且保留对象；合法 archive 只恢复到空目标，恢复后 ledger 与 metadata 完全一致，再 forward replay 得到最新 schema。
- runtime 可对本 schema 的 owner-created probe 执行 SELECT/INSERT/UPDATE/DELETE；创建表/schema、访问其他 schema/ledger、使用 public schema 或切换 owner 均被拒绝。

## 性能数据

- 固定 11 schema/26 role 的真实集成全套 8.676 秒；迁移 metadata/catalog 操作为固定小集合。
- 本任务不创建资产表或资产查询，因此不重复 M0-005 的 50 万资产 profile；没有引入与资产数线性加载的代码路径。

## 尚未覆盖

- 新 Ubuntu `postgres:16.15-bookworm` GitHub Actions job 的首次远程执行结果，待合并检查留存。
- 生产环境、真实备份介质、生产凭证和跨主机灾备演练，按设计不在本任务连接或模拟。
- 未来业务表的 RLS、业务索引、容量和数据访问库，由其 owning task 在本迁移底座上追加迁移与恢复测试。
