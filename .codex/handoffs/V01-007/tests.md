# V01-007 测试记录

## 执行环境

- Windows x64 worktree：`C:\YOKI\Codex\worktrees\V01-007`；实现 commit `7fb7de2d65ea9b87117ba6430fc7df496f5ed941`。
- .NET SDK：任务本地已验证 `10.0.111`；CLI home 与 NuGet cache 均位于 V01-007 `.runtime`。
- Python：Codex 随附 `3.12.13`；最终 Python 门禁使用 `-B`，不在源码树生成缓存。
- PostgreSQL：复用已校验 EDB no-install `16.15` 工具链；required 集群和备份仅位于任务 `.runtime/sandbox-storage`，只监听 loopback。
- Web：精确 Node.js `24.20.0`、pnpm `11.19.0`、Playwright Chromium；Kotlin 使用 task-local Temurin `21.0.12+8`。
- 所有物理文件场景仅使用 `C:\YOKI\Codex\worktrees\V01-007\.runtime\sandbox-storage\V01-007\fixture-*`；没有系统安装、注册表/服务修改或真实资产/NAS 访问。

## 执行命令

```text
dotnet restore AssetLibrary.slnx --locked-mode
dotnet format AssetLibrary.slnx --verify-no-changes --no-restore
dotnet build AssetLibrary.slnx --configuration Release --no-restore
dotnet test tests/dotnet/AssetLibrary.TransferOperation.Tests/AssetLibrary.TransferOperation.Tests.csproj --configuration Release --no-build --no-restore
dotnet test AssetLibrary.slnx --configuration Release --no-build --no-restore
dotnet package list --project AssetLibrary.slnx --include-transitive --vulnerable --no-restore --format json --output-version 1
python -B scripts/validate_dotnet_dependencies.py --solution AssetLibrary.slnx --packages-dir .runtime/nuget --vulnerability-report .runtime/dotnet-vulnerabilities.json
python -B scripts/validate_dotnet_source.py

ASSETLIBRARY_TEST_POSTGRES_BIN=C:\YOKI\Codex\worktrees\V01-004\.runtime\sandbox-storage\V01-004\postgresql-16.15\pgsql\bin
ASSETLIBRARY_TEST_POSTGRES_REQUIRED=1
ASSETLIBRARY_TEST_RUNTIME=C:\YOKI\Codex\worktrees\V01-007\.runtime\sandbox-storage\V01-007
python -B -W error -m unittest discover -s tests/database -p test_*.py -v

pnpm --dir apps/web install --frozen-lockfile
pnpm --dir apps/web run format:check
pnpm --dir apps/web run lint
pnpm --dir apps/web run typecheck
pnpm --dir apps/web run build
pnpm --dir apps/web run test:browser
pnpm --dir apps/web audit --audit-level low
python -B scripts/validate_web_dependencies.py --require-build-artifacts
python -B scripts/validate_web_source.py

pnpm --dir packages/sdk/assetlink/typescript install --frozen-lockfile
pnpm --dir packages/sdk/assetlink/typescript run lint
pnpm --dir packages/sdk/assetlink/typescript run typecheck
pnpm --dir packages/sdk/assetlink/typescript run test
pnpm --dir packages/sdk/assetlink/typescript audit --audit-level low
packages/sdk/assetlink/kotlin/gradlew.bat -p packages/sdk/assetlink/kotlin --no-daemon --dependency-verification strict build
python -B scripts/validate_assetlink_sdk_dependencies.py --require-build-artifacts
python -B scripts/generate_assetlink_sdks.py --check
python -B -m unittest discover -s tests/sdk -p test_*.py -v
python -B -m unittest discover -s tests/spikes/assetlink -p test_*.py -v

python -B scripts/verify_repository.py
python -B -m unittest discover -s tests/repository -p test_*.py -v
python -B -m unittest discover -s tests/architecture -p test_*.py -v
python -B tests/architecture/check_release_gates.py --target v0.1-start
python -B tests/architecture/check_release_gates.py --target production-file-writes
python -B tests/architecture/check_release_gates.py --target v0.1-release
git diff --check
```

## 架构与契约测试

- V01-007 MSTest 64 个：值对象/default 防御、幂等、不可倒退状态、确认绑定、计划上限、保护/权限/容量/冲突、执行权 fencing、超时/取消和不可信端口/store 返回值。
- 物理沙箱覆盖 copy/move/rename/trash/restore、目标重开完整 SHA-256、no-replace、路径/reparse 边界、同目标并发、故障注入与新实例恢复。
- Repository 42 个与 architecture 14 个覆盖模块依赖方向、生产 composition 负向门禁、canonical 合同、任务/handoff、语言/依赖预算和 release gate。
- Database 53 个含真实 PostgreSQL 16.15 integration 14 个；Web Chromium 6 个；SDK Python 14 个、AssetLink contract/spike 21 个。

## 通过

- 唯一自动测试计数 317/317，0 failed，0 skipped：.NET 167、database 53、repository 42、architecture 14、SDK Python 14、AssetLink 21、Chromium 6。
- V01-007 定向 64/64；完整 .NET solution 167/167；Release build 0 warning / 0 error；format 通过。
- `.NET dependency policy passed (9 projects, 18 locked packages)`；136 个 C# 文件通过 source/复杂度/重复/敏感日志政策。
- TypeScript 生成 SDK 5/5、audit clean；Kotlin/JVM 4 个 Gradle task build 成功。为保持既有口径，不重复计入 317。
- Web frozen install、format/lint/type/build、Chromium 6/6、audit、许可证/完整性/源码和体积门禁通过；Vite JS 230.68 kB（gzip 72.20 kB）、CSS 8.92 kB（gzip 2.76 kB）。
- `v0.1-start` 正常放行；`production-file-writes` 以退出码 3 按 M0-006-G2 阻断，`v0.1-release` 以退出码 3 按 M0-004-G2、M0-006-G1/G2 阻断，均为预期安全结果。
- 最终 repository verifier、SDK generation、`git diff --check`、沙箱残留和任务进程检查通过。

## 失败 / 跳过

- 最终必需链路：0 failed，0 skipped。
- 开发期 Roslyn CA1506 两次识别出测试适配器耦合过高；按完整职责拆分后 analyzer 与全套测试通过，没有压制诊断。
- 一次 `dotnet package list` 使用了当前 SDK 不支持的输出文件参数；改用受支持的标准输出报告并由依赖验证器读取，最终漏洞/许可证/锁文件检查通过。

## 故障注入与恢复验证

- stage 写后、target 提交后、源入垃圾桶前与源入垃圾桶后均注入中断；每个场景由新 adapter/service 实例恢复两次，第二次保持同一收敛结果。
- 恢复先读取 source/stage/target/trash 的长度与 SHA-256，再参考相对 journal；目标已验证则完成或继续安全 finalization，证据冲突则返回人工冲突。
- target 并发出现、两个执行者竞争同一目标和过期 fencing token 均不能覆盖；loser 返回稳定冲突且源保留。
- 源长度/哈希变化、目标哈希错误、空间不足、错误确认、跨计划确认、调用方取消和绝对截止时间耗尽均失败关闭。
- 路径逃逸、根本身、UNC/设备/其他卷、marker 缺失及 reparse point 被拒绝；失败后无正式目标或源丢失。
- 端口/store 伪造错误 ID、越界/倒退进度、错误物理事实、undefined failure/default 值和自发 cancellation 均被应用层拒绝。

## 性能数据

- 单计划上限 128 项；传输缓冲配置只接受 4 KiB–1 MiB，测试包含 2 MiB+17 bytes 确定性多块 fixture，并观察单块上界。
- 文件复制、哈希和目标复核均流式执行，没有整体读入内存；执行使用绝对截止时间，preflight/store/port 耗时都计入同一预算。
- 批次逐项返回，失败项不会回滚或污染已完成项；没有无界列表、无界重试或无界恢复循环。

## 尚未覆盖

- 生产 durable 文件/NAS/SMB 适配器、数据库 operation/session/journal 持久化、跨进程或多节点锁与 fencing、服务/API/Worker composition。
- 真实 ACL/保护/审计入口、跨卷语义、非协作外部写入、进程崩溃/操作系统断电、磁盘临界空间和发布回滚。
- 1/20/100 GiB、50 万资产、真实 NAS/P95、长时间取消/重启和正式灾难恢复演练；这些属于 M0-006-G1/G2/G3 的后续独立证据。
