# Architecture Tests

此目录保存自动化架构边界测试。M0-009 必须根据最终技术栈把本基线转换为可执行检查，并作为 CI 阻断门禁。

最低检查：

1. Domain 不引用 Infrastructure、UI、数据库或平台 SDK；
2. 模块不引用其他模块的 Infrastructure/内部实现；
3. 无循环依赖；
4. 客户端、Provider、Shell 不依赖 PostgreSQL；
5. Shell 不链接网络、数据库、媒体解码或第三方重型库；
6. 新语言/框架必须与 `LANGUAGE_BUDGET.md` 和已批准 ADR 一致；
7. 公共契约变化后生成 SDK 与消费者契约测试一致；
8. 不允许新增无边界万能共享目录。

当前 `scripts/validate_architecture_baseline.py` 只验证交接骨架和政策是否齐全；实际 C#、TypeScript、Kotlin、C++ 依赖图测试由 M0-009 实现。
