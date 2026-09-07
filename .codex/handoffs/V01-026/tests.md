# V01-026 验证记录

最终：独立回归28项（协议5、manifest21、真实PG驱动1、真实Web E2E 1）及仓库架构回归14项，共42项通过，失败0、跳过0。PG驱动内嵌套.NET查询及verify_repository重复包含的同一manifest用例不重复计数。历史失败与修复过程保留在下文；成功的PG/协议检查未因Web或纯注释/样式组织变化重复执行。

已执行的实际入口（SDK10.0.111 / PG16.15使用协调器已验证的共享临时工具，NuGet复用主库缓存）：

- `dotnet restore AssetLibrary.slnx --locked-mode`
- `dotnet format AssetLibrary.slnx --verify-no-changes --no-restore`
- `dotnet build AssetLibrary.slnx --configuration Release --no-restore`
- `dotnet test tests/dotnet/AssetLibrary.WebGateway.Tests/AssetLibrary.WebGateway.Tests.csproj --configuration Release --no-build --no-restore --filter 'FullyQualifiedName~InteractiveReadProtocol|FullyQualifiedName~GatewayContractTests'`：5/5通过，零跳过。
- `python -B database/migrations/production/migration_tool.py validate`
- `python -B -m unittest discover -s tests/database -p test_migration_manifest.py -v`
- `python -B -m unittest discover -s tests/database -p test_migration_integration.py -k test_web_interaction_upgrade_and_dotnet_queries -v`，`ASSETLIBRARY_TEST_POSTGRES_REQUIRED=1`，独立短系统临时根；包含真实 `InteractiveReadModelIntegrationTests`。
- 旧list/browse/search SQL和schema权限负向检查已纳入上述单一PG驱动，不另跑旧数据库69项。
- `python -B scripts/validate_dotnet_source.py`：353个C#文件通过。
- `python -I -B scripts/verify_repository.py`：最终全部通过，日志 `.runtime/validation/repository-accepted.log`。包含交接/M0-009架构、Alpha有效性审计（发布保持blocked）、.NET源码、迁移、SDK生成/依赖、Web依赖/源码及14项架构回归。此前SDK consumer指纹由协调器7473fb4用原生成器同步，超长styles.css由V01-025按职责拆分并由2d2bef2合入，未放宽门禁。

首轮format/build发现CA1506、CA1862及格式，集中分离边界职责并复验通过；源码门禁发现重复lookahead块后将三个实际读消费者合并使用有界分页处理，最终format/build/source均通过。Release build零警告、零错误。首次真实PG12.815s通过；共享分页处理后的最终PG12.210s通过，TRX明确total=executed=passed=1、notExecuted=failed=0，不以仅exit0计通过。

真实E2E使用原 `tests/integration/read-only-trial/run_e2e.py --execute`，固定dotnet/PG，Node来自V01-006工具目录，Web及Playwright来自V01-025不可变构建。初次证据 `.runtime/V01-026/real-web-e2e/20260907T171445Z-dd900b29` 保留runner.log/TRX/桌面及失败截图：手机首击选择引发toolbar换行，row下移，第二击失败。已报告协调器并由V01-025修复；不改测试动作绕过。后续Host安全/恢复/源不变阶段首轮未执行，不能计通过。CAS测试同时对齐真实Web显式images登记，先读回并断言，再CAS photos。

第二次 `20260907T172906Z-b26215c0` 的initial/resumed浏览器JSON均passed；随后测试helper重复使用已有parent的JsonObject失败，尚未到达剩余恢复/源不变阶段。修正为envelope使用body.DeepClone，产品代码未变，测试项目增量build通过。

最终E2E `20260907T174230Z-0d158ac9` 在干净整合版本2d2bef2、V01-025修复后的不可变dist执行，原mobile dblclick动作保留。TRX 1/1、21.0624秒，`acceptance.json` 为passed/skipped=0/resource_cleanup=verified；17个已有综合场景及新增分类断言全部走完。桌面/手机深色两阶段截图、浏览器版本、runner.log、run.json、TRX和验收JSON保留。最后源哈希和mtime未变，自有PG/登录角色、私密目录、TLS容器等均由原夹具回收。没有连接或修改NAS真实资产。

实际E2E命令：

```powershell
python -I -B tests/integration/read-only-trial/run_e2e.py --execute --dotnet C:/Users/Administrator/AppData/Local/Temp/V01-014-tooling-and-tests/tooling/dotnet/dotnet.exe --postgres-bin C:/Users/Administrator/AppData/Local/Temp/V01-014-tooling-and-tests/tooling/postgresql/pgsql/bin --node C:/YOKI/Codex/worktrees/V01-006/.runtime/toolchains/node-v24.20.0-win-x64/node.exe --web-root C:/YOKI/Codex/AssetLibrary-worktrees/V01-025/apps/web/dist --playwright-module C:/YOKI/Codex/AssetLibrary-worktrees/V01-025/apps/web/node_modules/@playwright/test/index.mjs --evidence .runtime/V01-026/real-web-e2e
```

运行的Python为 `C:/Users/Administrator/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe`。独立PG驱动设置 `ASSETLIBRARY_TEST_POSTGRES_REQUIRED=1`、上述PG bin、上述dotnet和短系统临时目录`%TEMP%/V026-pg-validation`，防止Windows路径上限；CLI_HOME在本worktree.runtime，NUGET_PACKAGES复用主仓库.runtime/nuget，证书自动生成关闭。

新增断言覆盖：分类幂等/CAS/default general；离线授权详情；name/modified/size双方向稳定多页，无遗漏或重复；bigint和微秒时间锚点；大小NULL桶后续页；literal `%_`过滤；reparse类型分组；第130条定位；cursor跨排序、方向、类型、name、scope/category拒绝；隐藏库/条目与不存在一致；数据库升级保存200157条事实及已完成scanID/count，首/后页索引计划与旧SQL函数兼容。
