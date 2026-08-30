# M0-005 handoff

status: ready_for_review
implementation_commit: `80babf5325071a227fa20d14420f7c47201a3885`
handoff_commit: recorded by the commit containing this file
branch: `codex/m0-005-postgres-domain-spike`

补充了 task-local 官方源码 bootstrap、同事务 advisory-lock migration runner、权限投影、
owner-aware outbox claim/release/publish 及完整并发/回滚/重启/500k 证据。当前 ready_for_review：

完成了 PostgreSQL 16.15 隔离 Spike：模块 schema/table ownership、前向迁移 ledger
与 checksum、事务失败回滚、durable task lease/heartbeat/reclaim/cancel/idempotency、
事务 outbox，以及 500,000 合成资产 keyset/FTS/trigram 计划。没有生产服务、ORM、
额外数据库或共享契约改动。

建议 M0-009 冻结迁移 owner、查询权限边界和备份/恢复门禁；在真实负载 P95 或写放大
持续超预算前不引入缓存/独立搜索。非目标风险是生产 ORM/备份编排及权限过滤合同尚未验证。
最终 fresh bootstrap 数据库表/索引大小为 114 MB / 201 MB；端到端（含 psql 启动）warm
分布(ms，端到端含 psql 启动)：keyset 32.255/32.997/33.556/33.556，FTS
59.560/61.203/63.476/63.476，trigram 34.585/35.226/36.483/36.483（依次
min/median/p95/max）。表/索引 114 MB / 201 MB。相对路径过滤为 Parallel Seq Scan，
约 25–29 ms，path index 留待 M0-009 决策。Tuple cursor/order 与权限过滤实际使用
`asset_library_path_keyset`。
