# 语言与框架预算

M0-009 已冻结技术族；精确补丁由首个生产清单与锁/验证文件固定：

| 区域 | 语言/框架 |
|---|---|
| 服务端核心、Worker、Windows/Linux/Docker Host | C# 14 / .NET 10 LTS / ASP.NET Core 10 |
| Windows独立客户端、AssetHost | C# 14 / .NET 10 / WinUI 3 / Windows App SDK 2.x stable |
| Windows Shell | C++17 / Windows SDK 10.0.26100 / WinRT / COM，仅最小桥接 |
| Web | Node.js 24 LTS 构建 / TypeScript 6.0.x / React 19.2.x |
| 浏览器扩展 | TypeScript 6.0.x / Manifest V3 |
| Android/平板 | Kotlin 2.3.x / Jetpack Compose stable BOM / AGP 9.x |
| 数据库 | PostgreSQL 16.x SQL |
| 构建、生成与仓库工具 | Python 3.12+，标准库优先 |

规则：

1. 同一职责不得并存两套主框架。
2. 新业务不使用裸 JavaScript；Android 新业务不混用 Java。
3. 禁止无 ADR 引入额外 Node/Go/Rust 服务、Electron、第二前端框架、第二数据库或独立搜索/消息集群。
4. 平台专用语言只可用于确有必要的系统集成边界。
5. 重大依赖必须记录许可证、安全更新、跨平台、包体和退出策略。
6. 同一版本线的安全补丁经完整 CI 升级；minor/major 或技术族替换必须新 ADR。
7. 首个生产源文件必须与真实构建清单、精确版本 pin 和原生 CI 门禁同提交；空目录只记 inactive，不算已验证。

机器执行源为 `tests/architecture/architecture-rules.json`；证据级别和激活条件见 `docs/23_M0架构冻结与质量门禁.md`。
