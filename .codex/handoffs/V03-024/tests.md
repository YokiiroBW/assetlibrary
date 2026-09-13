# V03-024 验证记录

2026-09-13，Windows x64，SDK10.0.111。精确SDK可执行：`C:/YOKI/Codex/worktrees/V01-004/.runtime/sandbox-storage/V01-004/dotnet/dotnet.exe`；Python为Codex依赖运行时，NuGet复用经授权的V03-015/.runtime/nuget。

| 检查 | 最终结果 |
|---|---|
| Windows solution restore --locked-mode | 通过；锁未变 |
| Windows solution format --verify-no-changes --no-restore | 通过；WinUI工作区加载提示不等于编译warning |
| Windows solution Release build | 通过，0 warning/0 error，包含Settings |
| TestCategory!=NativeLive | 120/120通过，0失败/0跳过 |
| Thumbnail或Preview定向非Live | 43/43通过，包含上表120，不重复加总 |
| 生产Host WinExe配置 build | 通过，AssetLibrary.Host.exe，0 warning/0 error |
| validate_dotnet_source.py | 通过，532个C#文件，无重复块豁免 |
| verify_repository.py | 通过；架构/契约/源码/迁移/平台规则；Alpha发布仍blocked |
| vulnerability list + validate_dotnet_dependencies.py | 通过，6项目/31锁定包，无新增依赖 |
| final diff --check /调用链审查 | 通过，任务归属及冻结协议一致 |

新增19个非Live测试覆盖独立ALP1向量（20向量由root生成）、最大Raw及PNG、预乘alpha、cross-profile拒绝、实际WIC Job、401/403撤销与404占位、失败epoch语义、过期解码丢弃、混合HTTP导航保留、混合4任务/4客户配额、真实后缀pipe断开取消与首帧超时、MIME/长度/压缩头/取消回收。原101个离线用例继续通过，旧ALG1的13向量与Job退出/失败测试保留。

预算实测证据见 `decode-budget.json`。最终120用例报告 `.runtime/preview-validation/windows-module.trx`：最大测试整个耗时1.2077539秒；父PNG验证加helper运行573ms，输入12,582,912字节，输出10,240,000字节，1600×1600。每次启动新进程，未复用helper；没有测量系统冷缓存或Job峰值，不能把128MiB硬限额写成实测占用。

实际命令（在本worktree；dotnet/python指上面的精确工具）：

```powershell
dotnet restore apps/windows-client/AssetLibrary.Windows.slnx --locked-mode --packages C:/YOKI/Codex/AssetLibrary-worktrees/V03-015/.runtime/nuget
dotnet format apps/windows-client/AssetLibrary.Windows.slnx --verify-no-changes --no-restore
dotnet build apps/windows-client/AssetLibrary.Windows.slnx -c Release --no-restore
dotnet test tests/windows-client/AssetLibrary.Windows.Tests.csproj -c Release --no-build --no-restore --filter "(FullyQualifiedName~Thumbnail|FullyQualifiedName~Preview)&TestCategory!=NativeLive" --logger "trx;LogFileName=preview-targeted.trx" --results-directory .runtime/preview-validation
dotnet test apps/windows-client/AssetLibrary.Windows.slnx -c Release --no-build --no-restore --filter "TestCategory!=NativeLive" --logger "trx;LogFileName=windows-module.trx" --results-directory .runtime/preview-validation
dotnet build apps/windows-client/AssetHost/AssetLibrary.Windows.AssetHost.csproj -c Release --no-restore -p:AssetLibraryProductionHost=true -o .runtime/preview-validation/production-host
python -I -B scripts/validate_dotnet_source.py
python -I -B scripts/verify_repository.py
dotnet package list --project apps/windows-client/AssetLibrary.Windows.slnx --include-transitive --vulnerable --no-restore --format json --output-version 1 > .runtime/preview-validation/vulnerabilities.json
python -I -B scripts/validate_dotnet_dependencies.py --solution apps/windows-client/AssetLibrary.Windows.slnx --packages-dir C:/YOKI/Codex/AssetLibrary-worktrees/V03-015/.runtime/nuget --vulnerability-report .runtime/preview-validation/vulnerabilities.json
```

首次新增测试曾因MSTest集合断言建议及测试pipe类型（FileStream）而编译失败，已修正；首次全solution格式检查发现新文件CRLF，已统一LF，最终检查通过。没有修改规则或压低断言。

NativeLive未由本任务执行，不列入本任务120通过或跳过。Root首轮记录3通过/1失败：Preview1600真实链通过，Thumbnail512在登录阶段遇2并发/0排队限制；三类ClassLevel并行是已定位原因。85a13a4修正方法级DoNotParallelize与双profile共fixture；本地修正后的format/build/source policy通过，Root仅重验本图片case。root可使用现有 `--filter FullyQualifiedName~ThumbnailLiveTests`，同一fixture/一次登录现在顺序验证Thumbnail512/Preview1600两规格，各验证landscape.png和transparent.png真实Core→HTTP→WIC→独占后缀pipe链。没有自建服务器，未改生产端点/配置/原件。无需重新授权G4，豁免且不安排。
