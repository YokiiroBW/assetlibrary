# V01-005 测试记录

## 执行环境

- Windows x64 worktree：`C:\YOKI\Codex\worktrees\V01-005`；基线 `0edf2152747f0ec3beab4b2379a8bd034af13eda`；实现 `74bf6f213af8bafdb4eaabc36b02ac2b718d2caf`。
- .NET SDK：已校验的任务本地 `10.0.111`；可变 CLI home 与 NuGet cache 均位于 V01-005 的 `.runtime`。
- Python：Codex 随附 `3.12.13`，Python 门禁使用 `-B`，不在仓库生成 bytecode。
- PostgreSQL server/CLI：复用已校验的任务本地 EDB no-install `16.15` 工具链，archive SHA-256 `5e8afffe67daf949aeeb03b74951f1ec2324e1888f73fbd036ab0e567ab004d9`；V01-005 数据和进程状态只在自身 `.runtime/pgfull`，仅监听 `127.0.0.1` 随机端口。
- 没有系统安装、服务注册、注册表写入、永久 PATH 修改或真实资产/NAS 访问。

## 执行命令

```text
dotnet restore AssetLibrary.slnx --locked-mode
dotnet format AssetLibrary.slnx --verify-no-changes --no-restore
dotnet build AssetLibrary.slnx -c Release --no-restore
dotnet test AssetLibrary.slnx -c Release --no-build --no-restore
dotnet package list --project AssetLibrary.slnx --include-transitive --vulnerable --no-restore --format json --output-version 1
python -B scripts/validate_dotnet_dependencies.py --solution AssetLibrary.slnx --packages-dir .runtime/nuget --vulnerability-report .runtime/dotnet-vulnerabilities.json
python -B scripts/validate_dotnet_source.py

ASSETLIBRARY_TEST_POSTGRES_BIN=C:\YOKI\Codex\worktrees\V01-004\.runtime\sandbox-storage\V01-004\postgresql-16.15\pgsql\bin
ASSETLIBRARY_TEST_POSTGRES_REQUIRED=1
ASSETLIBRARY_TEST_RUNTIME=C:\YOKI\Codex\worktrees\V01-005\.runtime\pgfull
python -B -W error -m unittest discover -s tests/database -p test_*.py -v
python -B -W error -m unittest discover -s tests/repository -p test_*.py
python -B -W error -m unittest discover -s tests/sdk -p test_*.py
python -B -W error -m unittest discover -s tests/spikes/assetlink -p test_*.py
python -B -W error -m unittest discover -s tests/architecture -p test_*.py

python -B database/migrations/production/migration_tool.py validate
python -B scripts/verify_repository.py
python -B tests/architecture/check_release_gates.py --target v0.1-start
git diff --check
```

交接提交后另执行 `validate_handoff.py`、architecture baseline、release gate、分支差异、缓存残留和任务 PostgreSQL 进程检查。

## 架构与契约测试

- TaskHealth MSTest 32 个：值对象和 JSON 上限、状态/取消/重试策略、入队时间规范化、批次/租约窗口、错误适配器结果、调用方取消、应用超时、outbox 发布/释放和健康 scope/reason 不变量。
- Database 45 个：21 个 manifest/工具、4 个 ReadCore migration、9 个 TaskHealth 静态规则和 11 个真实 PostgreSQL integration；required 模式下不允许跳过数据库测试。
- Repository 29、architecture 14、SDK Python 14、AssetLink contract/spike 21；生成源、锁文件、依赖完整性/许可证、模块边界和 release gate 均覆盖。

## 通过

- 唯一自动测试计数 213/213，0 failed，0 skipped；全 .NET solution 为 90/90，其中 TaskHealth 32/32。
- Release build 0 warning / 0 error；locked restore 与 format verify 通过。
- `.NET dependency policy passed (7 projects, 15 locked packages)`；NuGet 漏洞报告没有阻断项。
- `MIGRATION_MANIFEST_OK migrations=6 modules=11 postgresql=16.15`；真实数据库完整套件 45/45，其中 integration 11/11。
- repository 29/29、SDK 14/14、AssetLink 21/21、architecture 14/14；`verify_repository.py` 与 `v0.1-start` 通过。
- 迁移 6 SHA-256 为 `41eedeef1174c48de01fd6f0c4665411e3043c3b0572fb13bd36a8cf15779aa2`，与 manifest 精确一致。

## 失败 / 跳过

- 最终必需链路：0 failed，0 skipped。
- 首次完整 PostgreSQL 编排使用过长的 Windows 临时路径，`pg_dump` 因路径长度失败；测试数据仍在任务沙盒内。改为短路径 `.runtime/pgfull` 后完整 45/45 通过，该问题不属于代码失败。
- 一次辅助调用沿用了不存在的旧 AssetLink 测试目录，测试未启动；定位真实入口 `tests/spikes/assetlink` 后 21/21 通过。
- 早期语法探测生成的两个精确 `__pycache__` 已在确认路径后移除；最终仓库缓存残留为 0，后续 Python 命令均使用 `-B`。

## 故障注入与恢复验证

- 错误 owner、旧 token、旧 generation、过期 lease 均不能 heartbeat、finish、publish 或 release；过期回收并重新 claim 后旧 worker 仍被围栏。
- 排队取消立即终止；租约中取消保留围栏并由 heartbeat 观察；失败/取消确认收敛为 cancelled，而有效当前租约的成功结果在竞态中保留 succeeded。
- retryable failure 在 attempt 上限前重新排队，到达上限进入 failed；outbox release/过期回收在发布上限后进入 dead-lettered，记录不删除。
- 两个并发 claimant 各取 10 项且集合不重叠；NULL/越界批次被拒绝。
- 运行角色直接 UPDATE/DELETE/INSERT 被拒绝，只能 SELECT 和执行受控函数；跨 schema 权限保持拒绝。
- 陈旧健康观测不会覆盖较新状态；system/library/asset 三种 scope 与 reason 约束由 C# 和数据库共同验证。
- PostgreSQL 停止并重新启动后，task、outbox 和 health 三类行仍各保留一条。
- 应用端验证底层 store 忽略取消、返回越界 lease、重复 ID、不可能回收数量或错误终态时失败关闭。

## 性能数据

- 在真实 PostgreSQL 中分别插入 10,000 个 queued task 和 10,000 个 pending outbox event。
- `EXPLAIN (COSTS OFF)` 分别命中 `durable_task_claim_index` 与 `outbox_event_claim_index`；单次 claim 均严格返回 256 项。
- 应用 claim 与 reclaim 的绝对批次上限为 256/1024；没有全队列物化接口。
- 未执行 50 万任务/事件正式 profile；本结果只证明 10,000 行下的索引选择和有界批量，不能外推生产吞吐。

## 尚未覆盖

- 生产 PostgreSQL adapter、依赖注入/API 组合、worker supervisor 和真实 outbox publisher/consumer。
- Provider 网络断连、确认丢失与消费者 event-ID 去重的端到端测试。
- 50 万任务/事件、长期积压、数据库空间不足、VACUUM/归档/保留策略和真实生产重启演练。
- V01-004 资产提交与扫描日志终态之间的 durable reconciliation 接线。
