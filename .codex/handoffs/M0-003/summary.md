# M0-003 交接摘要

## 完成状态

`ready_for_review`

## 完成内容

交付 AssetLink v1 JSON 控制、握手、事件/重放、统一错误和 Range/分块传输候选；JSON/NDJSON/流式 octet-stream 映射、版本能力协商、同 server_id 主备切换、幂等、游标过期重建、强哈希和 canonical uint64 字符串边界均已规范化。

## 关键决策

浏览器基线采用 HTTPS JSON；事件采用 NDJSON；文件内容永不进入 JSON。未知次版本字段/能力前向兼容，未知主版本明确失败。

## 修改文件

`contracts/assetlink/**`、`packages/sdk/assetlink/README.md`、`tests/spikes/assetlink/**`、`docs/spikes/M0-003/protocol-candidate.md`。

## 模块边界、依赖方向与复用

仅修改 assetlink-contract 所有权目录；无客户端、服务端、数据库或其他公共契约访问。复用既有 handoff schema 和 Python 标准库；未复制业务逻辑。

## 新语言、框架或重大依赖

无；测试仅使用 Python 标准库。

## 共享契约或数据库变化

新增/变更仅 AssetLink v1 契约，未改数据库。

## 测试结果

5 tests passed；覆盖正负协商、同身份 failover、游标重放、100GB、负 offset、未知字段及原始草案回归。

## 架构测试与质量门禁

JSON 解析、Draft/$id 唯一性和 git diff --check 通过；运行 handoff/architecture validators 见 tests.md。

## 文件安全、权限与性能影响

协议不授予权限；文件体始终流式，64 MiB 分块上限，100GB 元数据使用 uint64 约束；强哈希完成校验语义明确。

## 技术债、已知问题与风险

正式 SDK 生成器和服务端实现留待 M0-009/后续任务；当前 Python validator 是最小结构验证器，不替代正式 JSON Schema 引擎。

## 建议合并顺序

先合并实现/测试 commit `6f550e1`，再合并本 handoff 元数据 commit。

## 下一步

协调器评审并在 M0-009 冻结生成器、正式 HTTP/2/浏览器消费测试；服务端状态机不属于本任务。

## Codex 线程链接（可选）

仅作为导航，不是唯一交接依据。
