# Windows原生隔离矩阵与实际SID恢复检查点

状态partial，源码提交 `c6649c5d70b4f2d966d9ecdb02c7f71aed4ee55b`。本批完成已约定范围的原生平台证据并交root审查，不自动启用Windows能力、不关闭通用Provider或Explorer门禁。root随后合入249fed9，独立运行活worker/SID回归1/1通过。

## 源码与不变约束

生产只加强WindowsImageProcessIdentity：PID创建时间不符而进程仍活时，必须继续核对AppContainer SID；仍是记录精确SID则不能当作PID复用。查询失败保持拒绝，进程已退出或确认不同SID才允许恢复。以实际睡眠worker、正确SID和错误正creation=1回归，修复前会误报已退出，修复后保留活worker。前一提交5bce4cd的负creation/符号/非规范数字14项验证继续成立。

原生测试由当前WindowsImageProcess/Startup/Profile/Capability/Job/Journaling启动。LPAC opt-out=1、sole lpacCom、零network能力、私有RX、三根stdio HANDLE_LIST、Job active1/512MiB committed/名义3秒user CPU/kill-on-close均保留。只有明确标注的可信控制使用普通token或普通AppContainer；不运行decoder/图片，不向控制进程提供Host/DB环境。

没有HKLM、UAC、全局策略、loopback exemption或防火墙修改。BITS控制只创建自己新返回的空job并立即Cancel，不添加URL或文件。原生probe仅在本任务目录；没有对用户资产或其他profile进行写入/删除。

## 最终本机矩阵

native-final-matrix.trx共12项通过、零失败/跳过。其中10项为文件/RX、事件白名单、FSO和BITS COM、CPU、内存、子进程拒绝、取消、父退出、实际SID恢复；另2项准确限定为本机Winsock/WinHTTP初始化阻断，不当作网络层EACCES证明。

| 行为 | 实际证据与边界 |
| --- | --- |
| 文件/RX | 自有host-only合成marker读被ERROR_ACCESS_DENIED拒绝，私有RX工作目录不能新写文件；原marker不变 |
| 非stdio事件 | 可信控制真实继承事件并使parent event signaled；生产三stdio上下文不能signal父事件，返回错误6或捕获STATUS_INVALID_HANDLE；父事件仍未signaled |
| 进程内COM | 相同最小环境的可信FSO控制能打开合成marker，LPAC在activation或OpenTextFile阶段被拒绝 |
| 进程外COM | 可信BITS控制CreateJob成功并Cancel成功；LPAC不能activation/create该job，E_ACCESSDENIED；没有添加传输 |
| CPU | 原生查询Job实际PerProcessUserTimeLimit=30000000；独立宽裕parent deadline未触发，退出为C0000044/STATUS_QUOTA_EXCEEDED；记录实际user CPU和wall时间 |
| 内存 | Native VirtualAlloc逐8MiB commit/touch，至少256MiB可成功，达到Job边界后失败；实际Job=536870912、PrivateUsage不超过该值；不把GC预算等同于总native内存 |
| 子进程 | 同一个probe试图创建自己受控的suspended子进程被拒绝，未改变active1 |
| 取消 | 已ready的睡眠worker被取消并确认退出，不以管道返回码代替reap |
| 父退出 | controller持续持有子stdin写端，仅杀父进程而非tree kill；睡眠子进程仍退出，随后Recover清理1个owner记录。排除了stdin EOF或controller直接杀子进程的混淆 |
| 实际SID | 错误正creation时间不能隐藏仍活且SID相同的worker |

CPU是Windows周期检查，不是严格3秒硬上界。初次wall8.2秒以及一次user CPU5.953秒超出了测试自定的7秒wall/4.5秒CPU假设；修正测试为检查真实配置、CPU达到阈值、quota退出及parent未取消，并保留所有旧失败。例次报告user CPU5.078125秒、wall6.9601626秒、C0000044。没有提高生产预算或新增第二监督机制。[Microsoft周期检查语义](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-jobobject_basic_limit_information)

事件探针首次在非法SetEvent后等待错误处理。阶段日志确认参数已完整读入；仅探针本进程设置错误UI抑制并捕获C0000008，避免GUI/WER等待，不修改系统策略。捕获其他异常仍继续系统异常处理。另一次失败来自临时阶段日志重复key，删除已无必要的诊断阶段后通过；不将这些测试工具失败记为产品通过。

## 非本机LAN控制

root在dev-230提供短时listener。第一端点192.168.31.230:59249不在服务器既有允许端口范围，普通token TCP超时/HTTP12002，服务端accepted0；该轮不能作为隔离证据，原始结果保留。

第二端点192.168.31.230:39420使用既有允许区间、没有改防火墙。六次控制时间2026-09-08T18:41:52.705594Z至18:41:53.317923Z：

| token/隔离 | 原生TCP | WinHTTP |
| --- | --- | --- |
| 普通token，same probe/minimal environment | socket0/connect0 | sent1/error0，HTTP204 |
| 普通AppContainer，sole lpacCom、零network、仅opt-out=0 | socket0/connect10013 | sent0/error12029 |
| LPAC，原生产全部约束 | WSAStartup10107，未建socket | sent0/error12004 |

root服务器实读日志：仅root预检HTTP、随后本窗口普通token TCP和HTTP，共3个accept；rejected peers=0，容器无连接。listener已自然结束、39420端口关闭，回执在root自有 `.codex/handoffs/V03-005/windows-lan-control.json`。两端时钟有轻微偏差，不按毫秒绝对同序拼接事件。

这轮区分普通AC零network能力的网络层拒绝和LPAC更早的初始化阻断。不能声称LPAC直接connect返回10013，也不能把本机loopback测试泛化为LAN拒绝。root明确接受上述范围证据；未测试原始AFD/全部网络API、所有COM类别、所有Windows SKU或通用第三方Provider，不解除其通用门禁。

## 工具源码与复现

`windows-native/`归档NativeProbe.cpp.txt、NativeCMakeLists.txt、CrashParent.cs.txt/csproj.txt、NetworkControl.cs.txt/csproj.txt及NormalStartup.cs.txt，均为当时工具的文本快照。evidence.json记录原始工作文件SHA256与LF标准化SHA256，避免Git换行转换被误认成相同原始字节；也记录实际probe、controller、父工具二进制SHA与各TRX摘要/hash。原始.runtime文件保留供root复核。

原生probe在 `.runtime/windows-image/confinement` 以冻结SDK10.0.26100.0、MSVC19.44.35228、C++17、/MT、/W4 /WX /analyze构建。NativeCMakeLists仅链接Windows系统库。崩溃parent以精确SDK10.0.111构建，反射调用本任务已编译测试程序集内的同一WindowsImageProcess.StartAsync；不改Core friend API。network controller链接当时生产源，仅普通AC控制的Startup副本将policy1改为0。其构建基线为5bce4cd，后续SID时间加强不在本轮正常网络调用的差异范围。

```powershell
cmake -S .runtime/windows-image/confinement -B .runtime/windows-image/confinement/build -G "Visual Studio 17 2022" -A x64
cmake --build .runtime/windows-image/confinement/build --config Release --parallel 2
dotnet build .runtime/windows-image/crash-parent/CrashParent.csproj -c Release
# ASSETLIBRARY_WINDOWS_IMAGE_PROBE指向上面的本任务native EXE
# ASSETLIBRARY_WINDOWS_IMAGE_PARENT_DLL指向本任务CrashParent.dll
# ASSETLIBRARY_WINDOWS_IMAGE_DOTNET指向精确10.0.111 dotnet
dotnet test tests/dotnet/AssetLibrary.Preview.Tests/AssetLibrary.Preview.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~WindowsComBoundaryTests|FullyQualifiedName~WindowsImageBoundaryTests|FullyQualifiedName~WindowsImageNetworkTests|FullyQualifiedName~WindowsImageResourceTests|FullyQualifiedName~WindowsImageParentExitTests|FullyQualifiedName~WindowsImageIdentityTests"
```

本次构建/格式/最终verify_repository通过（432个C#文件、既有35项迁移/架构回归，Alpha仍blocked）。无重复块门槛放宽。工具目录owner记录最终为0，正常测试沙箱由fixture清理；LAN listener由root独占清理。没有用新claim覆盖原始失败或跨平台缺口。

## Explorer仍独立未完成

用户恢复授权后，第二次实际尝试Ctrl+L又遇GetCursorPos 0x80070005；随后的只读Session2于17:16:27.5947994Z为WTSDisconnected。注册watchdog已结束，显式unregister/verify两键false。未导航、未关闭测试窗口2229954或原窗口1247028，未重启Explorer。授权有效，但需要新的可操作RDP状态；不重复CU或注册来猜测状态。Windows图片进展不等于原生Explorer入口、IPC、G1..G4或整个V0.3完成。
