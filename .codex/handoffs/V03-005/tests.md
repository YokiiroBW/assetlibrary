# V03-005 当前验证记录

本阶段是分工/启动，不是新功能验收。共享70ce45c基线已运行python -I -B scripts/verify_repository.py并通过（原迁移21、架构14及现有源/SDK/依赖检查），原日志在主目录.runtime/parallel-browse-baseline.log。此35项仅是基线，不算本批新增功能测试。

4个App创建的工作区均检查git-common-dir与本仓库一致，初始HEAD70ce45c/无改动，然后建立独立codex/v03-006..009分支，生成各自任务/交接。真实thread ID通过read_thread核实。新窗口默认权限造成命令审批等待，已向用户说明；不能把waitingOnApproval说成已实施。

本轮修改限规划、任务图/注册表/状态和交接；没有应用/核心/wire/数据库/依赖变更，不重复既有业务测试。规划元数据在提交前执行既有handoff/架构校验。实际功能、平台、权限/源安全、性能和联调测试在各窗口实现稳定后由V03-005集中汇总和复核。

统一预览验收的10个合成输入已准备在本协调worktree的.runtime/sandbox-storage/V03-005/preview-fixtures，覆盖JPEG/PNG/WebP、EXIF方向、透明、相同内容不同文件名、中文路径和损坏/超大头/SVG拒绝；manifest记录源hash/mtime。仅为待执行输入，不计预览通过，没有使用个人资产。

## 共享真实服务测试入口扩展

新增显式 `--image-fixtures` 与 `--image-preview-worker`。默认138文件、现有连接JSON、真实认证与清理路径保留；图片复制进同库的图片样例目录，参与原件hash/mtime核验。没有替换decoder或跳过生产隔离的测试模式。

稳定修改审查后执行现有入口（精确SDK10.0.111、Python3.12；均在V03-005私有worktree/cache）：

```text
python -I -B -m unittest discover -s tests/integration/native-clients -p test_serve.py -v
dotnet restore tests/dotnet/AssetLibrary.WebGateway.Tests/AssetLibrary.WebGateway.Tests.csproj --locked-mode
dotnet format tests/dotnet/AssetLibrary.WebGateway.Tests/AssetLibrary.WebGateway.Tests.csproj --verify-no-changes --no-restore
dotnet build tests/dotnet/AssetLibrary.WebGateway.Tests/AssetLibrary.WebGateway.Tests.csproj --configuration Release --no-restore
dotnet test tests/dotnet/AssetLibrary.WebGateway.Tests/AssetLibrary.WebGateway.Tests.csproj --configuration Release --no-build --no-restore --filter FullyQualifiedName~NativeClientTlsFixtureTests|FullyQualifiedName~NativeClientImageFixtureTests
```

Python 8项通过，.NET 6项通过，均0失败/跳过；其中新增图片输入验证3+3项，旧生命周期/TLS用例5+3项。复制路径/manifest/hash拒绝、旧样例数量不变、中文嵌套名与变更后原件核验失败均有实际断言。首次构建因MSTEST0037断言表达规范失败2处，按分析器改为AreEqual后format/Release零警告零错误通过；没有关闭规则。

该证据只证明真实服务测试入口的可构建性和输入边界，真实Core图片请求、隔离decoder和最终清理联调尚待执行，不计为图片功能通过。
