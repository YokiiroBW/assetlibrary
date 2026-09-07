# V01-026 验证记录

当前：后端已集中验证；计数27通过（协议5、manifest21、真实PG驱动1；其中嵌套真实.NET查询1项不重复计数），零跳过。首次真实Web E2E因窄屏工具栏布局跳动失败1项；V01-025已修复，待保留原双击动作再执行。其余成功项不重跑。

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
- `python -I -B scripts/verify_repository.py`：首次发现三个SDK consumer指纹stale；协调器7473fb4已包含原生成器再生，待最后文档同步后一并复验。

首轮format/build发现CA1506、CA1862及格式，集中分离边界职责并复验通过；源码门禁发现重复lookahead块后将三个实际读消费者合并使用有界分页处理，最终format/build/source均通过。Release build零警告、零错误。首次真实PG12.815s通过；共享分页处理后的最终PG12.210s通过，TRX明确total=executed=passed=1、notExecuted=failed=0，不以仅exit0计通过。

真实E2E使用原 `tests/integration/read-only-trial/run_e2e.py --execute`，固定dotnet/PG，Node来自V01-006工具目录，Web及Playwright来自V01-025不可变构建。初次证据 `.runtime/V01-026/real-web-e2e/20260907T171445Z-dd900b29` 保留runner.log/TRX/桌面及失败截图：手机首击选择引发toolbar换行，row下移，第二击失败。已报告协调器并由V01-025修复；不改测试动作绕过。后续Host安全/恢复/源不变阶段首轮未执行，不能计通过。CAS测试同时对齐真实Web显式images登记，先读回并断言，再CAS photos。

新增断言覆盖：分类幂等/CAS/default general；离线授权详情；name/modified/size双方向稳定多页，无遗漏或重复；bigint和微秒时间锚点；大小NULL桶后续页；literal `%_`过滤；reparse类型分组；第130条定位；cursor跨排序、方向、类型、name、scope/category拒绝；隐藏库/条目与不存在一致；数据库升级保存200157条事实及已完成scanID/count，首/后页索引计划与旧SQL函数兼容。
