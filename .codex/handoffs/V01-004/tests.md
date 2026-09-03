# V01-004 测试记录

## 执行环境

- Windows x64 worktree：`C:\YOKI\Codex\worktrees\V01-004`；基线 `056e318c547e669a1ef92ab59b2c53facf28dc29`；实现 `71cb1494eb25903a9628cf1b076aa7b10b75012f`。
- .NET SDK：任务本地 `10.0.111`；Python：Codex 随附 `3.12.13`，测试均使用 `-B`。
- PostgreSQL server/CLI：任务本地 EDB no-install `16.15`，仅监听 `127.0.0.1` 随机端口；archive 大小 `333048048`，SHA-256 `5e8afffe67daf949aeeb03b74951f1ec2324e1888f73fbd036ab0e567ab004d9`。
- Kotlin 门禁：任务本地 Temurin `21.0.12+8`（archive `205069442` bytes，SHA-256 `9ba963ee2371874a74185d18bc7bb2ab9407df7683300855ed7606e0662321d0`）、Gradle `9.3.1`（archive `137037885` bytes，wrapper 固定 SHA-256 `b266d5ff6b90eada6dc3b20cb090e3731302e553a27c5d3e4df1f0d76beaff06`）、Kotlin `2.3.20`。
- JDK、Gradle、.NET 和 PostgreSQL 均只在 `.runtime/sandbox-storage/V01-004`；没有系统安装、服务注册或永久环境变量修改。

## 执行命令

```text
dotnet restore AssetLibrary.slnx --locked-mode
dotnet format AssetLibrary.slnx --verify-no-changes --no-restore
dotnet build AssetLibrary.slnx --configuration Release --no-restore
dotnet test AssetLibrary.slnx --configuration Release --no-build --no-restore
python -B scripts/validate_dotnet_dependencies.py
python -B scripts/validate_dotnet_source.py
dotnet list AssetLibrary.slnx package --vulnerable --include-transitive

ASSETLIBRARY_TEST_POSTGRES_BIN=<task-local-16.15-bin>
ASSETLIBRARY_TEST_POSTGRES_REQUIRED=1
ASSETLIBRARY_TEST_RUNTIME=<worktree>/.runtime/sandbox-storage/V01-004
python -B -W error -m unittest discover -s tests/database -p test_*.py -v
python -B -W error -m unittest discover -s tests/repository -p test_*.py -v
python -B -W error -m unittest discover -s tests/sdk -p test_*.py -v
python -B -W error -m unittest discover -s tests/spikes/assetlink -p test_*.py -v
python -B -W error -m unittest discover -s tests/architecture -p test_*.py -v
python -B scripts/verify_repository.py

pnpm --dir packages/sdk/assetlink/typescript install --frozen-lockfile
pnpm --dir packages/sdk/assetlink/typescript run lint
pnpm --dir packages/sdk/assetlink/typescript run typecheck
pnpm --dir packages/sdk/assetlink/typescript run test
pnpm --dir packages/sdk/assetlink/typescript audit --audit-level low
packages/sdk/assetlink/kotlin/gradlew.bat -p packages/sdk/assetlink/kotlin --no-daemon --dependency-verification strict build sdkTest
python -B scripts/validate_assetlink_sdk_dependencies.py --require-build-artifacts
python -B scripts/validate_assetlink_sdk_source.py
```

最终交接提交后还执行 `validate_handoff.py`、`validate_architecture_baseline.py`、`check_release_gates.py --target v0.1-start`、`git diff --check`、缓存残留和任务 PostgreSQL 进程检查。

## 架构与契约测试

- ReadCore MSTest 43 个：根规范化/重叠/探测、值对象、默认忽略、在线/离线/空库、批次、reparse、初始化/发现/提交/日志失败、取消、超时和真实沙盒摘要不变。
- Database 31 个：25 个静态/工具测试和 6 个真实 PostgreSQL 16.15 integration；新增迁移连续性、owner/schema、无物理写词汇和初扫 fail-closed 合同。
- Repository 29、architecture 14、SDK Python 14、AssetLink contract/spike 21、TypeScript Node 5；生成源、锁文件、依赖完整性/许可证/大小预算和 v0.1 gate 均覆盖。

## 通过

- 唯一自动测试计数 172/172，0 failed，0 skipped；`.NET` 全解决方案为 58/58，其中 ReadCore 43/43。
- Release build 0 warning / 0 error；NuGet 和 pnpm 未报告已知漏洞。
- `MIGRATION_MANIFEST_OK migrations=5 modules=11 postgresql=16.15`；真实数据库 31/31，其中 integration 6/6 且 required 模式下不能跳过。
- Kotlin `build sdkTest` 在 1m40s 完成，4 个 Gradle actionable tasks 全部执行；输出 `Kotlin AssetLink generated SDK verification passed.`。
- TypeScript lint/typecheck/build/test 通过，Node 5/5；SDK 产物完整性与 source policy 通过。
- repository 29/29、architecture 14/14、SDK 14/14、AssetLink 21/21；`verify_repository.py`、`v0.1-start`、manifest 和 diff 门禁通过。

## 失败 / 跳过

- 最终必需链路：0 failed，0 skipped。
- PostgreSQL 发行包可选 `pgAdmin 4/python/**/__pycache__` 曾触发仓库 cache-free 门禁；确认任务进程为 0 后仅移除精确的任务沙盒 `pgsql/pgAdmin 4`，保留原始 ZIP，再运行 repository 得到 29/29。
- Gradle wrapper 直连下载两次被网络重置；改用 BITS 下载官方 Gradle GitHub 发行包，SHA-256 与 wrapper 固定值完全一致后放入任务本地 wrapper cache，构建成功。该环境问题不计为代码或最终测试失败。

## 故障注入与恢复验证

- 离线库在创建 scan/session 前返回；目标 Library ID 错配在日志/index 调用前拒绝。
- 初始化失败、发现异常、普通内部异常、用户取消和超时均验证终态与 abort；未关联的 `OperationCanceledException` 不会被误标为用户取消/超时。
- complete 前的失败不会生成快照；混入其他 library 的 staged row 被 SQL 拒绝且 staging 保留以便显式 abort；重复 initial commit 被拒绝。
- complete 成功后的 journal 失败不会调用 abort 或删除已提交的完整资产快照，错误会显式向上冒泡。
- 同源相同/父/子根在并发安全函数中拒绝；路径前缀近似不误判；不同源允许。runtime 跨 schema 读写均被拒绝。
- reparse 目录不递归、reparse 文件不解引用长度；沙盒扫描前后路径、内容、长度和时间戳的强摘要相同。

## 性能数据

- 10,000 个生成观察项以 40 个批次完成，每批最多 256；应用批次硬上限 1024，没有整库物化。
- 真实发现使用深度栈和流式 `IAsyncEnumerable`，每 128 项协作让出；数据库索引针对库+路径、状态、稳定 ID 和扫描开始时间。
- 没有运行 50 万资产完整 profile；该门禁按路线图留给生产适配器和容量任务，不能从 10,000 项测试外推吞吐结论。

## 尚未覆盖

- 生产 PostgreSQL adapter、依赖注入/API 组合以及跨模块 durable outbox/对账。
- 50 万真实资产、NAS 延迟/断连、权限在枚举中途变化、磁盘/数据库空间不足和长期恢复演练。
- 同步 Windows 文件系统元数据调用在内核/NAS 卡死时的进程级隔离；生产 Worker 健康监管由后续任务交付。
