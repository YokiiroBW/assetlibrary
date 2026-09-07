# Production database migrations

`production/manifest.json` is the only production migration entry point. The two SQL files directly under this directory are retained M0-005 Spike evidence and are deliberately not listed by the production manifest.

The production foundation targets PostgreSQL 16.x and is verified against 16.15. It reserves one schema, one NOLOGIN owner role and one NOLOGIN runtime role for each frozen server module, plus a migration ledger and a read-only catalog projection that audits those boundaries.

V01-004 adds the first module-owned business objects without changing the foundation migrations: `LibraryStorage` stores source availability and non-overlapping physical roots, `AssetIdentity` stores stable entries, explicit empty-library snapshots and bounded initial-scan staging, and `ScanReconciliation` stores completeness-aware scan runs. Each migration executes as its frozen module owner, creates objects only in that module schema, and has no cross-schema foreign key or write path. The initial-scan commit function refuses to replace an existing index; an incomplete scan can only discard its staging rows.

V01-005 adds the `TaskHealth` production core in migration 6. It owns bounded durable-task and outbox queues, fenced leases (`owner + token + generation + expiry`), cooperative cancellation, retry/dead-letter state, and monotonic system/library/asset health observations. Runtime callers receive read access plus narrowly scoped `SECURITY DEFINER` functions; direct table mutation remains denied. Outbox delivery is explicitly at-least-once, and the stable event ID is the consumer idempotency key. An identical enqueue replay returns the original record and cannot reschedule it: generated IDs and availability/write timestamps are not identity fields, while task/event type, payload, priority/attempt policy, source, schema version and occurrence time remain conflict-checked as applicable. No network publisher or physical-file operation is part of this migration.

V01-006 adds the permission-filtered Web/Gateway read path in migrations 7–9. `LibraryStorage` owns per-library access rows and publishes catalog/permission security-barrier views; `AssetIdentity` publishes only present physical facts plus bounded browse and path-search indexes; `GatewayAuth` owns authenticated-subject mapping and the four `SECURITY DEFINER` read functions consumed by its runtime adapter. The source modules grant their owner-approved views only to the GatewayAuth owner, while the GatewayAuth runtime can execute the final functions but cannot inspect principals or source tables. Directory and search queries join authorization before returning rows, expose only library-relative paths, use keyset pagination, and never read asset bytes.

## Safety model

- `migration_tool.py validate` is the default, connection-free operation.
- Role provisioning is explicit and intended for a fresh AssetLibrary database/isolated test cluster. It never creates a login or stores a password.
- `apply` requires the target database name twice and a backup directory. When work is pending, a verified custom-format dump and its metadata are published together by one same-filesystem directory rename before any DDL.
- `restore` requires the target database name twice and refuses a target containing any user schema or object. It never drops or creates a database.
- A successful restore is followed by a read-back comparison of the restored migration ledger against the SHA-256-referenced backup metadata.
- Connection passwords are accepted only through libpq mechanisms such as `PGPASSWORD` or a password file. They are never command-line arguments or backup metadata.
- PostgreSQL client and server major versions must both equal the manifest major. Commands have bounded connection, lock, statement and process timeouts.

Examples (placeholders only):

```text
python -B database/migrations/production/migration_tool.py validate
python -B database/migrations/production/migration_tool.py status --host 127.0.0.1 --port 5432 --username deploy_login --database assetlibrary --confirm-database assetlibrary --postgres-bin /path/to/postgresql/bin
python -B database/migrations/production/migration_tool.py apply --host 127.0.0.1 --port 5432 --username deploy_login --database assetlibrary --confirm-database assetlibrary --backup-dir /isolated/backup/path --postgres-bin /path/to/postgresql/bin
```

`provision-roles` needs an administrative deployment connection because roles are cluster-global. The deployment login is separately granted direct `CONNECT` on the target database and `assetlibrary_migration_executor WITH INHERIT FALSE, SET TRUE`. Runtime logins are separately granted direct `CONNECT` and exactly the module runtime roles they need. Those login identities and credentials are deployment configuration, not repository data.

All fixed roles are `NOLOGIN NOINHERIT`. One membership is intentionally narrower than that role-level default: `assetlibrary_migration_owner` receives the 11 module-owner roles with `INHERIT TRUE, SET TRUE, ADMIN FALSE` so it can establish their default privileges in the foundation migration. The deployment login still reaches that role only through non-inheriting executor membership, and runtime roles never inherit or set an owner role. Provisioning fails if any fixed role has an unexpected outgoing membership or membership options.

Cross-cluster recovery first applies the same role provisioning to the empty destination database, then restores the trusted archive. A database dump does not include cluster-global roles; the committed, checksum-pinned NOLOGIN role definition is therefore part of the recovery contract. Product users and library permissions are future domain data and will be included in software backups; database login secrets are not.

## Ownership rules for future migrations

- Add a new contiguous manifest entry and never edit an applied file.
- Set `module` and `owner_role` to the single owning module. The runner selects that role; migration SQL must not issue `SET ROLE` or transaction commands.
- A module may create/write only objects in its own schema. Cross-module reads require a separately reviewed, explicitly granted read-only projection owned by the source module.
- `migration`, its ledger and the audit projection remain exclusive to `database-migration-owner`.
- Business tables for Library/Asset/Scan and TaskHealth/Outbox belong to V01-004 and V01-005. The M0-005 `library`, `tasks` and `events` objects are not production tables.

Official references: PostgreSQL [versioning policy](https://www.postgresql.org/support/versioning/), [role membership](https://www.postgresql.org/docs/16/role-membership.html), [`GRANT`](https://www.postgresql.org/docs/16/sql-grant.html), [`pg_dump`](https://www.postgresql.org/docs/16/app-pgdump.html), and [`pg_restore`](https://www.postgresql.org/docs/16/app-pgrestore.html). PostgreSQL 16.15 is supported through 2028-11-09; the Windows task-local evidence uses the EDB binary archive linked from the PostgreSQL Windows download page.

## ALIGN-001 查询分词对齐

迁移 13 由 AssetIdentity owner 以 `CREATE OR REPLACE FUNCTION` 修正只读搜索：输入查询与既有 `path_search_document` 统一分割 `/._-`，使完整文件名和路径可匹配。保留函数签名、调用方权限过滤、keyset、GIN 索引及既有 execute grants；不改迁移 1–12、资产原文件或任何功能门禁。升级遵循既有备份优先/前进迁移/空库恢复合同。

## V01-017 持久只读试用

迁移14在 GatewayAuth 内提供已获本机授权的管理员恢复准备查询；其重放仍取原期望凭据版本，由既有恢复函数最终 CAS。迁移15由 LibraryStorage 保存按调用者隔离的登记幂等记录及每库可用性，普通 catalog 投影保持原列合同。迁移16增加 AssetIdentity 快照事实查询和每库扫描会话锁，首次快照保持不可替换。迁移17由 ScanReconciliation 保存接受/补投意图、调用者操作幂等绑定、任务与扫描尝试关联及进度。迁移18由 TaskHealth 增加按类型领取、有界最终提交租约行锁，以及经扫描快照证实的成功终态修复。没有跨schema写表、外键、权限扩张或原文件写路径。

数据库先保存扫描请求，再幂等入队；重启可补投。发现按256条默认批次暂存，每批短事务；完整发现才原子提交。中断只删除相应未提交stage；已提交快照按scan_id核对后修复任务结果，绝不清空索引重扫。任务提交guard在自己的事务中保留TaskHealth租约行锁，AssetIdentity通过公开端口完成自己的事务。二者之间的进程/响应故障依靠不可变快照恢复，不承诺跨模块分布式原子事务。

原生验证入口沿 `tests/database/test_migration_integration.py`：`test_read_only_trial_dotnet_runtime_and_isolated_workers` 以隔离PostgreSQL运行已构建ReadCore测试，并调用真实Host只读worker入口。需设置既有 `ASSETLIBRARY_TEST_POSTGRES_BIN`、`ASSETLIBRARY_TEST_POSTGRES_REQUIRED=1`、`ASSETLIBRARY_TEST_DOTNET` 和 `ASSETLIBRARY_TEST_HOST_DLL`；Windows试用ACL证据只在Windows运行。未授权其他发行门禁或通用重新扫描。
