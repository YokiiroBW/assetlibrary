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

## 实际命令

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
