# V03-010 验证

2026-09-12；Windows 当前实际用户；仓库精确 .NET 10.0.111 SDK。使用已存在 SDK 可执行路径；PATH 的 Program Files 安装未包含 SDK。没有改动系统 .NET 或安装依赖。

| 检查 | 结果 |
| --- | --- |
| `dotnet restore apps/windows-client/AssetLibrary.Windows.slnx --locked-mode` | 4 项目通过 |
| `dotnet format apps/windows-client/AssetLibrary.Windows.slnx --verify-no-changes --no-restore` | 通过 |
| `dotnet build apps/windows-client/AssetLibrary.Windows.slnx --configuration Release --no-restore` | 0 警告，0 错误 |
| `dotnet test ... --filter "TestCategory!=NativeLive"` | 54/54；`.runtime/assethost-tests/windows-affected-final.trx` |
| `dotnet test ... --filter "TestCategory=NativeLive"` | 2/2；`.runtime/assethost-tests/windows-native-live-final.trx` |
| `python -I -B scripts/verify_repository.py` | 通过；`.runtime/assethost-tests/repository-final.log` |
| `python -I -B scripts/validate_dotnet_source.py` | 451 C# 文件通过 |
| `dotnet package list --project ... --include-transitive --vulnerable --no-restore --format json --output-version 1` | `.runtime/assethost-tests/windows-vulnerabilities.json` |
| `python -I -B scripts/validate_dotnet_dependencies.py --solution apps/windows-client/AssetLibrary.Windows.slnx --packages-dir .runtime/nuget --vulnerability-report .runtime/assethost-tests/windows-vulnerabilities.json` | 4 项目/15 锁定包通过，无新增外部包 |

上表 `dotnet test ...` 展开为 `dotnet test apps/windows-client/AssetLibrary.Windows.slnx --configuration Release --no-build --no-restore`；`dotnet package list` 的 `...` 为同一 solution。NativeLive 仅设置 `ASSETLIBRARY_NATIVE_TEST_PROFILE` 为主协调私密支架路径，未打印内容；主协调持有支架生命周期。

Host 34 个不同用例：33 普通（严格 wire/endian/GUID/UTF16/bounds/100+one、过期无回放、失败退避、401/403/404、410、会话到期晚到结果、2 并发/合并/Busy、reparse、64 页面/8192 令牌、实际 Windows pipe 慢读取、4 慢客户端与恢复、畸形头恢复、实际 TokenUser DACL、第二 Host 预留拒绝、私密配置边界）；1 真实 Core（授权库、100+下一页、第二页、相册/夏日/文件、无权限账号、跨身份旧 token）。其余 21 普通和 1 NativeLive 为现有 Windows 适配器回归。重跑不累计用例数量。

首次 Host targeted：31 通过、1 失败；畸形 packet 抛 `InvalidDataException` 未被 listener 的 `IOException` 分支捕获，修复显式类别后 32/32 通过。新增 100+one 反例后最终普通套件为 54/54。首次 Host NativeLive 失败于测试假设“相册”直接含文件，实际支架为“相册/夏日/文件”；测试据真实支架修正并通过，未用虚构数据掩盖失败。构建曾要求按职责拆分过大耦合类；遵循指标拆分 projection 与测试 fixture，未抑制规则。source 策略发现重复测试 setup，提取既有协议/支架 helper 复用后通过。

真实命名管道测试只使用随机后缀，已 dispose；没有声明跨用户实登或另一会话真实客户端测试通过（DACL/会话保护的实证为本用户原生句柄安全描述符和代码路径）。没有启动默认 Host、操作 Explorer、修改注册表或改变支架资产。C++/Explorer 统一验收、源哈希、崩溃恢复和八小时稳定性仍交主协调。

实现提交：`0be1438e66534086028147a5e0122e1a876e5181`。
