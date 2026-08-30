# M0-005 handoff

status: partial
implementation_commit: `63569b4e52e52bb29614c4c7905d031efa92c059`
handoff_commit: recorded by the commit containing this file
branch: `codex/m0-005-postgres-domain-spike`

补充了 task-local 官方源码 bootstrap、migration advisory lock 内序列化入口及 outbox
claim/publish 函数。当前仍为 partial：

完成了 PostgreSQL 16.15 隔离 Spike：模块 schema/table ownership、前向迁移 ledger
与 checksum、事务失败回滚、durable task lease/heartbeat/reclaim/cancel/idempotency、
事务 outbox，以及 500,000 合成资产 keyset/FTS/trigram 计划。没有生产服务、ORM、
额外数据库或共享契约改动。

建议 M0-009 冻结迁移 owner、查询权限边界和备份/恢复门禁；在真实负载 P95 或写放大
持续超预算前不引入缓存/独立搜索。未决风险是生产 ORM/备份编排及权限过滤合同尚未验证。
