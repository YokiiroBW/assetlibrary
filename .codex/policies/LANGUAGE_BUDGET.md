# 语言与框架预算

默认候选预算，最终由 M0 Spike 和 ADR 冻结：

| 区域 | 语言/框架 |
|---|---|
| 服务端核心、Worker、Windows服务端 | C# / .NET |
| Windows独立客户端、AssetHost | C# / WinUI 3 或验证后的单一等价栈 |
| Windows Shell | C++ / WinRT / COM，仅最小桥接 |
| Web | TypeScript / React |
| 浏览器扩展 | TypeScript |
| Android/平板 | Kotlin / Jetpack Compose |
| 数据库 | PostgreSQL SQL |
| 构建、生成与仓库工具 | Python |

规则：

1. 同一职责不得并存两套主框架。
2. 新业务不使用裸 JavaScript；Android 新业务不混用 Java。
3. 禁止无 ADR 引入额外 Node/Go/Rust 服务、Electron、第二前端框架、第二数据库或独立搜索/消息集群。
4. 平台专用语言只可用于确有必要的系统集成边界。
5. 重大依赖必须记录许可证、安全更新、跨平台、包体和退出策略。
