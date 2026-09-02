# 模块边界与依赖方向

## 允许依赖

```text
Adapters/UI/Gateways -> Application -> Domain
Infrastructure -------> Application定义的端口
Provider Adapter -----> Provider公开契约
```

## 禁止依赖

- Domain -> Infrastructure/UI/平台SDK
- 模块A -> 模块B的Infrastructure或内部实体
- 客户端/Provider -> PostgreSQL
- Windows Shell -> 网络/数据库/媒体解码/第三方重型运行时
- 多个入口各自实现权限、传输、同步、删除或垃圾桶规则

## 跨模块方式

仅允许：公开 Command/Query、端口、版本化 Contract、领域事件、批准的只读投影。

数据库表有明确模块所有者；跨模块写表一律禁止。确需跨边界修改时，先提交 ADR 或 Contract Change Proposal。

## 冻结模块 owner

`GatewayAuth`、`LibraryStorage`、`AssetIdentity`、`MetadataSidecar`、`ScanReconciliation`、`TransferSync`、`OperationTrash`、`SearchDedup`、`PreviewProvider`、`TaskHealth`、`BackupUpdate` 各自拥有自己的状态、政策、schema 和写模型。机器可读列表位于 `tests/architecture/architecture-rules.json`，完整定义位于 `docs/23_M0架构冻结与质量门禁.md`。

`migration` ledger 由 `database-migration-owner` 独占；AssetLink、Provider 契约和生成 SDK 分别由唯一 contract/generation owner 管理。M0-005 SQL 是 Spike 输入，不授予生产模块跨 schema 写入权。

架构门禁会检查 Domain/Application 反向依赖、跨模块内部引用、循环、数据库直连、Shell 重型依赖和复制核心状态机的明显标记。首次生产构建还必须启用语言原生依赖分析。
