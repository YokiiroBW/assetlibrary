# V03-007 独立图片解码运行时补充提案

2026-09-08；待协调批准，不是实测成功记录。原提案的Provider gate编号已纠正为机器账本中的M0-008-G1/G2/G3；M0-007是性能fixture任务。

## 真实起点

V01-021/nas-deployment-evidence.json记载NAS Linux5.10.55+x86_64、Docker24.0.2、memory_limit=true、cpu_cfs_quota=false、pids_limit=false。现有NAS Core容器1654用户、只读根、capabilities全移除、no-new-privileges，不挂Docker socket。不能假定可委托cgroup或创建嵌套namespace，不能用CPU shares充当硬配额，也不能改NAS全局nproc/内核配置。参照 `infra/docker/nas/README.md`。

Windows现有M0-008 probe仅证明Job资源限制与RestrictedToken降权；其实际探针仍能读写同用户sibling目录和回环网络。当前任务不重复这种实验后宣称AppContainer。

## 最小程序集与进程协议

建议新建 `services/worker-supervisor/ImagePreview/AssetLibrary.ImagePreview.Worker.csproj`，仅BCL+已审计SkiaSharp native，独立发布win-x64/linux-x64；不引用Core/Host/Npgsql/SDK，不接受Provider manifest、路径参数或任意操作。父工程必须排除子目录Compile，root维护slnx、依赖和锁。

核心现有Host的独立 `--read-only-worker preview-source` 可实现受控内容读取broker：它在Host配置前运行，不继承DB/Host环境，使用批准的库根+相对路径和安全打开句柄，读取稳定副本，避免NAS不响应的文件I/O占住Host进程线程。它只负责可信BCL文件读取，绝不调用Skia。parent通过有界pipe把稳定副本字节交decoder，decoder永远不获源路径。

decoder启动后只加载自身精确程序集/原生库、建立硬限制和OS拒绝规则；输入协议开始之前必须成功完成自检并报告固定版本ready。父仅传 magic/version/固定variant/有界长度+编码图片字节。输出固定magic/status/宽高/长度+PNG，parent逐字段检查长度、规格、终端状态和exit；无日志携带原路径/源内容。错误诊断至多4KiB。每请求一进程，无常驻解码池/消息队列/网络监听。未知协议/多余尾部/缺少终态/崩溃全部失败。

Windows进程在suspended状态创建、先assign Job再resume，避免启动阶段抢跑。Linux先完成受信运行时预热和不可撤销限制，再读取攻击输入。超时、断开、源变化、撤权均取消并回收；CPU时间上限不能替代15秒总墙钟期限。最终库授权、会话和源状态校验必须发生在首字节发送前。

## Windows可实施机制

使用Windows系统API `CreateAppContainerProfile` / `DeriveAppContainerSidFromAppContainerName`、`InitializeProcThreadAttributeList` / `UpdateProcThreadAttribute` 的 `PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES`；capability count=0，不申请internetClient/privateNetworkClientServer/enterpriseAuthentication。CreateProcess使用STARTUPINFOEX、CREATE_SUSPENDED、CREATE_NO_WINDOW和显式handle allowlist，只继承三根stdio pipe。匿名Job固定512MiB committed memory、active process1、CPU3秒、kill-on-close；wall timeout由父独立执行。[官方创建流程](https://learn.microsoft.com/en-us/windows/win32/secauthz/implementing-an-appcontainer)、[官方隔离语义](https://learn.microsoft.com/en-us/windows/win32/secauthz/appcontainer-isolation)。

自包含发布目录只给该应用SID ReadAndExecute；临时写目录仅当前请求所有权，内容不含Host状态、凭据或原资产路径。不给库根/Host私密目录ACL，不创建loopback exemption，不改HKLM/UAC/全局防火墙/安全策略。首次探针只在本任务临时目录创建命名profile与ACL，用后DeleteAppContainerProfile并核验SID/profile/ACL/进程残留；需要协调明确将这一临时平台验证纳入允许范围。

必须用同启动器下的受信攻击probe实际证明：同用户sibling marker不可读/写、伪Host配置/伪原库marker不可读、TCP回环/UDP/外部连接被拒、非允许句柄不可用、子进程不可创建、CPU/内存超限由OS阻止、父退出/取消没有孤儿。真实图片解码通过后还必须重复关键拒绝探针；任何一项缺证据均不启用生产。

## Linux最小候选：进程内安装、内核执行的seccomp allowlist

为了避免给NAS Core增加SYS_ADMIN、Docker socket或setuid helper，建议先验证独立decoder在读取图片之前安装 `no_new_privs` + `seccomp(SECCOMP_SET_MODE_FILTER, SECCOMP_FILTER_FLAG_TSYNC)`，覆盖.NET已创建的全部线程。任何返回非零（包含不能同步的线程ID）都必须立即退出，不降为单线程过滤。[Linux seccomp文档](https://man7.org/linux/man-pages/man2/seccomp.2.html)、[内核说明](https://www.kernel.org/doc/html/v5.15/userspace-api/seccomp_filter.html)。

过滤器必须先校验AUDIT_ARCH_X86_64并拒绝x32 syscall变体，采用审核过的显式allowlist。建立限制之前预加载本进程自己运行时与Skia，并确保没有Host/DB/源目录fd；不读取父配置，不启用.NET diagnostics/agent/profiler环境。进入限制后：文件open/openat/openat2/open_by_handle_at/creat与路径写操作、socket/connect/socketpair/网络消息、exec/fork/vfork、ptrace/process_vm/pidfd获取、io_uring、mount/unshare/setns/bpf等均不在允许集合；仅保留有界stdio读写、runtime必要内存/同步/时间/信号操作。若.NET需要clone，必须限定CLONE_THREAD，clone3返回ENOSYS以避免检查不了其指针参数，而非允许任意新进程。还要审核signal target、fd继承/复制、sendmsg与proc/sys旁路；不能把列了几项拒绝的黑名单称为完整allowlist。

此候选把decoder约束为已打开runtime映射+stdio字节机器，不需要后续文件打开或网络连接，适合固定三格式内存解码；它不能作为任意第三方Provider的通用运行环境，也不能自动关闭M0-008-G2要求的完整发行门禁。若运行时/色彩/编码需要额外文件操作，默认失败，不放开任意open作为临时修复。

内存是独立验收点：`DOTNET_GCHeapHardLimit`只约束GC，**不约束Skia native总内存**。[官方GC设置](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/garbage-collector)。可对独立进程设置并读回RLIMIT_CPU、RLIMIT_AS/DATA/FSIZE，配合固定GC堆与实际基线映射测量，但必须用native匿名映射/分配压力probe证明具体硬界限，不能把.NET巨大虚拟预留误认成RSS。若无法在此NAS证明可接受的独立硬内存限制，需协调部署一个独立memory-capped隔离worker包装或保留503，不能默默退回仅进程内像素上限。实现前不把512MiB RSS写成已保证值。

最小实际验证集：TSYNC后所有线程的open/network/ptrace/fork拒绝；父仅传已授权稳定字节；32MiB/40MP等输入限制；native/GC分配压力、CPU busy、超时/中断/父退出；三格式/透明/方向实际解码；跨身份与源变化。NAS上的只读能力探测、合成fixture运行与部署配置写者保持root，V03-007不直接连接或更改NAS。

## 请协调冻结的准入点

1. SkiaSharp与Linux.NoDependencies精确4.151.2；根版本/lock/许可证notice由root单一写者，授权worker工程持有PackageReference。依赖只在decoder，不进入Host/Core项目引用。
2. 独立worker目录、父csproj Compile排除、Host/source-broker接线、GatewayAuth授权编排和测试fixture编译链接的具体所有权。
3. Windows任务临时AppContainer profile/自有目录ACL探针范围；Linux先seccomp候选是否准许开发、实际NAS验证由root执行；任一平台没有硬隔离证据时生产端点503。保留第三方Provider完整门禁。
