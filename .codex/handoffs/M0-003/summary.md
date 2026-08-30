# M0-003 交接摘要

## 完成状态

`ready_for_review`

## 完成内容

交付 AssetLink v1 可执行协议候选：20 个消息信封 schema、1 个公共定义
schema、26 个固定夹具、规范性传输映射、SDK 消费边界，以及 21 项标准库
契约测试。覆盖握手、控制/取消/错误、事件重放、分块上传、Range 下载和完成
后强校验证据；未实现客户端、服务端或文件写入业务。

## 关键决策

- 浏览器基线为 HTTPS JSON、NDJSON 事件流和流式 octet bytes；文件内容不进入 JSON。
- 版本选择为双方完整支持列表的最高精确交集；无精确交集明确失败，不猜测次版本兼容。
- 能力只启用双方交集；未知可选字段、能力和 enum-like 字符串原样保留。
- 主备切换必须核对稳定 `server_id`；角色名不能替代服务身份。
- uint64 统一使用 canonical decimal string，并检查完整无符号 64 位上限。
- 分块以 `(transfer_id, offset)` 作为幂等身份；完成必须重读并验证长度、可读性和 SHA-256。
- 游标过期或 replay gap 必须完整重建，禁止继续增量消费。

## 修改范围

仅修改任务允许的 `contracts/assetlink/**`、`packages/sdk/assetlink/**`、
`tests/spikes/assetlink/**`、`docs/spikes/M0-003/**`、任务包和本交接目录。
实现提交为 `ef22cd349b1bae4670bed7dbf2b0ae29bb85e1bd`。

## 模块边界、依赖方向与复用

该任务是 M0 AssetLink 候选契约的单一所有者。协议只描述 DTO、传输与恢复
证据，权限、路径、存储、同步、锁、审计和写入状态机仍归核心。测试复用
Python 标准库与仓库现有验证脚本；未引入客户端/服务端业务逻辑或跨模块访问。

## 新语言、框架或重大依赖

无。测试仅使用 Python 3 标准库；未增加生产依赖或第二套序列化栈。

## 测试结果

21/21 契约测试通过，0 失败、0 跳过。仓库验证、handoff 验证、架构基线、
JSON/reference 检查、允许路径、空白与缓存残留检查均通过，完整命令见
`tests.md`。

## 文件安全、权限、性能与兼容性影响

协议不授予权限且不执行文件写入。文件字节始终流式处理；单 Range/分块上限
64 MiB，100 GB 与完整 uint64 边界不经 JavaScript `number`。未知消息进入
显式 unknown 分支，不会被解释为已知命令。

## 技术债、已知问题与风险

- 尚未运行真实 Chromium 流、HTTP/2/代理、网络中断、进程崩溃和持久存储恢复。
- .NET、TypeScript、Kotlin 正式 SDK 生成器和服务端状态机尚未实现。
- 测试内 schema oracle 只实现当前契约实际使用的关键词，不是通用 JSON Schema 引擎。

以上风险进入后续实现/冻结门禁，不影响本 Spike 作为候选契约进入评审。

## 建议合并顺序

先保留实现提交 `ef22cd349b1bae4670bed7dbf2b0ae29bb85e1bd`，再合并当前
handoff 元数据提交。M0-005 与 M0-008 可在该候选合并后启动；M0-009 负责最终冻结。

## Codex 线程链接（可选）

仅作为导航，不是唯一交接依据。
