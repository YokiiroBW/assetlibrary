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

## 集成检查点与真实图片样例生命周期

3890793在合入客户端/Windows诊断及同步实际状态后，通过 `python -I -B scripts/verify_repository.py`：handoff/模块架构/生成SDK/主题/源/依赖均有效，21迁移与14架构回归通过，Alpha依旧blocked。四个模块目录的Git tree与各自审查提交相同，见integration-checkpoint.json；原窗口65个Web、77个Android用例单列，不重复累计或重跑。Android候选APK已复制至本worktree.runtime/releases/native-clients，重新计算SHA256与原窗口一致，未替换旧包或宣称真实预览通过。

随后实际运行README的真实服务器入口，增加 `--image-fixtures .runtime/sandbox-storage/V03-005/preview-fixtures --lifetime-seconds 2`，未设置worker：真实PG16.15、21迁移、6个LOGIN、HTTPS及首次扫描成功，文件数148，READY后按期限退出。原hash/mtime保持不变，Host/PG退出、HTTPS端口关闭、6个LOGIN移除、私密runtime删除均verified，非敏感原始结果见image-fixture-lifecycle.json。这是一个真实新增图片样例生命周期用例，不是图片端点或客户端图片显示验收。

这10项输入随后按manifest强hash核对并逐字节复制进 `tests/integration/native-clients/fixtures/image-preview-v1`，方便其他worktree和CI复用；没有引入Pillow运行时依赖或修改图片内容。

## Linux 安全读取基础

f580b15经代码审查后合入，4b87896将Preview.Tests加入既有solution/Windows+Ubuntu CI矩阵和依赖缓存，不新增测试层级。该不可变集成源码tar为34,693,120B，SHA256 94b72b1d7fe25d42143df2b5e1729ccee4e5de341218ed7fb8e2f76945252919；传至dev-230自有临时目录后重新验证，再以安全tar过滤解包，未写远端工作仓库或NAS210生产。

复用已核实10.0.111 SDK镜像，仅在自有构建镜像添加clang/zlib开发包。测试容器非root、2CPU/3GiB/512进程限制、去全部capability和no-new-privileges，仅挂本任务源码；执行Preview.Tests的locked restore、format verify、Release build和StableImageSourceTests，全部通过，0warning/0error、9/9测试、0skip。原始TRX逐项读回含真实叶文件symlink拒绝；JSON索引、hash与工具镜像来源见linux-source-boundary.json，原始日志/TRX在本worktree.runtime/linux-source-evidence。

结束读回自有容器不存在，测试sandbox子目录为0。构建镜像和自有临时源码保留供后续NativeAOT测试；没有启动decoder、修改NAS、或用Docker外层拒绝冒充worker自身隔离。SDK首次提示workload验证诊断，但实际restore/format/Release和测试通过；未因此更新全局workload或忽略警告。

审查另发现数据库timestamptz与文件100ns时间精度潜在不一致，已交V03-007在实际索引接线前复现并修正；上述9项只覆盖物理读取基础，不能替代持久索引/解码/HTTP联调。
