# V01-002 交接摘要

## 完成状态

`ready_for_review`

- 分支：`codex/v01-002-assetlink-sdk-generation`
- 实现 commit：`3c43f3d912f1702e67fc8bafb37d84e3a593f57b`
- Worktree：`C:\YOKI\Codex\AssetLibrary-worktrees\V01-002`

## 完成内容

- 新增唯一生成入口 `scripts/generate_assetlink_sdks.py`，从 `contracts/assetlink/**` 的 21 份 canonical schema 确定性生成 .NET、TypeScript、Kotlin/JVM 三端 SDK。
- 三端覆盖相同的 20 个已知 `message_type`，提供显式 unknown 分支、完整 JSON 往返、生成字段访问器以及受检 uint64 十进制字符串转换。
- 根生成清单保存单份完整 schema shape、合同摘要和 6 个 target 输出摘要；三端各提交相同的 `.contract-source.sha256`。
- `--check` 对缺失、过期、手改和额外生成文件失败关闭；生成器还拒绝缺失消息、公共 wire kind 漂移与无法解析的跨文件引用。
- .NET 使用现有 .NET 10/C# 14 底座且无运行时 NuGet 依赖；TypeScript 无运行时 npm 依赖；Kotlin 仅使用 `kotlinx-serialization-json` 的 JSON tree API。
- 新增版本、来源、许可证、锁文件、Gradle 校验元数据、源码能力边界、敏感日志和体积门禁，并接入 Windows/Ubuntu `fast-merge`。

## 关键决策

- canonical JSON Schema 仍是唯一产品合同；生成清单只是可审计证据，不能反向成为合同来源。
- SDK 只分类和保存 wire envelope，不实现认证、权限、路径、网络、重试、传输状态、文件读写或 Provider 行为。
- 已知消息保留原始 JSON object，因此未来可选字段和 enum-like 字符串可以无损往返；未知 `message_type` 不会被冒充为已知命令。
- 公共 schema 出现新定义或既有 wire kind 变化时，生成器要求显式升级映射并重新评审，避免静默生成错误类型。
- Kotlin 使用依赖最小的 `sdkTest` 可执行验证而未引入测试框架；`check` 和 `build` 都依赖该验证。

## 修改文件

- 生成器与策略：`scripts/generate_assetlink_sdks.py`、两个 SDK 验证脚本、`eng/assetlink-sdk-dependency-policy.json`。
- 三端产物：`packages/sdk/assetlink/dotnet/**`、`typescript/**`、`kotlin/**` 以及 `generation-manifest.json`。
- 测试与构建：SDK Python 测试、.NET MSTest、TypeScript Node test、Kotlin `sdkTest`、solution、架构/仓库测试和 CI workflow。
- 任务证据：`.codex/tasks/V01-002.md` 与本交接三件套。

完整清单见 `result.json.changed_files`。

## 模块边界、依赖方向与复用

- 三端 SDK 不引用 `apps/**`、`services/**`、`gateways/**` 或其他业务模块；只有测试引用 .NET SDK 项目。
- 复用 M0-003 冻结的 AssetLink schema、未知字段/evolution 和 uint64 规则，复用 M0-009 架构、handoff、CI tier 与启动门禁，复用 V01-001 .NET solution 和依赖审计底座。
- 没有复制权限、路径、哈希校验、传输、同步、垃圾桶或文件安全业务规则；没有跨模块内部访问。

## 新语言、框架或重大依赖

- 激活 ADR-0012 已批准的 TypeScript 与 Kotlin/JVM SDK 技术家族，没有增加第二套同职责框架。
- 固定构建工具：Node.js `24.20.0`（MIT）、pnpm `11.19.0`（MIT）、TypeScript `6.0.3`（Apache-2.0）、Eclipse Temurin `21.0.12+8`（GPL-2.0-only WITH Classpath-exception-2.0）、Gradle `9.3.1`（Apache-2.0）、Kotlin JVM plugin `2.3.20`（Apache-2.0）。
- 唯一新增 SDK 运行时依赖是 `kotlinx-serialization-json 1.11.0`（Apache-2.0）；TypeScript 无运行时包，.NET 仅使用 BCL。
- pnpm lock、Gradle dependency lock、Gradle SHA-256 verification metadata 和 wrapper/distribution SHA-256 均提交并由门禁核验；升级必须显式更新策略和证据。
- 移除路径明确：删除对应 target 目录及 CI/架构激活项即可，不影响 canonical 合同或核心业务模块。

## 共享契约或数据库变化

- 公共 AssetLink schema、API、事件、Provider 合同和数据库 schema/migration：无变化。
- 内部机器合同更新：架构基线要求三端 manifest/lock/verification 文件；`fast-merge` 新增三端生成、构建、测试、审计、许可证、体积和源码边界命令。
- 合同摘要统一按 LF 归一化，避免同一 canonical 文本因 Windows/Ubuntu checkout 行尾不同产生伪漂移。

## 测试结果

- 最终自动检查计数：97 passed，0 failed，0 skipped（architecture 14、repository 24、SDK Python 14、M0-003 contract 21、MSTest 15、Node test 5、Kotlin logical checks 4）。
- .NET locked restore、format、Release build（0 warnings/0 errors）、测试、NuGet 漏洞/许可证和源码门禁通过。
- TypeScript frozen install、lint、strict typecheck、build、Node test、完整 audit、许可证、体积和源码门禁通过；audit 无已知漏洞。
- Kotlin 官方 wrapper 在全新空 Gradle cache 中验证下载摘要并以 strict dependency verification 构建通过；最终 Windows 等价 build/`sdkTest` 通过。
- 生成漂移、合同、handoff、架构、repository、`v0.1-start` 与最终 diff 检查均通过。

## 架构测试与质量门禁

- 负向夹具覆盖生成物缺失/手改/额外文件、schema 集合缺失、公共类型漂移、外部引用失效、版本漂移、Gradle SHA 缺失、构建产物缺失、越界网络源码、敏感日志和业务策略复制。
- 三端都验证未知字段/未来字符串保留、显式 unknown 分支、uint64 上下界及非法输入、非对象/缺失或非字符串 `message_type` 失败关闭。
- Windows/Ubuntu workflow 不允许 `continue-on-error`；浏览器与 Android instrumentation 明确保留给第一个对应消费端，而不是在纯 SDK 层伪造。

## 文件安全、权限与性能影响

- 未读取或写入真实资产，未操作注册表、Explorer、系统服务、Provider、管理员权限、网络监听或生产数据库。
- 下载的 Node/JDK/Gradle 和所有构建缓存均位于被忽略的任务 `.runtime/`；没有安装全局工具或修改系统 PATH。
- 生成 SDK 不包含网络或文件 I/O；parse/serialize 的时间和空间复杂度均为单条消息字节数 O(n)，与 50 万资产目录规模无关。
- 最终产物大小：TypeScript `dist` 16,938 bytes、.NET DLL 20,992 bytes、Kotlin JAR 55,362 bytes，均远低于已审查预算。

## 技术债、已知问题与风险

- Windows/Ubuntu GitHub Actions 矩阵尚未在远程 runner 上执行；本地证据覆盖 Windows 等价链路，Ubuntu 最终证据由合并检查产生。
- 当前生成器有意固定 AssetLink v1 的公共类型集合；未来合同增加 wire kind 时必须显式扩展生成器和三端测试。
- Kotlin `sdkTest` 为依赖最小的逻辑测试 runner，不产生 JUnit XML；首个 Android consumer 仍需增加 Android lint、instrumentation 和平台依赖门禁。
- SDK 只做 wire envelope 基础能力；浏览器兼容、Android API、网络传输、认证和业务语义必须由对应消费端测试。

## 建议合并顺序

V0.1 顺序 `2`。V01-002 已基于 V01-001 完成后的主线并满足 M0-003/M0-009 依赖，可在后续消费 AssetLink 的官方客户端任务之前合并。

## 下一步

- 主协调线程复核实现 commit、交接和最终 diff，随后 fast-forward 合并并更新任务状态。
- 由远程 Windows/Ubuntu `fast-merge` 产生首份双平台三端构建证据。
- V0.1 release、生产文件写入、Provider、Explorer、网络传输和平台发布门禁仍保持阻断；本任务没有关闭这些门禁。

## Codex 线程链接（可选）

未填写；仓库 commit 与交接文件是权威证据。
