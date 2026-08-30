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
