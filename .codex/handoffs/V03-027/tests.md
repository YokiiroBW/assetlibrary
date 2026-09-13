# V03-027 验证记录

Windows本地工具为SDK10.0.111（V01-004固定工具路径），NuGet复用V03-015/.runtime/nuget。Linux/NAS操作由root单一所有者执行；本任务没有远程登录或控制NAS。

| 检查 | 结果与范围 |
|---|---|
| ImagePreview locked restore | 通过，无锁修改 |
| ImageSupervisor restore | 通过；本地临时NuGetLockFilePath在其.runtime，正式普通/AOT锁由root生成 |
| 两项目Release build --no-restore | 最终0警告/0错误 |
| 两项目format / 新测试format | 最终通过 |
| ImageContainer定向测试 | 最终34通过、0失败、0跳过（22ac562） |
| 相邻预览filter排除Windows/Live类 | 54通过、3跳过、0失败（a2b7d8a，含当时31个新增用例，不重复加总） |
| validate_dotnet_source.py | 552文件通过，无重复块豁免 |
| verify_repository.py | 通过：M0-009架构/契约/源代码/依赖等快速gate，Alpha发布仍blocked |
| Root首次NAS AOT probes | isolation/threads/memory passed；CPU137且OOMfalse；对应7255b45集成镜像 |
| Root首次真实socket图片 | 10文件×2规格=20项通过，peer_uid0；owned_containers_cleaned |

3个跳过项为 `RealHostSourceChildStreamsVerifiedBytesAndReleasesTheOriginalHandle`、`RealPostgresRoundTripPreservesUnchangedImageAdmission`、`LeafSymbolicLinkIsRejected`，缺少原生source worker/真实PostgreSQL夹具/Windows链接能力。它们不算通过，也不替代root的真实平台链路。

本地34用例覆盖：跨重启3次未完成预算、损坏/截断/未知计数拒绝；自身deadline与已观测client/operator取消的预算区别；Uid/Gid真实/有效/保存/fs字段、effective/permitted/inheritable capabilities与NNP负控；实际controller路径解析；固定24B帧、32MiB超限拒绝、未知规格/像素字段、source精确转送不消费监控字节、失败禁止body、512/1600规格上限。

真实命令（使用精确dotnet/python可执行）：

```powershell
dotnet restore services/worker-supervisor/ImagePreview/AssetLibrary.ImagePreview.Worker.csproj --locked-mode --packages C:/YOKI/Codex/AssetLibrary-worktrees/V03-015/.runtime/nuget
dotnet restore services/worker-supervisor/ImageSupervisor/AssetLibrary.ImageSupervisor.csproj --lock-file-path .runtime/supervisor.lock.json --packages C:/YOKI/Codex/AssetLibrary-worktrees/V03-015/.runtime/nuget
dotnet build services/worker-supervisor/ImagePreview/AssetLibrary.ImagePreview.Worker.csproj -c Release --no-restore
dotnet build services/worker-supervisor/ImageSupervisor/AssetLibrary.ImageSupervisor.csproj -c Release --no-restore
dotnet format services/worker-supervisor/ImagePreview/AssetLibrary.ImagePreview.Worker.csproj --verify-no-changes --no-restore
dotnet format services/worker-supervisor/ImageSupervisor/AssetLibrary.ImageSupervisor.csproj --verify-no-changes --no-restore
dotnet test tests/dotnet/AssetLibrary.Preview.Tests/AssetLibrary.Preview.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~ImageContainer --logger "trx;LogFileName=container-pure-final.trx" --results-directory .runtime/v03-027-validation
dotnet test tests/dotnet/AssetLibrary.Preview.Tests/AssetLibrary.Preview.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName!~Windows&FullyQualifiedName!~Live" --logger "trx;LogFileName=preview-offline.trx" --results-directory .runtime/v03-027-validation
python -I -B scripts/validate_dotnet_source.py
python -I -B scripts/verify_repository.py
```

Root授权本地临时将四个BCL源链接进预览测试项目：ImageCircuitState、LocalImageFrames、LinuxContainerStatus、LinuxContainerMemory。测试结束已用保存的原始字节恢复csproj；root真正集成入口另有相同链接，不提交跨owner项目改动。

最终源码审查后将deadline分类与新增父访问探针集中在22ac562，随后重跑定向34用例与严格构建。正常首次迭代中的代码复杂度/平台注解、CancellationToken参数顺序及源码写入转义错误均修正，未禁用分析器。初版三cap方案被root发现线程级capset问题后废弃，当前四cap监督/全线程cap0子进程才是最终方案。

Root22ac562同码新增证据（本任务已只读核验）：

- `.runtime/second-native-isolation.log`：parent_memory_denied=true、parent_ptrace_error=1，全部isolation条件通过。
- `.runtime/nas-fault-cases-second.log`：3种正常取消均children0/failures0；child上传期崩溃计数1；自身deadline8.2877秒计数2；PID1死亡重启计数3、Ready7/new_children0；fault_cases_passed及owned_fault_resources_cleaned。
- `.runtime/nas-maximum-cases.log`：40MP+32MiB返回Limit6，未通过期望成功。
- `.runtime/nas-boundary-controls.log`：40MP小编码Limit6；1×1+32MiB Unavailable7；资源已清理。
- `.runtime/nas-31m-control.log`：1×1+31MiB Unavailable7，未通过。

以上路径相对root V03-026 worktree，不是本任务执行NAS测试。最大边界仍有真实失败，不能合并成“全平台全通过”。

## 0063769 / 8cd23ea

最小SetImmutable两行优化及固定stage+退出码诊断均完成strict build/format/source-policy验证。Windows临时functional probe（`.runtime/immutable-validation/Probe.csproj`，未提交项目或锁）以原始22ac562 StaticImageDecoder为baseline：mutable image与bitmap像素指针不同，immutable image指针相同；18个before/after PNG逐字节和尺寸一致。输出为 `IMMUTABLE_SHARE_POINTER_OK; BASELINE_EQUIVALENCE_CASES=18; WINDOWS_FUNCTIONAL_ONLY`。

没有重跑未变化的34纯单元用例或相邻预览套件。Root将0063769+8cd23ea一次AOT复验最大边界；当前GC64MiB、AS512MiB、memcg512MiB及现有Job限制均未变。源码现暂停，待实际诊断反馈；最大输入+1、最终Core同NAS闭环和部署仍由root验收。不以新cgroup为零冒充旧资源回收，不安排G4。
