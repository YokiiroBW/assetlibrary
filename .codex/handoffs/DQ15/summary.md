# DQ15 同一 CI 作业的重复检查清理

基线 d7b43eca94136e90303cce585f2b96c80223f3af，分支 codex/quality-life-20261003。只修改 fast-merge 编排及其既有合同测试。

Repository verification 已执行 Alpha 审查、migration manifest 验证和对应静态测试，因此删除工作流随后对这三项的重复调用。同步 ci-tiers 的直接命令清单，保留独立 PostgreSQL 作业内的迁移验证和数据库全套。既有 Alpha 工作流测试改为验证统一入口仍执行审查且没有直接重复步骤。

没有产品、原始资产、权限、数据库结构、依赖或发布状态修改。验证范围与既有失败见 tests.md；没有运行无关客户端或真实 NAS 流程。
