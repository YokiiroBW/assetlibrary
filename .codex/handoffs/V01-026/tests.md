# V01-026 验证记录

当前：实现完成并做过diff/调用点只读校对，尚未运行restore、构建或测试；按协调器要求等待Web稳定后集中验证。计数为0，不是缺工具或跳过后通过。

待运行的实际入口（精确SDK/PG使用协调器已验证的共享临时工具，NuGet复用主库缓存）：

- `dotnet restore AssetLibrary.slnx --locked-mode`
- `dotnet format AssetLibrary.slnx --verify-no-changes --no-restore`
- `dotnet build AssetLibrary.slnx --configuration Release --no-restore`
- `dotnet test tests/dotnet/AssetLibrary.WebGateway.Tests/AssetLibrary.WebGateway.Tests.csproj --configuration Release --no-build --no-restore`，真实PG/Host用例由现有驱动提供环境。
- `python -B database/migrations/production/migration_tool.py validate`
- `python -B -m unittest discover -s tests/database -p test_migration_manifest.py -v`
- `python -B -m unittest discover -s tests/database -p test_migration_integration.py -k test_web_interaction_upgrade_and_dotnet_queries -v`，`ASSETLIBRARY_TEST_POSTGRES_REQUIRED=1`，独立短系统临时根；包含真实 `InteractiveReadModelIntegrationTests`。
- 受影响旧默认/权限回归可复用同驱动 `test_web_gateway_filters_permissions_pages_and_revocation_in_database`；不重复执行旧数据库全套。
- `python -I -B scripts/verify_repository.py`、`python -B scripts/validate_dotnet_source.py` 及根集成相应门禁。

新增断言覆盖：分类幂等/CAS/default general；离线授权详情；name/modified/size双方向稳定多页，无遗漏或重复；bigint和微秒时间锚点；大小NULL桶后续页；literal `%_`过滤；reparse类型分组；第130条定位；cursor跨排序、方向、类型、name、scope/category拒绝；隐藏库/条目与不存在一致；数据库升级保存200157条事实及已完成scanID/count，首/后页索引计划与旧SQL函数兼容。
