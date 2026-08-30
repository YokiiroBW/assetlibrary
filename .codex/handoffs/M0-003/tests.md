# M0-003 测试记录

## 执行环境

Linux x86-64，Python 3 标准库，`codex/m0-003-assetlink-contract` 独立 worktree。

## 执行命令与结果

| 命令 | 结果 |
|---|---|
| `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s tests/spikes/assetlink -p 'test_*.py' -v` | 21/21 通过 |
| `python3 scripts/verify_repository.py` | 通过 |
| `python3 scripts/validate_handoff.py` | 通过 |
| `python3 scripts/validate_architecture_baseline.py` | 通过 |
| `git diff --check c73b347...ef22cd349b1bae4670bed7dbf2b0ae29bb85e1bd` | 通过 |

## 架构与契约验证

- 20 个消息信封 schema 均有有效固定夹具；公共 schema 与全部信封声明
  Draft 2020-12 和唯一稳定 `$id`。
- 所有 26 个夹具均被分类并解析；相对和 internal `$ref` 递归解析通过。
- 允许路径、模块边界、handoff schema、仓库 JSON 与架构基线通过。
- 实现提交是当前分支祖先，`result.commit` 与实际实现提交一致。

## 兼容性与失败/恢复路径

通过固定夹具和语义 oracle 验证：旧/新次版本 offer、无交集失败、未知字段、
未知能力、同 `server_id` failover、断线 replay、cursor-expired snapshot 重建、
gap/乱序拒绝、retry hint、100 GB/uint64 边界、负/非 canonical offset、64 MiB
chunk 上限、重复 chunk 与 hash 冲突、received ranges、Range 边界，以及完成后的
长度/强哈希/可读性证据。原始不完整 handshake 夹具会被当前 schema 拒绝。

## 性能与文件安全

测试只处理小型 JSON 元数据，不读写资产文件。候选协议限定流式字节传输和
64 MiB 单块/Range；本任务不建立吞吐或延迟基线。

## 未执行的外部门禁

真实 Chromium/HTTP 流、HTTP/2 与代理行为、网络中断、跨进程崩溃、持久存储
恢复和三端生成 SDK 尚未执行。本任务无生产客户端/服务端，因此这些是后续
实现门禁而不是自动化测试跳过项。
