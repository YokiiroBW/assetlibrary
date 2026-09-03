# V01-006 测试记录

## 执行环境

- Windows x64 worktree：`C:\YOKI\Codex\worktrees\V01-006`；实现 commit `7776deccacd283d8618731fbb6cd6c6602496eeb`。
- .NET SDK：已校验任务本地 `10.0.111`；CLI home 与 NuGet cache 均位于 V01-006 `.runtime`。
- Python：Codex 随附 `3.12.13`；最终 Python 门禁使用 `-B` / `PYTHONDONTWRITEBYTECODE=1`。
- PostgreSQL：复用已校验 EDB no-install `16.15` 工具链；自托管 required 集群和备份只在 V01-006 `.runtime/sandbox-storage`，仅监听 `127.0.0.1` 随机端口。
- Web：任务本地 Node.js `24.20.0`、pnpm `11.19.0`、Playwright Chromium；构建和浏览器输出只在 `.runtime` / ignored build 目录。
- Kotlin：任务本地 Temurin `21.0.12+8`，archive SHA-256 `9ba963ee2371874a74185d18bc7bb2ab9407df7683300855ed7606e0662321d0`；Gradle dependency verification 为 strict。
- 没有系统安装、服务注册、注册表写入、永久 PATH/JAVA_HOME 修改或真实资产/NAS 访问。

## 执行命令

```text
dotnet restore AssetLibrary.slnx --locked-mode
dotnet format AssetLibrary.slnx --verify-no-changes --no-restore
dotnet build AssetLibrary.slnx --configuration Release --no-restore
dotnet test AssetLibrary.slnx --configuration Release --no-build --no-restore
dotnet package list --project AssetLibrary.slnx --include-transitive --vulnerable --no-restore --format json --output-version 1
python -B scripts/validate_dotnet_dependencies.py --solution AssetLibrary.slnx --packages-dir .runtime/nuget --vulnerability-report .runtime/dotnet-vulnerabilities.json
python -B scripts/validate_dotnet_source.py

ASSETLIBRARY_TEST_POSTGRES_BIN=C:\YOKI\Codex\worktrees\V01-004\.runtime\sandbox-storage\V01-004\postgresql-16.15\pgsql\bin
ASSETLIBRARY_TEST_POSTGRES_REQUIRED=1
ASSETLIBRARY_TEST_RUNTIME=C:\YOKI\Codex\worktrees\V01-006\.runtime\sandbox-storage\V01-006
python -B -W error -m unittest discover -s tests/database -p test_*.py -v

pnpm --dir apps/web install --frozen-lockfile
pnpm --dir apps/web run format:check
pnpm --dir apps/web run lint
pnpm --dir apps/web run typecheck
pnpm --dir apps/web run build
pnpm --dir apps/web exec playwright install chromium
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
git diff --check
```

## 架构与契约测试

- Gateway Web MSTest 13 个：输入值对象、游标篡改/跨过滤器复用、授权结果边界、超时/调用方取消、匿名/畸形/写操作拒绝、64 KiB、UTF-8、媒体类型、稳定外部错误和相对事实序列化。
- Database 53 个：manifest/tool 21、ReadCore 4、TaskHealth 9、Web Gateway static 5、真实 PostgreSQL integration 14；required 模式不允许跳过数据库测试。
- Repository 37、architecture 14、SDK Python 14、AssetLink contract/spike 21；冻结生成源、锁文件、依赖/许可证、模块边界和 release gate 全覆盖。
- Chromium 6 个：桌面分页/窗口化/目录导航、390px 重排与空目录、服务错误与登录失效恢复、跨未载入资源库导航、无效响应失败关闭、陈旧搜索响应隔离。

## 通过

- 唯一自动测试计数 248/248，0 failed，0 skipped：.NET 103、database 53、repository 37、architecture 14、SDK Python 14、AssetLink 21、Chromium 6。
- TypeScript 生成 SDK 另为 5/5；Kotlin `sdkTest` 与 build 成功。它们验证同一冻结生成合同，未重复计入 248 的既有 handoff 统计口径。
- WebGateway 13/13；全 .NET solution 103/103；Release build 0 warning / 0 error；format 通过。
- `.NET dependency policy passed (8 projects, 18 locked packages)`；NuGet 与 npm audit 无已知阻断漏洞，许可证/source/integrity 均通过。
- `MIGRATION_MANIFEST_OK migrations=9 modules=11 postgresql=16.15`；真实 PostgreSQL 53/53，integration 14/14。
- Web production build：Vite 报告 JavaScript 230.68 kB（gzip 72.20 kB）、CSS 8.92 kB（gzip 2.76 kB），低于策略预算。
- repository verifier、architecture baseline、SDK generation、`v0.1-start` 和最终 diff check 均通过。

## 失败 / 跳过

- 最终必需链路：0 failed，0 skipped。
- 开发中 NuGet 策略最初不认识中央包管理合法的 `CentralTransitive` 锁类型；门禁已补充中央版本/SHA-512/许可证校验及正反向测试，最终通过。
- 一次 Python 定向测试产生 `__pycache__`，零残留门禁按设计失败；缓存经确认路径后移到系统临时隔离目录，最终 worktree 内 `__pycache__` 为 0。
- Kotlin 首次预检因当前会话没有 Java 而未启动；改用 task-local、SHA-256 校验的 Temurin 21 后完整 build 通过，未修改系统环境。
- 显式只读事务初版触发重复代码/耦合度门禁；抽取有界 `PostgresReadExecutor` 后 source policy 与 Roslyn analyzer 通过。

## 故障注入与恢复验证

- 匿名、重复/缺失主体、畸形 JSON/字段类型、错误媒体类型、无效 UTF-8、超 64 KiB body、写操作/未知操作和读请求携带写身份均在查询前失败关闭。
- 数据库普通用户只见显式授权库，管理员见全部，禁用/未知主体为空；无权限库和不存在库得到同一外部结果。撤权后列表、浏览、搜索均不再返回该库。
- runtime 直接读取主体表、LibraryStorage/AssetIdentity 投影或写主体表均被 PostgreSQL 拒绝；只能执行 GatewayAuth 最终函数。
- 游标篡改、跨操作/库/目录/搜索词复用、孤立 cursor 字段和越界 limit 被拒绝；端口返回越界页/跨目录行时应用层失败关闭。
- 服务超时映射为稳定 504，调用方取消保持 cancellation；HTTP 错误不泄露底层异常、主体、搜索词或路径。
- Web 会 abort 旧搜索并拒绝陈旧 generation；request ID 不匹配和 101 项响应均进入错误状态；401 后重试会同时刷新列表、目录和搜索。
- 自托管 PostgreSQL 重启后主体、权限和可搜索物理事实仍可通过最小权限入口读取。

## 性能数据

- 真实 PostgreSQL 插入 100,000 个同目录 present 条目；browse plan 命中 `filesystem_entry_browse_index`，搜索 plan 命中 `filesystem_entry_path_search_index`。
- 所有数据库入口 limit 为 1–101，应用外部页为 1–100；使用 keyset tuple 比较，无 offset、`COUNT(*)` 或无界集合。
- Web 100 项数据页的可见 DOM 少于 40 个条目；换库/换目录/换搜索词清空旧页并取消在途请求。
- Vite 报告生产构建 JS+CSS 为 239.60 kB，低于 286,720-byte 联合预算。

## 尚未覆盖

- 可执行 CoreServer host、真实 OIDC/宿主认证、连接字符串/DI composition、共享 Data Protection key ring 和生产部署。
- 权限写 API/管理 UI、审计写链路、上传/移动/改名/删除、WebDAV、Developer API、MCP、Provider 与 Explorer。
- 标签、评分、OCR、AI、正文/高级模糊搜索和 SearchDedup 持久投影。
- 50 万真实 NAS/P95、迁移 8 在大型既有表上的锁/磁盘压力、数据库空间不足、多实例切换和正式发布/恢复演练。
