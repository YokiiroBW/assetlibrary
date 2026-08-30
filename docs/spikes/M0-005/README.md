# M0-005 PostgreSQL Spike

结论：在本机 Linux x86-64、PostgreSQL 16.15（从官方 PostgreSQL 16.15 源码
构建，`--without-readline --without-zlib`）上，PostgreSQL 单库足以覆盖本 Spike
的库/资产投影、durable task、lease、outbox 和 500,000 行基础检索。该结论是
主机特定证据，不是生产阈值；M0-009 前不冻结 SQL 为公开合同。

官方版本策略显示 PostgreSQL 18.6、17.11、16.15、15.19、14.24 均受支持，主版本
支持五年；候选生产基线建议 M0-009 决定 17/18，Spike 使用 16.15 仅因本机客户端
和可复现源码包。官方许可证是 PostgreSQL License（BSD/MIT 类宽松开源许可证）。

- 版本策略：https://www.postgresql.org/support/versioning/
- 16.15 源码：https://ftp.postgresql.org/pub/source/v16.15/postgresql-16.15.tar.bz2
- 许可证：https://www.postgresql.org/about/licence/

## 交付对象

`database/migrations/001_core.sql` 定义 `library`、`tasks`、`events` 模块所有权，
相对路径与库 ID，强哈希，FTS/trigram/keyset 索引；`002_ledger.sql` 明确前向迁移
策略。测试 runner 对每个迁移持有事务级 advisory lock，记录 SHA-256 checksum；
重复应用跳过，相同版本 checksum 漂移失败。事务失败不会留下对象。

`tasks.claim_one` 使用 `FOR UPDATE SKIP LOCKED`，lease/heartbeat 校验 owner，过期
任务 reclaim，最大尝试次数、取消和 idempotency key 均可验证。资产状态与 outbox
事件在同一事务提交；发布端应按 event_id 幂等、允许 at-least-once，不宣称 exactly-once。

## 证据

命令：`PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s tests/spikes/postgres -p 'test_*.py' -v`

结果：2 tests OK；首次迁移、并发 runner 序列化、重复应用、checksum drift、失败迁移
回滚、错误 owner heartbeat、过期 reclaim、取消/幂等插入、outbox owner/retry、权限
正负例及服务重启后资产持久化均通过。加载 500,000 条合成资产约 45 秒（本机特定）。
每次运行动态写入 ignored evidence：权限约束的 tuple-keyset 使用
`asset_library_path_keyset`，全文
使用 `asset_search_gin` 且命中 50,000/500,000，trigram 使用 `asset_filename_trgm`
且精确命中 1 行；同时记录 5 次 warm latency、表/索引大小和 BUFFERS 计划。
相对路径过滤在当前 500k 数据上实际为 Parallel Seq Scan（约 25–29 ms），未擅自
增加索引，交由 M0-009 决定 path index。

所有 server data/socket/log/cache 位于 `/tmp/m005-pg-*`，测试 teardown 停止服务并
删除目录；源码/构建目录也位于 `/tmp`。没有访问外部 5432 或写入真实资产。

## M0-009 冻结建议与风险

冻结单一迁移 owner、schema/table owner、查询 contract、事务边界、forward-only 与
checksum 规则；生产迁移需要备份/恢复门禁。暂不引入 Redis 或独立搜索集群。只有
在真实工作负载下 keyset/FTS/trigram 的 P95、写放大、VACUUM 或并发 lease 证据持续
无法满足预算时，才通过 ADR 评估缓存/搜索组件。当前风险：尚未验证生产 ORM/备份
恢复编排，FTS 词典和权限过滤语义仍需核心服务合同化。
