# V03-036 测试记录

Windows x64，SDK10.0.111：C:/YOKI/Codex/worktrees/V01-004/.runtime/sandbox-storage/V01-004/dotnet；进程内DOTNET_ROOT/PATH显式指向此处。NUGET_PACKAGES为C:/YOKI/Codex/AssetLibrary-worktrees/V03-015/.runtime/nuget。Python为Codex primary runtime。全部命令在V03-036，未改系统配置。

```text
python -I -B -m unittest discover -s tests/integration/native-clients -p test_serve.py -v
dotnet restore tests/dotnet/AssetLibrary.WebGateway.Tests/AssetLibrary.WebGateway.Tests.csproj --locked-mode
dotnet build tests/dotnet/AssetLibrary.WebGateway.Tests/AssetLibrary.WebGateway.Tests.csproj --configuration Release --no-restore
dotnet format tests/dotnet/AssetLibrary.WebGateway.Tests/AssetLibrary.WebGateway.Tests.csproj --verify-no-changes --no-restore --include tests/dotnet/AssetLibrary.WebGateway.Tests/NativeClientSampleAssets.cs tests/dotnet/AssetLibrary.WebGateway.Tests/NativeClientImageFixtureTests.cs
dotnet test tests/dotnet/AssetLibrary.WebGateway.Tests/AssetLibrary.WebGateway.Tests.csproj --configuration Release --no-build --no-restore --filter FullyQualifiedName~NativeClientImageFixtureTests --logger "trx;LogFileName=image-fixture.trx" --results-directory .runtime/validation
python -I -B tests/integration/native-clients/make_page_corpus.py --output .runtime/corpus
python -I -B scripts/verify_repository.py
```

- Python13/13通过，0.747s。旧8项保留；新增128/129、真实64MiB复制及多1字节拒绝、32MiB+1拒绝、生成器120图/staging/hash/mtime/不覆盖与输出边界。
- C#9/9通过、0skip、326ms：原3项保留；DataRows验证128接受/129拒绝、直接调用64MiB通过/+1拒绝、空文件/32MiB+1拒绝。正控default138、128图总266、64MiB实际字节数；复制数据参加hash/mtime生命周期。
- locked restore通过，仅现有项目依赖；Release最终0warning/0error（1.80s），WebGateway.Tests及Host完整输出；定向format返回0。
- verify_repository通过，M0-009架构门禁与源/依赖/主题/契约检查通过；未重复运行无关全solution或全平台测试。
- 120图实际生成与目标读回hash成功，5,183,400B，不重新编码媒体。test-only数据非真实资产。

初次build和format失败各2项MSTEST0037：IsTrue比较需用IsGreaterThan/IsLessThanOrEqualTo。仅改断言写法后两项检查通过；原失败保存 `.runtime/validation/{build,format}-initial.log`，最终日志/9项TRX在同目录。没有禁用分析器或降低warnings-as-errors。

本任务未运行serve --execute、Host/PG/decoder、真实Core/Explorer、GUI/注册/安装/NAS、长时循环/G4。root负责实机跨页与清理；只构建程序不能代替这些结果。
