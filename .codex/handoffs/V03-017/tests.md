# V03-017 验证记录

环境：Windows x64；global.json 的 .NET SDK 10.0.111；Python 标准库。所有安装/删除测试只使用系统临时目录；未运行真实 HKCU 注册、GUI、Host/Settings、Explorer 或 NAS 操作。

| 检查 | 结果 |
| --- | --- |
| python -I -B scripts/verify_repository.py | 通过；architecture/source/SDK/contracts；Alpha正确保持blocked |
| dotnet restore tests/windows-setup/AssetLibrary.Windows.Setup.slnx --locked-mode | 通过 |
| dotnet format tests/windows-setup/AssetLibrary.Windows.Setup.slnx --verify-no-changes --no-restore | 通过 |
| dotnet build tests/windows-setup/AssetLibrary.Windows.Setup.Tests.csproj --configuration Release --no-restore | 通过；0警告/0错误 |
| dotnet test tests/windows-setup/AssetLibrary.Windows.Setup.Tests.csproj --configuration Release --no-build --no-restore | 23通过，0失败，0跳过 |
| python -I -B -m unittest discover -s tests/windows-setup -p test_package.py -v | 4通过 |
| Setup RID locked restore + Release self-contained publish | 通过；精确10.0.11运行时/ILLink锁 |
| python -I -B tests/windows-setup/test_cli.py --setup .runtime/setup-publish-final/AssetLibrary.Setup.exe --report .runtime/setup-cli-results.json | 实际EXE 8步通过；见cli-results.json |
| dotnet package list --project tests/windows-setup/AssetLibrary.Windows.Setup.slnx --include-transitive --vulnerable --no-restore --format json --output-version 1 | 通过；两个项目未报告漏洞 |
| python -I -B scripts/validate_dotnet_dependencies.py --solution tests/windows-setup/AssetLibrary.Windows.Setup.slnx --packages-dir <approved-NuGet-cache> --vulnerability-report .runtime/setup-vulnerabilities.json | 通过；2项目、15 locked packages，使用root fe8b4f6策略 |
| git diff --check / 最终diff审查 | 通过；修改只在任务拥有范围，root共享提交仅重放 |

普通安装测试加 --logger "trx;LogFileName=setup-tests.trx" --results-directory .runtime/setup-test-results 生成真实TRX。Self-contained restore/publish共同参数：-p:RuntimeIdentifier=win-x64 -p:SelfContained=true -p:RuntimeFrameworkVersion=10.0.11 -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:AssetLibraryReleaseLockRoot=<repo>/infra/windows-client/locks。restore用 --locked-mode，publish用 --configuration Release --runtime win-x64 --self-contained true --no-restore。必须显式传 RuntimeIdentifier 单数，单用 restore --runtime 不会触发根级 RID 锁位置条件。

23个.NET案例包含：初装/重装/升级/卸载/重试、源包与配置保留、损坏组件、额外文件、8个非法路径边界、大小写重复、同版本哈希冲突、foreign目录与注册owner、空间不足/取消、staging中断、注册失败回滚、崩溃日志恢复、Host退出失败、真实DLL文件锁延期及重试、未知文件拒删、NTFS junction拒绝和sandbox路径限制。实际EXE用合成payload，仅验证安装器，不冒充真实应用包。

首次符号链接测试因当前普通用户无 symlink 特权失败，随后改为无需提权的真实NTFS junction，未改系统策略，最终全数通过。独立csproj格式命令不会格式化ProjectReference源，最终改以本任务solution检查并纠正三个新文件CRLF；功能测试未因换行修正重复执行。未关闭任何分析器或把缺证据记为通过。

尚未执行：三个最终生产组件统一打包、系统TaskDialog、真实HKCU、原生Explorer用户闭环、生产DLL加载态卸载、登录重启启动恢复。均由主任务单一实机入口执行。没有执行G4，依用户豁免明确未测；未运行不相关的全仓库.NET/Android/前端套件。
