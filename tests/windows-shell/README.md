# Explorer 原生入口验证

V03-002 的 test-only C++17/Windows SDK 10.0.26100 验证，复用系统 DefView。只有当前用户独占 CLSID `{4FF8301D-2E73-4D49-9FE5-868D5F1EA302}`；拒绝已有注册，不修改 HKLM/UAC/系统策略，不重启 Explorer。代码无网络、资产 I/O、数据库、Provider 和 WinUI 运行时。

## 当前只读快照实现（2026-09-12）

当前 DLL 已移除固定示例条目，按冻结的 `contracts/windows-shell/read-only-snapshot-v1.md` 从同用户/同会话的进程外 Host 读取一页。每页最多 100 条资产/目录与一个“下一页（导航）”；资源库、普通目录和下一页可进入，文件、链接项目及固定错误状态不可进入。缺少 Host、正在加载、权限拒绝、过期、协议错误和繁忙显示带 F5 指引的状态行；Ready 的零条目才表示真实空页。F5/重开刷新，不宣称推送失效。

每次 EnumObjects 共用一个 150ms 单调等待预算，不等待 EOF；四个在途/待回收名额，取消后保留 OVERLAPPED、缓冲、句柄与 DLL 引用到完成。连接后核实际 TokenUser SID、session 与持有的服务进程身份；客户端只能被识别，不能被 Host 冒用身份。名称/类型/排序/解析从严格校验的私有 PIDL 完成，不做逐条 IPC。解析名称用有界的 `snapshot-pidl-v1:<完整私有PIDL hex>`，支持新实例、多层相对及完整名称还原；hex 中仍只有 epoch/token/kind/展示名，不包含 Core 路径或凭据。

无需注册、无需 GUI 的实际检查命令：

```powershell
cmake -S tests/windows-shell -B .runtime/explorer-snapshot -G "Visual Studio 17 2022" -A x64
cmake --build .runtime/explorer-snapshot --config Release --parallel 2
ctest --test-dir .runtime/explorer-snapshot -C Release --output-on-failure
```

CTest 有 30 秒外限，包含三个独立固定字节向量、畸形帧/UTF-16/分页边界、PIDL、真实本地管道、累计超时、取消、四名额，以及不注册的 COM/独立 probe 测试。Mock 独占冻结端点，拒绝覆盖已有 Host；执行前由协调者保留该端点。Mock 等待客户端关闭再断开，避免丢弃未读缓冲；再次枚举前等待 mock 恢复监听，不掩盖生产 Busy 状态。

与已启动的真实 Host 联调：

```powershell
& .runtime/explorer-snapshot/Release/ExplorerSnapshotProbe.exe --dll (Resolve-Path .runtime/explorer-snapshot/Release/AssetLibraryExplorerProof.dll).Path --budget-ms 8000 --require-navigation
```

probe 直接 LoadLibrary/factory/固定空根 PIDL，不注册、不调用桌面或打开窗口。全程共享可选 150..30000ms 重试预算（默认 8000），Loading/Unavailable/Busy 每 250ms 重试；每次 Shell 请求仍保持 150ms。`--require-navigation` 适用于同时含库、目录与下一页的合成联调样例；一般数据省略此参数。`--once` 只读取根一页，Host 缺失等固定状态返回 exit 2，真正成功返回 0，探针错误返回 1。JSON 行仅输出阶段、状态、种类和转义展示名；不输出 token、路径、服务地址或凭据。不要将真实资产名称日志纳入公共测试夹具。

旧 registration/verify 仍保留包外来源准入与 owner 清理；旧 probe 改为验证只读页，允许无 Host 时的固定状态，不再要求示例库。以下历史原生证据只适用于其记录的旧 DLL；新 DLL 的真实 HTTPS/Core 与原生 Explorer 验收由协调任务执行，COM 通过不能关闭 G1..G4。

## G2 故障 CLI（V03-012，test-only）

`ExplorerFaultHost` 是独立故障进程，只使用冻结的 SID/session pipe；不读取 profile、资产或网络，不注册、不操作 Explorer。没有 `--execute` 只输出 `dry_run`，执行时必须显式指定 `--mode`。实际 TokenUser SID 独占 DACL、拒绝远程、检查客户端 session/PID、first-instance 拒绝占用已有 Host；同一进程持有一个 pipe 实例直到退出。不得同时运行真实 AssetHost、SnapshotTests 或另一份故障 CLI。

```powershell
cmake -S tests/windows-shell -B .runtime/explorer-fault-harness -G "Visual Studio 17 2022" -A x64
cmake --build .runtime/explorer-fault-harness --config Release --parallel 2
ctest --test-dir .runtime/explorer-fault-harness -C Release --output-on-failure -R '^explorer_(snapshot|fault_harness)$'

& .runtime/explorer-fault-harness/Release/ExplorerFaultHost.exe --mode crash
& .runtime/explorer-fault-harness/Release/ExplorerFaultHost.exe --execute --mode silent --lifetime-ms 30000 --max-connections 32
```

`--lifetime-ms` 默认 30000、范围 1..119000；最多另用 1000ms 确认取消，总驻留上限 120 秒。独立监护线程也约束日志接收端阻塞或异常清理。`--max-connections` 默认32、范围1..128，包含不完整/拒绝请求；并发固定1，每连接总预算500ms。连接上限、期限或 stop-event 触发后退出并释放端点；没有自动重试或常驻。正常停止取消并确认当前 OVERLAPPED 完成后释放存储；无法确认时仅终止自有故障进程。

| mode | 实际注入 | 现有 Shell 客户端状态 |
|---|---|---|
| `silent` | 读完48字节请求后不响应，等待客户端关闭/连接期限 | `Unavailable` (2)，约150ms |
| `invalid-version` | 回显真实 request-id，响应头 version=2 | `InvalidResponse` (5) |
| `partial-frame` | 声明24字节body，只发16字节header和4字节status | `Unavailable` (2)，约150ms |
| `crash` | 读完合法请求后以 `0xE0000012` 终止当前故障进程，不弹 WER | `Unavailable` (2) |
| `ready` | 新随机epoch、零条目合成根；任意非根返回Expired | `Ready` (0)，仅用于工具正控 |

`crash` 是有意自终止的异常退出模型，不能据此声称已触发真实 AssetHost 的任意内部崩溃路径。其它模式保持进程存活并有界观察客户端关闭，避免 `DisconnectNamedPipe` 丢弃尚未读出的响应。一次在途连接时其它调用可能立即得到 `Busy` (6)；等新 `listening` 再操作。CLI stdout 是限量 JSONL：mode、pid、client_pid、requests、elapsed_ms（进程内单调时钟）、response_bytes 和event；不记录request-id、请求正文、SID、名称、路径或凭据。`response_written` 只在响应完整写入管道后记录20/40字节；失败为 `response_failed`，其它事件的response_bytes为0，写入完成不代表客户端已读取。`client_pid=0` 表示尚未关联客户端，只有成功核session/PID后读取的请求才计数。`request_received`/`expected_crash` 的 client_pid 必须与已核验目标 Explorer 进程匹配，不能凭同时段请求归因。

退出码：0=惰性或正常 `stopped`/`deadline`/`connection_limit`；1=初始化/I/O失败；2=参数拒绝；3=`pipe_unavailable`（已有端点或拒绝创建，不抢占）；70=取消或监护期限，可能无末尾日志；`0xE0000012`=预期故障自终止（PowerShell有符号值 -536870894）。没有 `listening` 不能视为已就绪。

协调者的真实 GUI 周期：先正常停止其拥有的真实 Host，确认端点释放，再后台启动故障 CLI；等待该进程的 `listening` 后，才在已核验的自有 Explorer 窗口 F5/重开。不要用 probe 抢走 `crash` 的首请求。下面是启动/停止片段；GUI动作由协调者在就绪后执行，非自动化桌面授权：

```powershell
$faultExe = (Resolve-Path .runtime/explorer-fault-harness/Release/ExplorerFaultHost.exe).Path
$faultLog = Join-Path (Split-Path $faultExe) ('fault-' + [guid]::NewGuid().ToString('N') + '.jsonl')
$faultErr = $faultLog + '.stderr'
$faultProcess = Start-Process -FilePath $faultExe -ArgumentList '--execute --mode silent --lifetime-ms 30000 --max-connections 32' -PassThru -WindowStyle Hidden -RedirectStandardOutput $faultLog -RedirectStandardError $faultErr
try {
    $faultWait = [Diagnostics.Stopwatch]::StartNew()
    do {
        if ($faultProcess.HasExited) { throw 'Fault CLI exited before readiness; inspect its JSONL/exit code.' }
        $faultReady = (Get-Content -Raw -LiteralPath $faultLog) -match '"event":"listening"'
        if (!$faultReady) { Start-Sleep -Milliseconds 50 }
    } while (!$faultReady -and $faultWait.ElapsedMilliseconds -lt 5000)
    if (!$faultReady) { throw 'Fault CLI readiness timeout.' }
    # 此处由协调者完成真实窗口操作、记录状态和目标 Explorer PID。
} finally {
    if (!$faultProcess.HasExited) {
        $faultStop = [Threading.EventWaitHandle]::OpenExisting('Local\AssetLibrary.ExplorerFault.Stop.' + $faultProcess.Id)
        try { $null = $faultStop.Set() } finally { $faultStop.Dispose() }
        if (!$faultProcess.WaitForExit(2500)) { throw 'Fault CLI stop timed out; retain evidence and await its bounded watchdog.' }
    }
}
```

记录故障状态、可交互性与请求PID后，正常 stop 或等待期限/预期crash，确认进程退出及端点释放，再启动原真实 Host、重新授权并重开根/F5 验证恢复。新Host不能复用旧epoch/node；只有真实Core内容和真实Explorer窗口证据能验证最终恢复。

`explorer_fault_harness` 实际启动该 CLI，验证惰性/参数边界、端点拒占用、实际TokenUser DACL、真实请求PID、四模式状态和150ms边界、正常停止（含未完成请求）、期限/连接数退出、同进程再次请求和新Host重新占用。其30秒CTest外限以及成功结果不代替真实 Explorer G2。不同SID/session真实客户端、远程连接、真实GUI故障周期、G3保留对象/取消增长与G4循环/8小时证据仍需独立验收；G2不要求所有COM对象即时释放。

## 默认打开与图标

单个资源库、目录、下一页提供唯一默认“打开”（双击/Enter），通过站点浏览器在当前窗口导航。文件、链接、状态、背景与多选不提供打开菜单；不增加写入、复制、新窗口或文件关联。图标来自Windows系统文件夹/通用文档资源，无资产访问或媒体解码。

```powershell
ctest --test-dir .runtime/explorer-snapshot -C Release --output-on-failure -R '^explorer_navigation_menu$'
```

此测试实际调用DLL菜单/图标COM接口，用无窗口浏览器服务记录器，不显示菜单、不连接pipe或注册，10秒外限。实际双击和图标仍需协调者新DLL原生周期验收。

## Loading 自动刷新与生命周期边界

窗口与站点就绪后，每view启动一次500ms观察，固定10秒/最多20次、最多4个活动名额。通过当前活动IShellView核对窗口，再读取IFolderView的条目数（上限101）和首项私有PIDL；只有实际显示的Loading状态行允许Refresh。普通项和错误状态停止；首次枚举尚无条目时只观察，不触发查询或声称Core成功。枚举副本Signal仅用于诊断，不控制刷新。

持续Loading不延长预算；站点变化/清空或窗口销毁停止。停止后F5仍是单次查询，重开可获得新周期；Core五秒有效期及单次150ms IPC不变。确认Detach即归还名额，callback/DLL保活则跟随真实COM引用，不能把关窗等同于对象全部释放。

`explorer_loading_refresh`检查实际条目状态、Signal矛盾、空视图期限、畸形PIDL、预算、代次、重入、owner线程、独立view及名额复用；包含真实十秒期限负控，外限60秒。`explorer_loading_view_wiring`使用真实Desktop owner检查系统自动site/window回调与活动view记录器，不代表proof数据验收。

严格proof-owner卸载诊断独立运行，当前仍返回exit1、保留callback1/DLL S_FALSE：

```powershell
& .runtime/explorer-snapshot/Release/ExplorerLoadingRefreshTests.exe (Resolve-Path .runtime/explorer-snapshot/Release/AssetLibraryExplorerProof.dll).Path --proof-owner-lifetime
```

该失败保留，直接加载的测试模块在COM清理后仍有外部持有时保持pin至进程退出。四项限定CTest通过不代表此卸载诊断或G2通过。详见[本轮范围与反例](../../.codex/handoffs/V03-006/loading-refresh.md)。

## 实际命令

临时只读诊断键 `{2F242D38-C686-4E35-87C3-36C9BAF44EFE}, pid=1` 通过空item的GetDetailsEx返回≤2048字符VT_BSTR。它不注册属性、不触发枚举/IPC/刷新；读取者必须先核JSON执行pid等于实际Explorer目标。字段、UInt64解释及因果边界见[诊断协议](../../.codex/handoffs/V03-006/probe-diagnostics.md)。诊断只为定位冷Loading，不代表已修复。

```powershell
cmake -S tests/windows-shell -B .runtime/explorer-proof -G "Visual Studio 17 2022" -A x64
cmake --build .runtime/explorer-proof --config Release --parallel 2
pwsh -NoProfile -File tests/windows-shell/verify.ps1 -BuildDirectory .runtime/explorer-proof
```

`verify.ps1`及`registration.ps1 -Action register`现在必须由经核验的普通用户系统Explorer直接启动的PowerShell进程执行。上面的verify命令是该包外进程中的命令，不可从包内终端直接运行并据其成功认定系统注册。先运行不登记的回归：

```powershell
pwsh -NoProfile -File tests/windows-shell/test-registration-context.ps1
```

测试使用合成来源证据检查允许/拒绝与presence/absence报告边界，并只对当前测试进程读取令牌，验证成员查询所需访问权限；不查询其他进程、不通知或注册。当前CI的Shell入口仍仅构建C++；测试注册不是未授权的CI桌面动作。

独立probe的原始stdout/stderr和exit/timeout结果先写入本次build目录的`verify-evidence/<新GUID>/`，再判断成功与失败。超时也保留此前已收到的字节；目录不复用。查看错误中给出的evidence位置，不能把缺少控制台输出当作probe没有执行。

### 注册表视图与启动路线

MSIX包内工具可能在私有注册表视图中成功写入HKCU，普通Explorer却看不到。没有包身份、同SID、同session或局部字段匹配都不能单独证明视图一致。测试入口用直接父进程的系统Explorer路径、创建先后、同用户/会话及普通权限检查选择已实测支持的启动路线，不依赖本机Silo、包名称/版本或更改manifest、UAC、系统策略。没有包身份只是必要检查之一，不是通用虚拟化探测器。

由协调者先观测一个新的系统Explorer窗口并核PID/creation/HWND，再通过该窗口的`ShellFolderView.Application.ShellExecute`启动固定PowerShell脚本，是本次采用的Windows支持方法；不要用包内`Process.Start`启动的新PowerShell替代。引用已观测窗口的`Document.Application`，不另创建一个未验证来源的Shell执行对象。完整精确方案见[本次交接](../../.codex/handoffs/V03-006/proof-native-registration-plan.md)。

`registration.ps1 -Action verify`仍可只读任意当前视图，结果含`RegistryView=current-process`和分开的`NativeLaunchRouteVerified`。`SystemExplorerRegistrationVerified`与`SystemExplorerCleanupVerified`始终为false，实际Explorer的类、PIDL、内容及包外清理证据要另取。`unregister`保留owner保护的当前视图清理，避免父进程退出使恢复被卡住；包内退出0或absence不能宣称系统清理，必须在相同已核包外执行上下文清理/复核。

参考：[Flexible virtualization](https://learn.microsoft.com/en-us/windows/msix/desktop/flexible-virtualization)、[MSIX运行时排查与Explorer边界](https://learn.microsoft.com/en-us/windows/msix/manage/troubleshoot-msix-container)、[通过已有Explorer执行](https://devblogs.microsoft.com/oldnewthing/20131118-00/?p=2643)。这些是测试工具的启动约束，不是产品安装器，也不自动证明G1通过。

构建启用 `/W4 /WX /permissive- /analyze /utf-8`。verify 在 finally 中卸载；隔离子进程最多10秒，校验 COM factory、PIDL、Desktop 枚举/属性/Folder 关联、DefView、子目录 bind、重复注册拒绝和最终无残留。它不替代 Explorer G1..G4。

交互验证必须由协调线程单独启用，限定注册/卸载和窗口所有权；`ExplorerProofProbe --navigate` 只通过 ShellWindows 前后差分定位本次新开的 Desktop 窗口，给 IWebBrowser2.Navigate2 传 PIDL SAFEARRAY。此 COM 调用可能被 Explorer 模态错误阻塞，外层必须设置进程超时和注册清理，不可直接无界运行。不得关闭用户已有窗口。任务曾观察到 UI 输入/截图拒绝，不能据此改变桌面安全状态。

## 当前证据

2026-09-11自身proof已完成一个包外普通用户最小闭环：原0174 DLL、原2HKCU根/字段，从Desktop正常进入后实际class4FF、22B根PIDL匹配、1项“示例资源库”；同进程注销和另包外9字段缺失、Desktop入口消失均确认。见[实际原件与边界](../../.codex/handoffs/V03-006/proof-native-entry.md)。这不是AssetHost/真实Core接线或G2..G4完成，测试文件夹不代表可用Windows客户端。

2026-09-11：主协调完成微软样例的包内/包外A/B与恢复。原包内自检20字段正确，而同用户/会话普通进程读回全部缺失；原样guard从已观察Explorer执行后，真实Explorer进入BA16类/42B PIDL/10项，原样清理后字段缺失和样例项消失。历史失败保留，下述“原因未定位”描述的是当时状态。微软样例通过不能直接晋级本项目test-only proof或G1..G4；本项目注册路线与独立测试需按新边界重验。

2026-09-08：隔离探针和可逆 HKCU 注册通过；真实 Explorer 的 CLSID、选择 API 和直接 Navigate2 入口均未证明加载本类，显示“无关联应用”。有界调用日志仅记录隔离探针；parent Desktop UPDATEDIR、正确 DWORD Folder 属性和成功 Folder open association 未消除故障。原因尚未定位，不擅自归因于某个系统设置，不关闭 G1。G2 故障生命周期、G3 取消/延迟、G4 20轮和8小时稳定性尚未获得真实视图证据。

历史 DLL 的 `proof-calls.log` 为旧验证诊断；当前快照 DLL 已移除此同步文件写入，避免在原生前台路径增加无界文件 I/O。新检查使用独立探针输出。

参考：[Microsoft NSE implementation](https://learn.microsoft.com/en-us/windows/win32/shell/nse-implement)、[ExplorerDataProvider sample](https://github.com/microsoft/Windows-classic-samples/tree/main/Samples/Win7Samples/winui/shell/shellextensibility/explorerdataprovider)、[Shell notifications](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shchangenotify)。

## V03-006 加载器诊断

`ExplorerLoaderProbe` 使用静态 CRT，启动时确认被测动态 CRT 尚未加载；不注册 COM，直接加载
指定 proof DLL 并调用其类工厂。此观察器不改变 Shell DLL 的 CRT 或生产依赖决策。

```powershell
cmake --build .runtime/explorer-proof --config Release --target ExplorerLoaderProbe --parallel 2
pwsh -NoProfile -File tests/windows-shell/diagnose-loader.ps1 -BuildDirectory .runtime/explorer-proof -DllPath <existing-proof-dll>
```

脚本把被测 DLL 复制到本构建目录中的独立诊断目录并核对 SHA256，避免 proof trace 修改旧任务目录。
各探针进程使用空工作目录、10 秒上限，分别验证继承 PATH、仅 System32 PATH、限制 DLL 搜索目录，
以及不存在 DLL 的失败控制。输出运行库是否来自 System32、实际加载和类工厂结果；不打印 PATH 或凭据。
搜索受限的成功只能说明当前机器上的该 DLL 无需开发 PATH，不能替代真实 Explorer 进程的加载证据，
也不能排除系统完整性策略、桌面/会话或入口层面的其他阻断。诊断文件保留在 `.runtime` 供复核。

## V03-006 Desktop 根绑定对照

`ExplorerProofProbe --root-bind` 在独立STA进程中依次调用SHParseDisplayName、SHGetDesktopFolder、
Desktop.BindToObject和CreateViewObject，不预先CoCreateInstance。此模式逐阶段无缓冲输出HRESULT；
parse失败/空PIDL不继续绑定，空view不能报告成功。使用原owner保护注册脚本、十秒外部进程期限和finally卸载，
不可把裸命令当作有超时保证的完整测试入口。

2026-09-09相同不可变DLL注册正控四阶段S_OK、exit0；卸载负控parse=80070057、PIDL absent、exit1。
[实际GUI与根绑定证据](../../.codex/handoffs/V03-006/explorer-entry-20260909.md)仍显示Desktop发现和shell URI失败；
[官方文件夹CLSID入口](../../.codex/handoffs/V03-006/explorer-folder-entry.md)仅显示普通空目录。
三类实际入口均未观察到Explorer加载proof DLL，独立根绑定/View成功不能计作真实G1。
