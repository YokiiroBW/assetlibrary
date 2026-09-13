# V03-028 测试记录

## 后续Windows旧隔离路径单项（c775b7d）

不可变c775b7d2b221479510f958f7e302a676b024e778在自有.runtime副本实际win-x64 NativeAOT发布；SDK10.0.111/MSVC14.44，原RID锁locked restore且哈希不变，严格测试项目build0warning/0error。仅NativeAotDecoderRunsInsideLpacAndReturnsARealThumbnail执行1项通过/0失败/0skip；现有LPAC+Job下Ready、512×300真实PNG与exit0。临时sandbox前后0、剩余自有Worker0，3564个归档文件未改。见[原始TRX、命令、哈希及清理](windows-lpac-c775b7d/README.md)。不重复运行Windows门禁/G4，不将此正常图回归扩张为最大图片或NAS验收。

## 后续开发Linux完整测试（a87b44c）

dev-230独立/tmp副本和给定镜像，实际SDK10.0.111、Linux6.14、内存2GiB/CPU2，未使用root构建缓存。完整131项92通过/39未执行；补Host locked restore/build后仅补充source-broker1项通过，唯一用例汇总93通过/0失败/38未执行。Linux真实SO_PEERCRED通过；剩余34 Windows项、1非Linux负控及3 PG/HTTPS live夹具项，不声称全部131都执行。两测试容器Exit0/无OOM且精确删除并核不存在。详见[命令、原始TRX与清理](linux-tests-a87b44c/README.md)。此后没有修改任何代码，且不是NAS验收。

## 原Windows组件测试（a173210）

Windows11 x64；SDK10.0.111位于C:/YOKI/Codex/worktrees/V01-004/.runtime/sandbox-storage/V01-004/dotnet；NuGet缓存C:/YOKI/Codex/AssetLibrary-worktrees/V03-015/.runtime/nuget。不修改项目/锁；.runtime/socket-test-links.targets只为AssetLibrary.Preview.Tests增加三份UnixSocketImage*实现源，与现有内部源码测试方式一致。

```powershell
$env:DOTNET_ROOT='C:/YOKI/Codex/worktrees/V01-004/.runtime/sandbox-storage/V01-004/dotnet'
$env:NUGET_PACKAGES='C:/YOKI/Codex/AssetLibrary-worktrees/V03-015/.runtime/nuget'
$env:DOTNET_CLI_HOME='C:/YOKI/Codex/AssetLibrary-worktrees/V03-028/.runtime/dotnet-home'
$env:DirectoryBuildTargetsPath='C:/YOKI/Codex/AssetLibrary-worktrees/V03-028/.runtime/socket-test-links.targets'
& "$env:DOTNET_ROOT/dotnet.exe" restore tests/dotnet/AssetLibrary.Preview.Tests/AssetLibrary.Preview.Tests.csproj --locked-mode
& "$env:DOTNET_ROOT/dotnet.exe" restore services/core-server/Host/AssetLibrary.CoreServer.Host.csproj --locked-mode
& "$env:DOTNET_ROOT/dotnet.exe" build tests/dotnet/AssetLibrary.Preview.Tests/AssetLibrary.Preview.Tests.csproj --configuration Release --no-restore
& "$env:DOTNET_ROOT/dotnet.exe" build services/core-server/Host/AssetLibrary.CoreServer.Host.csproj --configuration Release --no-restore
& "$env:DOTNET_ROOT/dotnet.exe" test tests/dotnet/AssetLibrary.Preview.Tests/AssetLibrary.Preview.Tests.csproj --configuration Release --no-build --no-restore --filter 'FullyQualifiedName~SocketImage|FullyQualifiedName~ImagePreviewServiceTests|FullyQualifiedName~AuthorizedImagePreviewTests|FullyQualifiedName~PngDerivativeValidationTests' --logger 'trx;LogFileName=socket-affected.trx' --results-directory .runtime/socket-results
```

格式实际使用dotnet format上述测试csproj/Host csproj --verify-no-changes --no-restore --include逐项新UnixSocketImage*/SocketImage*源及Runtime/Host配置，通过；具体日志socket-format-verify.log、socket-host-format.log。构建socket-build-final.log及socket-host-build.log均0warning/0error。restore首次NuGet TLS有一次重试后成功，没有改锁或关闭审计。

最终python -I -B scripts/verify_repository.py通过，含handoff、533架构输入、依赖/主题和14项架构测试；日志.runtime/socket-repository.log。Alpha保持blocked。changed_files来自git diff --name-only -z 9c76f66。

最终45 passed/0 failed/1 skipped，8秒；.runtime/socket-results/socket-affected.trx和.runtime/socket-affected.log。先前仅Socket过滤32 passed/1 skipped是同一逻辑集合，不重复累加。Linux-only RealLinuxPeerUsesEffectiveUserAndRejectsUnconnectedSocket在Windows明确未执行，不能算身份验证通过；其它socket测试用真实Windows AF_UNIX与明确假peer策略。Linux运行时该项会读取真实geteuid和SO_PEERCRED，另一个非Linux负控会Inconclusive。

覆盖：512/1600请求profile、精确四字节source、服务观察Core不half-close、服务保持连接直到Core关闭证明不等EOF；peer拒绝和坏Ready不读源；空失败status4..7映射、坏profile/length/PNG CRC/截断/已缓冲多余字节/失败残留字段；源变化不改分类，零/负源Invalid及超过32MiB LimitExceeded均在connect前拒绝。排队中取消不开第二连接；等六秒后进入第二请求仍在最初八秒预算内超时；在途和等待同时Dispose、重复Dispose、Dispose后调用无semaphore提前释放，Dispose不等八秒。保留原应用两许可、cache/源复验、授权和PNG相关13项通过。

初始实现将调度生命周期与socket帧交换放一类触及CA1506；按真实职责拆为Decoder/Transport，测试按协议/生命周期拆类，未调阈值或屏蔽诊断。最终差异审查只包含允许的Infrastructure、Host配置和新Socket测试，无worker/ABI/锁/公共契约变更。

临时socket及目录由fixture在finally关闭/删除；没有使用生产固定socket、NAS或图片原件。真实Linux peer UID0/1654、容器监督器、MEMLOCK/NPROC/namespace和整包端到端由root执行。root需接入已批准的ImagePreviewService decoder.Dispose及正式csproj源链接；本工作区不代写这些共享文件。
