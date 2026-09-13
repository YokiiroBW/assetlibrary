# V03-027 最终验证记录

本地：Windows、SDK10.0.111，NuGet复用V03-015/.runtime/nuget；Linux AOT由root builder生成。Root后续授权本任务使用自建V03-027 label临时NAS容器/卷及只读合成corpus，所有操作均独立于production和V03-026 acceptance。

| 检查 | 结果 |
|---|---|
| ImagePreview locked restore | 通过，无锁改动 |
| ImageSupervisor restore | 通过；本地临时锁在.runtime，正式锁由root所有 |
| 两项目Release build / format | 通过，0 warning/0 error |
| ImageContainer+ImageInputPolicy定向 | 最终38通过、0失败/跳过 |
| 前期相邻预览filter | 54通过、3跳过、0失败（包含当时31新增用例，不重复加总） |
| 源策略 / repository gate | 553文件源策略通过；架构/契约快速gate通过 |
| Immutable功能对照 | Windows pointer-sharing成立，18个PNG字节/尺寸before-after相同；不当作Linux隔离证明 |
| Root NAS正常图与故障 | 20图、resource probes、父mem/ptrace及6故障case通过；详见root日志 |
| NAS Region128对照 | pre VmSize424164→227360KiB；40MP两profile成功，真实32MiB像素两profile成功 |
| 最终正式image | 10个边界/邻接case通过，isolation通过；所有case ledger0/child0/oom0/failcnt0 |
| 最终资源查验 | V03-027 containers=[]、volumes=[] |

3个旧条件缺失跳过：RealHostSourceChildStreamsVerifiedBytesAndReleasesTheOriginalHandle、RealPostgresRoundTripPreservesUnchangedImageAdmission、LeafSymbolicLinkIsRejected。未把缺少原生source worker、PG夹具或Windows链接能力记作通过。

## 本地入口

```powershell
dotnet restore services/worker-supervisor/ImagePreview/AssetLibrary.ImagePreview.Worker.csproj --locked-mode --packages C:/YOKI/Codex/AssetLibrary-worktrees/V03-015/.runtime/nuget
dotnet restore services/worker-supervisor/ImageSupervisor/AssetLibrary.ImageSupervisor.csproj --lock-file-path .runtime/supervisor.lock.json --packages C:/YOKI/Codex/AssetLibrary-worktrees/V03-015/.runtime/nuget
dotnet build services/worker-supervisor/ImagePreview/AssetLibrary.ImagePreview.Worker.csproj -c Release --no-restore
dotnet build services/worker-supervisor/ImageSupervisor/AssetLibrary.ImageSupervisor.csproj -c Release --no-restore
dotnet format services/worker-supervisor/ImagePreview/AssetLibrary.ImagePreview.Worker.csproj --verify-no-changes --no-restore
dotnet format services/worker-supervisor/ImageSupervisor/AssetLibrary.ImageSupervisor.csproj --verify-no-changes --no-restore
dotnet test tests/dotnet/AssetLibrary.Preview.Tests/AssetLibrary.Preview.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ImageInputPolicyTests|FullyQualifiedName~ImageContainer" --logger "trx;LogFileName=container-input-final.trx" --results-directory .runtime/v03-027-validation
python -I -B scripts/validate_dotnet_source.py
python -I -B scripts/verify_repository.py
```

Root批准本地临时链接ImageCircuitState、LocalImageFrames、LinuxContainerStatus、LinuxContainerMemory、ImageInputPolicy五个BCL源到既有Preview.Tests；每次结束均用保存的原始字节恢复csproj。Root真实同源项目已接线；本任务没有提交共享csproj或锁。

38用例覆盖预记账跨重启3次熔断、坏状态拒绝、deadline与已观测client/operator取消区别、Uid/Gid含fsuid和cap/NNP负控、controller映射、固定帧/精确source转送、错误零body及闭合规格，并新增4MiB可通过预检查、+1两类非IDAT拒绝、大IDAT不误套metadata预算。CRC常量独立生成；预检查单测不等同于完整Skia出图。

## NAS证据

本任务脚本与完整日志在 `.runtime/v03-027-validation`；摘要和日志强hash保存在nas-validation.json。正式Supervisor PID1、UID降权和全部硬限始终使用原实现；没有诊断PID1替身或提高CPU/Heap/AS/memcg。

- nas-baseline-ticks.log：third image247e...，40MP小编码Limit6/pre VM424164KiB；31MiB padding CPU峰2.97s、wall3.57s、result/exit137，memcg约98.7MB且无OOM。
- nas-region128-comparison.log：fourth imagefc8a...，VM减少约192MiB。40MP p1为1600×1000，CPU观测0.27s；p0为512×320。31MiB padding仍CPU2.98s，证实CPU和AS是两件事。
- nas-region128-pixel32m.log：7600×1092的exact32MiB像素PNG核SHA后p0/p1成功，CPU观测0.08/0.19s。
- nas-final-metadata.log：正式image76b3...，4MiB-1/=成功1×1（CPU峰0.89/0.90s）；4MiB+1、31M、32M单块均Limit6且ledger0；8个≤4MiB块组成exact32MiB成功1×1，CPU峰1.06s/wall1.75s；32MiB像素p0/p1成功（CPU0.09/0.18s）；两次邻接普通图正常。10case均child0/ledger0/OOM0/failcnt0，最终isolation全部passed。
- nas-cleanup-check.log：独立查询所有V03-027 label容器/卷均为空。

Root已有日志位于V03-026/.runtime：first-native-probe.log、nas-first-cases.log、second-native-isolation.log、nas-fault-cases-second.log。早取消/Ready后/partial均计数0且无child；上传期杀child计数1；保持连接并停住child导致自身8.29s超时计数2；杀PID1重启后计数3、Ready7、无新child。父mem拒绝与ptrace EPERM均通过。

RegionRange只减少虚拟地址预留，GC64MiB、AS/memcg512MiB、CPU3秒、NPROC1不变。超大单块PNG metadata现在按已批准资源规则拒绝，不把它写成成功出图。Core端到端和最终production部署由root继续，不用这些模块结果关闭整个V0.3/G4/其他客户端门禁。
