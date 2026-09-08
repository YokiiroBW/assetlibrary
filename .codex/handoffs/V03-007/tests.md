# V03-007 验证记录

2026-09-08，在本任务独立工作区运行基线 `python -I -B scripts/verify_repository.py` 成功。Python 使用 `C:/Users/Administrator/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe`，默认 PATH 没有 python；没有安装新运行时。

包含既有迁移21项、架构14项及仓库/合同/源策略检查；Alpha有效性审计成功且发布仍blocked。没有运行或声明预览功能、性能、隔离平台或NAS验收通过。

前置只读命令一度因执行环境启动失败，经本窗口批准后成功；用户恢复时已切换为允许执行的环境。这不是业务测试失败。

## 安全读取基础阶段

精确SDK `C:/Users/Administrator/AppData/Local/Temp/V01-014-tooling-and-tests/tooling/dotnet/dotnet.exe` (10.0.111)。复制已安装NuGet缓存到本工作区.runtime/nuget，Core锁定restore通过；新Preview.Tests使用既有MSTest pins生成本工程lock，无根版本修改。

- `dotnet build services/core-server/AssetLibrary.CoreServer.csproj --configuration Release --no-restore`：通过，零警告。首次Linux UTF8 P/Invoke触发CA2101，改严格UTF8零终止byte数组，没有压警告。
- `dotnet format whitespace tests/dotnet/AssetLibrary.Preview.Tests/AssetLibrary.Preview.Tests.csproj --no-restore`：完成。
- `dotnet test tests/dotnet/AssetLibrary.Preview.Tests/AssetLibrary.Preview.Tests.csproj --configuration Release --no-restore --logger "trx;LogFileName=source-boundary.trx" --results-directory .runtime/preview-tests/source`：8通过、0失败、1跳过；TRX位于本工作区.runtime/preview-tests/source/source-boundary.trx。
- `python -I -B scripts/validate_dotnet_source.py`：通过。

通过内容：Unicode字节/原hash与mtime不变、陈旧长度/mtime/容量拒绝且不写副本、取消、真实Windows目录junction/库根junction拒绝、并发写删拒绝、ADS/设备名/路径深度、句柄释放。首次两个symlink fixture因缺SeCreateSymbolicLinkPrivilege失败；目录改复用既有SandboxDirectoryLink（Windows真实junction），文件symlink单列真实缺口，未改系统权限且没有算通过。Linux安全打开/源变化分支以及Windows文件symlink仍需对应平台证据。

阶段测试Compile链接同一内部生产源而不改Core friend API；Core独立编译同样成功，HTTP/隔离实际进程集成不能由此替代。所有样例在本独立worktree内由RepositorySandbox生成的GUID路径，退出清理，没有真实NAS资产。
