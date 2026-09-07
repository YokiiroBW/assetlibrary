# ALIGN-001 测试记录

## 执行环境

审查自 2026-09-05 开始，最终集成验证于 2026-09-07 在 Windows x64 执行。协调目录 `C:\YOKI\Codex\AssetLibrary-worktrees\ALIGN-001`，所有写入验证限临时目录/隔离沙箱，无生产数据库或资产访问。

- Python：`C:\Users\Administrator\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe`，下文 `python` 指此解释器。
- .NET SDK 10.0.111：`C:\Users\Administrator\AppData\Local\Temp\V01-014-tooling-and-tests\tooling\dotnet`；进程内前置 DOTNET_ROOT/PATH、禁用首次证书/遥测。依赖缓存复用主仓库 `.runtime/nuget`，未改永久系统配置。
- PostgreSQL 16.15：同一临时工具目录的 `postgresql/pgsql/bin`；设置 `ASSETLIBRARY_TEST_POSTGRES_REQUIRED=1` 和显式 dotnet/Host DLL。最终 `ASSETLIBRARY_TEST_RUNTIME` 为系统临时目录下 `ALIGN-001-postgres`，cluster 自动启动/回收。
- Web/TypeScript 使用 ALIGN-003 的精确 Node 24.20.0、pnpm 11.19.0、Chromium；Kotlin 使用既有 JDK 21.0.12+8 和 Gradle 9.3.1 wrapper 严格依赖校验。
- 完整输出在协调目录 ignored `.runtime/ALIGN-001/` 及各子任务交接列出的日志目录。受控交接保存命令、计数、失败解释及平台边界。

## 执行命令

源码稳定后先 review 完整 diff，再执行既有 CI 命令。已通过且输入未变的检查不重复；独立 worktree 结果按已合入的相同源码提交复用。

```text
python -I -B scripts/verify_repository.py
python -I -B -m unittest discover -s tests/repository -p test_*.py -v
python -B -m unittest discover -s tests/database -p test_*.py -v
python -B -m unittest discover -s tests/sdk -p test_*.py -v
python -B -m unittest discover -s tests/spikes/windows-shell -p test_*.py -v
python -I -B -m unittest discover -s tests/release -p test_*.py -v
dotnet restore AssetLibrary.slnx --locked-mode
dotnet format AssetLibrary.slnx --verify-no-changes --no-restore
dotnet build AssetLibrary.slnx --configuration Release --no-restore --disable-build-servers
dotnet test AssetLibrary.slnx --configuration Release --no-build --no-restore --logger "console;verbosity=normal"
dotnet package list --project AssetLibrary.slnx --include-transitive --vulnerable --no-restore --format json --output-version 1
python scripts/validate_dotnet_dependencies.py --solution AssetLibrary.slnx --packages-dir C:/YOKI/Codex/AssetLibrary/.runtime/nuget --vulnerability-report .runtime/dotnet-vulnerabilities.json
packages/sdk/assetlink/kotlin/gradlew.bat -p packages/sdk/assetlink/kotlin --no-daemon --dependency-verification strict build
python -B scripts/validate_assetlink_sdk_dependencies.py --require-build-artifacts
python -B scripts/validate_web_dependencies.py --require-build-artifacts
python -B tests/architecture/check_release_gates.py --target v0.1-start
python -B tests/architecture/check_release_gates.py --target v0.1-release
python -B tests/architecture/check_release_gates.py --target production-file-writes
python -I -B scripts/validate_v0_1_alpha.py
python -I -B scripts/validate_v0_1_alpha.py --require-ready
git diff --check
```

Web/TypeScript/Playwright 的完整命令见 `../ALIGN-003/tests.md`；M0-004 RID、来源固定和三次冷发布见 `../ALIGN-004/tests.md`。协调目录复制了已验证且源码/清单一致的 SDK/Web dist 供统一大小门禁读取，未冒称在协调目录重新构建浏览器。

## 通过

| 最终套件 | 通过 | 去重说明 |
|---|---:|---|
| .NET solution | 265 | Build 2、AssetLink 13、ReadCore 51、TaskHealth 32、WebGateway 73、TransferOperation 64、Packaging 30 |
| 数据库 | 67 | 39 静态/合同 + 28 真实 PostgreSQL；最终摘要运行 70.094 秒 |
| 架构规则 | 14 | 最终仓库入口包含；不叠加子任务相同测试 |
| 仓库工作流 | 54 | 包含 ALIGN-003 的六个 Web foundation 测试 |
| SDK 生成/政策 | 14 | 三语言生成漂移、边界与依赖政策 |
| AssetLink Spike 合同 | 21 | 复用 ALIGN-002 相同未改输入上的成功结果 |
| 发行/Alpha 判定 | 32 | 隔离模式、原生 Host 进程及判定负向/变异测试 |
| Windows Shell 合同 | 17 | build/soak 保护和既有 COM/注册静态合同 |
| Web Chromium | 22 | ALIGN-003 完整浏览器套件 |
| TypeScript SDK native | 5 | ALIGN-003 生成 SDK 编译与 Node 测试 |
| M0-004 服务/发行 Spike | 20 | ALIGN-004：9 个合同/mock/沙箱 + 11 个 native/provenance |
| **总计** | **531** | **最终 0 failed、0 skipped，不重复累计执行次数/CLI 检查** |

.NET restore/format/Release build 通过，0 warning/0 error。最终迁移摘要变化后只重建/验证受影响 Packaging 和真实数据库套件，另六个 .NET 项目保留相同输入的成功记录。NuGet 覆盖 11 个项目、46 个锁定包，本轮查询未发现已知漏洞或许可证违规；Node 审计与依赖完整性/产物预算通过。

Kotlin `build` 和 `sdkTest` 自检成功，日志 `.runtime/gradle-home/daemon/9.3.1/daemon-17752.out.log` 记录 `Kotlin AssetLink generated SDK verification passed`、`BUILD SUCCESSFUL in 1m 50s`、四个任务执行。它不是 JUnit suite，不另虚增用例数。

## 失败 / 跳过

- 扫描旧实现受控复现为 6 failed/1 passed，修复后 ReadCore 51 全过；Web 四条畸形/挂起错误回归在修复前真实失败，最终完整 22 全过。
- 初次 .NET 全套 263 通过/2 失败，均因测试写死账本版本 12；补齐唯一冻结版本断言和动态 extra/schemaVersion 后 Packaging 30 全过，未放宽生产就绪判断。
- 首次完整数据库运行 62 通过/1 失败/4 错误：一处旧版本预期和 Windows pg_dump 备份路径过长。使用允许的较短临时路径、补齐预期后 67 全过；SQL 去掉尾空行/同步摘要后再次完整通过。最终迁移 SHA-256：`2dcd97b305cc00e6e3b43d8f44c8a267ab0a6f0ee4eb4953db674cc42dd154f9`。
- 中断后一次环境恢复失效，required PostgreSQL 主动报错；显式恢复环境后重跑，未变成 skipped。发行测试首次缺 `-I`、随后未前置固定 SDK，均在 bootstrap 阶段失败；按 workflow 隔离模式/固定 SDK 后 32 全过。
- M0-004 原显式 PackageReference 版本与中央依赖冲突、旧 Program 不满足新根分析器，在 ALIGN-004 修正后保持相同 pin、分析器强度和来源合同。

## 故障注入与恢复验证

真实 PostgreSQL 先在 12 条迁移数据库确认完整路径查不到，再经验证备份升级到 13；同一索引数据正确匹配，函数 OID/owner/ACL 相同。撤权后路径查询为零，标点-only、大小写、完整文件名/路径、隐藏库均有断言。原扫描、取消、恢复、并发和权限负向用例未删除或放宽。

## 性能数据

100,000 条真实 PostgreSQL 索引计划回归通过；Web 100 条页 DOM 少于 40 行，JS+CSS 240445 bytes，在 286720-byte 预算内。扫描复验成本约 O((条目+目录)×祖先深度)，未以缓存复验结果削弱边界。这些不是 50 万真实 NAS 或 1–100 GiB 容量证据。

## 尚未覆盖

`v0.1-start` 返回 0；`v0.1-release` 和 `production-file-writes` 返回 3（预期阻断）。Alpha audit 返回 0 且 decision=blocked；`--require-ready` 返回 3。gate JSON、Alpha policy、V01-008 partial 状态未修改。

未执行真实 SCM 安装/卸载、Shell 注册、Explorer 20 次恢复/8 小时 soak、Linux systemd、真实 Docker daemon、Provider 硬隔离或真实 NAS/大文件容量实验。本轮 Shell 使用 17 个合同检查，无新增 MSVC 原生运行证据。M0-004 Linux RID 是交叉发布，不等于 Linux 执行。

## Git 与磁盘状态

本地和 NAS main 仅快进同步，核对同一 HEAD/tree、原双方提交均为祖先。36 个工作区逐一检查 `status --porcelain=v1 --untracked-files=all` 与 HEAD；历史分支保留，未合并条目为零。精确同步读回追加于本节；最终 metadata commit 自身由 Git 历史确认，避免自引用。

NAS 旧 Linux worktree 在 Windows 显示 prunable，但目录存在且 Git clean；用原管理目录只读核验，未改指针、未 prune/repair。ignored 缓存/产物不算 Git 脏文件。

ALIGN-002 九个无文件目录的清理被自动批准审查两次拒绝，仅返回 `blocked by policy`；命令未执行且未绕过。准确绝对路径见 `../ALIGN-002/tests.md`。Git clean 不表示磁盘完全无残留。

### 已执行同步读回（2026-09-07）

- 本地 main、NAS main 和协调分支首次验收一致：HEAD `663f3f101294baedb7ae2f8a473dbe66aa73a858`；tree `7450cb93f71fea8bd0458ce59e7e21226d013aa2`。仅使用快进合并。
- 36/36 工作区 Git clean，HEAD 与原 worktree 清单一致；未合并内容为零，旧两端提交的祖先检查均为 true。没有删除、重置历史任务。
- NAS 实际文件再次执行迁移清单校验：13 migrations / 11 modules / PostgreSQL 16.15，通过；任务数据库/Spike/Host 测试进程均已退出。
- 上述结果写入本次纯状态/交接提交后再快进同步两端；最终 tip 可从 Git 读取，不将本文件的自引用 hash 写入自身。源码清单对应的 334 个文件未改变，不重复未变源码的已通过测试。
