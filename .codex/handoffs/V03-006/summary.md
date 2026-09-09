# V03-006 — Explorer 入口诊断交接

最新运行状态：[两轮ETW前置准备](explorer-etw-preparations.md)均未提交GUI入口，首轮采集器scope guard结束、第二轮UAC启动被取消（均由主协调报告）。本任务两轮guard正常撤销注册、自有窗口/空目录全部清理，原用户窗口保留；停止自动重试，待用户准备处理系统提示后由协调重新安排。授权持续，不将未导航的准备态计作G1新结果。

最新状态 **partial**，源码提交 `47806297fe6989e3cd04e83e692f78ae7ed4bce2`。本轮[真实入口与根绑定证据](explorer-entry-20260909.md)已覆盖重新可用的桌面：已知 `shell:Desktop` 正控成功，包含虚拟项目的Desktop视图未发现自有项，裸CLSID与完整shell URI均实际报错；注册在导航和报错期间仍有效，所有Explorer模块查询均成功但未见proof DLL。独立进程不预先CoCreate的Desktop根绑定/View正控成功，卸载负控在parse阶段失败。G1..G4仍开放；当前阻断是实际Explorer发现/激活差异，以下Disconnected/Escape记录仅为历史。

本轮只新增test-only Probe根绑定模式与证据，不改DLL、GUID、注册语义、生产接口或依赖方向。复用系统Shell API与既有owner保护注册脚本；无真实资产写入，十秒probe上限。自有注册、模态及窗口已清理，用户原窗口保留。两项新根绑定控制单列，不累计为原四项loader或图片矩阵。后续经主协调批准的官方文件夹CLSID入口对照也未激活扩展，详见下文；不重复策略采样或已通过的图片测试。

## 历史检查点（以下按发生顺序保留）

最新追加[单次静态CRT对照](explorer-mt-control.md)：原源码、原SDK、仅/MT的独立产物构建及root-bind通过，相同真实GUI沙箱入口仍为空目录，无Explorer factory/module。没有改变生产默认CRT或注册语义；600秒guard正常清理，自有窗口/目录撤销。此后停止DLL/注册试探，转由主协调安排所需权限下的精确跟踪。

后续[官方文件夹CLSID入口](explorer-folder-entry.md)已完成单项对照：相同注册/DLL在实际Explorer显示普通空目录，未见Factory或模块加载。注册期间读回有效，600秒guard正常finally卸载；自有窗口与空目录已清理。它没有关闭G1；三类真实入口与独立绑定的差异需要新的精确加载/COM观测。

状态 **partial**。实现提交 `453e10b`，分支 `codex/v03-006-windows-explorer-native-integration`，独立 worktree `C:/Users/Administrator/.codex/worktrees/6f7b/AssetLibrary`，基线 `70ce45c`。本轮交付可复现的加载器诊断，未交付真实 Explorer 入口、AssetHost IPC 或 Windows 安装包；G1..G4 与生产 Shell 保持开放/禁用。

后续按root冻结57d1578和V03-007转交ca1d235接手Windows图片隔离。新[LPAC guard检查点](windows-image-guard.md)实现提交 `a06d3f3`：完成有效访问双控制/真实文件对照，以及无trace生产NativeAOT首张PNG生成；完整拒权/资源/启动取消/清理恢复仍待收尾，Windows预览尚不启用。下述Explorer诊断仍作为独立partial保留，不被图片进展替代。

后续[启动/清理检查点](windows-image-lifecycle.md)为a21a145、53bc455、d152a80，累计八个不同Windows逻辑用例通过，完整原生矩阵仍在继续。[09日Explorer新观测](explorer-20260909-interruption.md)确认会话/输入恢复并创建自有窗口，但导航前被用户Escape主动停止；注册已撤销，CU暂停等待明确恢复，不再将旧Disconnected当当前阻断。

最新[原生矩阵与SID检查点](windows-native-matrix.md)源码c6649c5：12项本机native测试通过（其中两项只代表初始化阻断），LAN6组控制与root服务器回执一致，正creation损坏不再隐藏活worker。负creation等14项回归为5bce4cd。原始失败、nominal CPU周期检查overshoot、普通AC网络拒绝与LPAC早期阻断均分别保留。后续桌面授权已恢复，但第二次实际Ctrl+L又遇输入拒绝，同期Session2再次Disconnected；两键已清理，等待可操作RDP。通用Provider/Explorer与整体版本门禁不关闭，不再扩展本批工作范围。

## 新证据与判断

旧 DLL SHA256 `0174db9b1b4ccd4925d3a28470930fa6faebd4070348cc374a7cb87313e2fd15` 与 V03-002 记录一致。实际导入包括 MSVCP140、VCRUNTIME140、VCRUNTIME140_1、UCRT 和系统 COM/Shell DLL。

新增静态 CRT 的 ExplorerLoaderProbe，自身实际只导入 ole32/KERNEL32。在 COM 初始化后、加载被测 DLL 前确认三项动态 CRT 未预载。对原 DLL 的本 worktree 校验副本，在空工作目录下分别使用继承 PATH、只有 System32 的 PATH、限制为 DLL 目录和 System32 的搜索；三次 LoadLibrary、DllGetClassObject、CreateInstance 均成功，三项 CRT 均确实来自 System32。不存在 DLL 的负向控制返回 Win32 126。原产物 hash 不变，四场景原始输出见 `loader-evidence.json`。

当前机器上“旧 DLL 缺少 CRT／必须依赖开发 PATH 才能加载”未获证据支持，没有为试错改 Shell 的 CRT、注册或接口。未读取长驻 Explorer 的完整 PATH/环境块；严格搜索实验是对开发 PATH 必要性的独立检验，不能冒充目标 Explorer 的 loader trace。

当前命令进程与四个 Explorer 均在 Session 2，完整性 RID 8192（Medium）；ExtensionPointDisable、Signature、ImageLoad mitigation flags 均为0，只排除已查询的显式进程限制。CodeIntegrity 自16:00起最多读取最近200项，无 proof 名称匹配，查询存在截断；同期 AppLocker EXE/DLL 与 Application/SideBySide 返回无匹配事件。不能据此排除所有系统策略，见 `environment-evidence.json`。

computer-use 重新列举时只能得到 Codex 窗口；启动 Explorer 的受支持 API 仍返回 GetCursorPos `0x80070005`。随后只读窗口列表仍没有可定位 Explorer，已停止输入。无截图/实际视图新证据，没有重试 CLSID 入口。权限调整或桌面解锁不等于加载根因解决；旧任务窗口交主协调按新鲜所有权证据处理，未使用旧句柄关闭任何窗口。

## 官方样例对照

- Microsoft [Dll.cpp](https://github.com/microsoft/Windows-classic-samples/blob/main/Samples/Win7Samples/winui/shell/shellextensibility/explorerdataprovider/Dll.cpp) 使用 HKCU Classes、Apartment、DWORD ShellFolder Attributes 与挂载父容器更新；与本 proof 的基本注册模式一致。样例挂载 MyComputer、本 proof 挂载 Desktop，没有从差异推断根因或重复注册试验。
- [样例工程](https://github.com/microsoft/Windows-classic-samples/blob/main/Samples/Win7Samples/winui/shell/shellextensibility/explorerdataprovider/ExplorerDataProvider.vcproj) 的旧 VC9 Release 配置写 RuntimeLibrary=0，不是当前 Shell 必须改变 CRT 的证据。
- [ExplorerDataProvider.cpp](https://github.com/microsoft/Windows-classic-samples/blob/main/Samples/Win7Samples/winui/shell/shellextensibility/explorerdataprovider/ExplorerDataProvider.cpp) 使用系统 DefView，并为子项提供关联对象、默认菜单和完整 parsing name。本 proof 缺这些子项行为，属于激活后的已知实现缺口；不能解释此前未观察到类工厂调用的事实。

官方来源于2026-09-08在线只读核对；API获取固定revision遇TLS连接重置，随后使用可访问的官方raw main，未取得commit pin。不宣称官方样例已在本机编译/注册/运行。

## 边界、验证与合并

只改 tests/windows-shell 四个文件及本任务包/交接。复用 Win32 loader/COM、旧不可变 proof、既有 CMake/MSVC；无新主语言、框架、包、契约、SDK、数据库或业务逻辑。静态 CRT 只用于测试观察器。没有资产访问、网络业务、HKLM/UAC/安全策略修改、Explorer重启、NAS部署或GUID注册写入；最终注册读回ClassPresent/NamespacePresent均false。

每个诊断子进程10秒上限；trace仍是原proof的1MiB上限，只写本任务.runtime副本旁。诊断进程均退出，诊断目录保留供复核；无50万资产/实际Explorer延迟或稳定性结论。

四项加载回归通过，桌面可操作性场景拒绝访问失败1。严格目标构建、导入表及仓库检查见tests.md。本补丁可独立合并为诊断工具，不依赖预览契约冻结；合并不解除任何产品门禁。

下一步由主协调恢复可用的受支持桌面操作后，先建立实际目标Explorer的loader/COM入口观测，再做一次带观测的自有GUID注册与导航，停止无观测注册轮询。取得真实DefView后才推进子项行为、进程外有界快照IPC和G2..G4。缺口归属V03-006/windows-shell-owner；生产apps/windows-shell仍需主协调按G1..G4裁决。

## 主协调追加的只读环境判断

2026-09-08T12:29:00Z，补查原始结果见 `shell-policy-evidence.json`。四个Explorer、命令观察器和原加载观察器均 `TokenIsAppContainer=0`，`GetPackageFullName` 返回15700（APPMODEL_ERROR_NO_PACKAGE），没有观察到AppContainer或包身份差异。[包身份API](https://learn.microsoft.com/en-us/windows/win32/api/appmodel/nf-appmodel-getpackagefullname)

六个被观察进程同属Session2，其 `WTSConnectState=4`，即WTSDisconnected。该状态表示会话仍存在但客户端已断开，是当前桌面交互前置条件不足的新证据；不能据此定位历史上真实Explorer在类工厂前报无关联应用的原因。[会话状态定义](https://learn.microsoft.com/en-us/windows/win32/api/wtsapi32/ne-wtsapi32-wts_connectstate_class)

读取HKCU/HKLM、Registry64/Registry32四种组合中的精确值：`Software\Microsoft\Windows\CurrentVersion\Policies\Explorer` 与 `Software\Policies\Microsoft\Windows\Explorer` 均未设置EnforceShellExtensionSecurity；`Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved` 和Blocked中均无本任务CLSID值。原始记录区分key_absent/value_absent。未观察到这些显式策略，但Approved缺失本身不能证明被阻止，也不能把缺失策略值扩张为全部Shell策略允许。

为准确读取加载观察器本身的创建身份，使用原哈希EXE创建一个隐藏、挂起的自有进程，仅查询身份与会话，从未恢复其主线程执行；随后终止并在3秒内确认退出、关闭句柄，EXE哈希不变。没有加载DLL、COM调用、GUI/CLSID重试、Explorer变更、注册键写入或全量日志读取。没有修改测试源代码，不重跑原成功构建/加载矩阵；仅补证据及交接校验，状态继续partial。

## 协助V03-007的静态审查

主协调另委派的[Windows解码启动静态复核](v03-007-windows-startup-static-review.md)已独立记录五个源码快照hash、官方依据和最小验证建议。没有启动AppContainer或安全探针、修改V03-007源代码，也未证实203根因。该文档不增加原loader测试计数；Explorer继续partial等待桌面恢复。
