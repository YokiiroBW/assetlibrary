# M0-007 性能基础 Spike

本目录的实现是测试支持，不是产品资产模型。`packages/test-support/performance`
只使用 Python 3.12 标准库，记录包含显式 `m0-007.asset-manifest.v1` 版本、稳定
`asset_id` 和物理相对路径。fixture hash 明确标为 `fixture-sha256:`，不代表真实
文件的哈希。

## 使用与安全边界

`generate_assets` 逐条 yield，`write_manifest` 逐行写 JSONL，并在替换目标前完成
flush/fsync；取消会删除 partial 文件。输出路径必须位于仓库
`.runtime/sandbox-storage/` 或系统临时目录，且拒绝路径逃逸和符号链接组件。
重复运行同一配置/种子产生相同字节，可安全替换。实现只生成逻辑大小，不物化文件。

档位由 `GeneratorConfig` 控制：小档使用较小 `count`，正式档为 `count=500000,
hot_directory_count=100000`；首条记录表示 `100 * 1024**3` bytes 的逻辑资产。记录
字段按扩展名、嵌套目录、重复展示名分布，ID 不依赖文件名。

## 500k 基线（隔离目录，一次执行）

命令：

```text
PYTHONDONTWRITEBYTECODE=1 python3 tests/spikes/performance/profile_500k.py
```

结果（2026-08-31，seed `20260831`）：500,000 条；墙钟 9.256015 s（外部 9.33 s）；峰值 RSS
19,608 KiB（外部 19,736 KiB）；JSONL 194,334,025 bytes；54,018.93 records/s；manifest SHA-256
`c7e7e79937fe376a65b37cfd29bfb685ed343650dfe7a72d694f67955601e748`。输出已从
`.runtime/sandbox-storage/M0-007/` 清理，未提交生成数据。

时间和吞吐用于后续回归比较，不构成产品性能门禁。复杂度为 O(n) 时间、逐条流式
处理和固定字段/扩展名摘要内存；查询索引消费应在真实服务端由其模块决定，本工具
不引入数据库或搜索实现。

## 故障计划

`build_fault_plan` 生成版本化、数据驱动的非破坏性事件，覆盖断网、进程崩溃、服务
重启、空间不足、同名冲突、哈希变化、权限拒绝、部分写入和重复重试。它只描述
触发点和可恢复性，不改变系统权限、磁盘配额或网络状态。
