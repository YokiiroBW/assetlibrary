# V03-006 — Explorer 入口诊断交接

最新：[只读快照 IPC](snapshot-ipc.md)实现与独立严格构建/协议/管道/COM/公开probe检查通过，fresh/nested canonical及类型排序已修正。默认pipe交回root，等待真实Host/Core与新DLL原生视图验收；本轮无注册/GUI，不关闭G1..G4。

最新：[自有4FF原生最小闭环](proof-native-entry.md)通过：verify-02真实包外来源/COM正控成功；原2HKCU/0174 DLL在600秒guard内，从Desktop一次正常双击进入实际4FF/22B相等/1项“示例资源库”。同guard18328清理、另包外9missing和虚拟Desktop00021400/2B/32项入口消失通过；目标与broker自有窗口均关闭、模块未留在两驻留进程，无原desktop重启。GUI权已还root。此为test-only最小入口/卸载闭环，Host/真实Core只读接入及G2..G4仍缺，未宣布Windows首版完成。

包外proof首次verify在登记前因self token成员查询SecurityException拒绝，原件保留。已由只读8/10权限对照确认仅self需QUERY|DUPLICATE，parent保持QUERY，修后当前包内上下文查询成功但仍因父来源拒绝，准入不降级。probe失败/超时原始流和exit先落盘；runtime guard计时前移至register调用前，并加包外只读HKLM2根冲突前置。新verify标签待审后执行，当前无新增登记/GUI guard。

最新：[本项目注册入口修正与下一实机计划](proof-native-registration-plan.md)已落代码：包内局部读回不再晋级system Explorer注册/清理，register与verify在写入前要求已核普通系统Explorer直接父路线；NoPackage本身不放行。owner当前视图cleanup保留，通知补STA/COM，CLI说明同步。19上下文/报告案例、2接线检查和帮助类型编译、17 Shell契约及仓库相关检查通过；本轮无GUI/注册/通知/重启，root仍是唯一GUI操作者。微软样例恢复仅解除环境阻断，本项目G1..G4待包外实机。

最新：[异常V2到期交接](key-open-v2-expiry.md)新registered normal/cancel各32真C++自行捕获、total33/详16、status0/句柄close/清理均通过；实机调用发生在600秒自然到期后被拒绝，observer/额外notify/ThisPC/Browse均未启动。8根双通知/20missing与专属SDK窗清理确认，原PID已不存在。按root排他协调交接后停止GUI/登记/实机，保留代码owner；脚本、SDK创建方法和接管边界已列明。

最新：[公开开键首轮](key-open-first.md)registered normal/cancel均status0/非空可读句柄/参考自行close及清理通过。真实SDK窗口准入和SHCORE→KernelBase实际IAT验证S_OK，但ready后5.609秒原异常保护结束，工具返回时已脱离，额外notify/首次ThisPC/Browse均未执行，0匹配无开键结论。有效20字段再次通过，SDK窗/8根双通知/最终20missing清理完成，用户窗口保留；不放宽预算或盲重跑。

最新：[RegOpen触发流程准备](notify-trigger-plan.md)已写notify-only薄包装，仅AST提取/核对323 guard原C#文字块，未来只调用一次原样STA双通知对。流程收敛SDK17→observer ready→通知成功→首次ThisPC及正控→一次Browse，采集日志须持续排空。旧capture未改、包装未执行、未编译提取C#、无notify/注册/GUI/attach；等待新公开API候选与阶段driver合并。

最新：[V2消费点实机](cache-consumption-v2.md)完成同周期normal/cancel语义及清理通过后的一次Explorer对照。参考各2组为flags1/A0000020/0/0，真实4组为0/0/0/0，外部字段前后20/20仍正确；入口仍无关联应用/ThisPC2项。59.438秒异常保护早停，6断点/Detach/目标存活清理成功；最后双通知/8根/20missing与专属UI清理，用户下载窗保留，无原Explorer重启。该消费状态差异不直接等于注册API失败，G1仍partial。

最新：[首轮cache参考](cache-reference-first.md)在原样注册内完成字段20/20前后读回与PIDL S_OK/22B；normal实际两组嵌套cache快照均flags1/A0000020/0/0、outer返回20000000，但候选要求cacheSnapshots==1导致参考失败。4断点/Detach/心跳/协作退出及最终8根双通知清理确认；cancel/实机/GUI/restart未执行，等待root修复冻结版本，不绕过失败门禁。

最新：[固定11字段只读工具](registry-field-readback.md)已准备，C#编译/PS AST及未登记expected-missing通过：11官方+4Owner+5外部HKCR记录均missing-key/OpenStatus2，未调用guard/注册。DWORD规范化、类型/存在/实际值分开，无默认值替代；已登记值与其他负路径待后续周期验证。无GUI/attach/重启，待新observer准备合并。

方案增补：已核root离线PDB的5个准确签名；ThisPC/F5可能先填cache，单盯_LoadValues容易零命中。唯一观察点优先考虑可覆盖cache-hit的消费/合并位置，具体仍待静态数据流冻结；void、DWORD与HRESULT/LSTATUS严格分开，不解码猜测私有结构。原始离线签名输出已保存，无实机操作。

最新：[真实注册读取只读准备](registry-read-observation-plan.md)收敛为matched GetAttributesOf内一个准确读取边界，与outer callId配对；具体API/RVA等待root固定模块离线分析。已区分DbgEng与OS线程ID、异常/线程退出归属风险，并发现旧guard的presence/常量Attributes摘要及单向Assert-Tree不能证明11字段持续完整或host实际读值。下个获准周期补固定11字段只读读回，原guard不改；本轮无GUI/注册/attach/restart。

最新：[冷启动实机对照](cold-session-execution.md)完成授权的两次当前用户Explorer重启，22/22及2/2固定旧目标退出，Windows自动恢复38388再32044且helper退出后持续存活。实际入口仍无关联应用/ThisPC2项；4属性返回仍0/0/0/26，B41早停28.485秒但5断点/Detach/目标存活清理成功。双通知/8根/测试UI及最终Shell模块清理全部确认；不追加变体，下一步仅评估真实CLSID/ShellFolder属性读取路径，G1未关闭。

最新：[当前用户Shell冷启动准备](cold-session-preflight.md)已编译/只读预检；用户授权已收到，但WTS Session2=4/Disconnected且输入桌面查询Win32 5，未注册/未stop/未GUI输入。受限工具持有精确身份句柄、固定集合/桌面最后、逐目标活动桌面复核与partial finally退出核验已准备。等待用户重连后全新preflight，不复用旧PID，G1未关闭。

最新：[双模式同条件属性对照](attribute-pidl-control.md)已完成，A20/B26次全部S_OK；枚举/解析child完整22B相等且所有前后SHA不变，两模式及双查询顺序结果一致。20000000→20000000、2044007F→20000024，与旧host差异仍在；未推定根因或扩展变体。原guard双通知/8根及两probe清理确认，GUI0/附加0。

恢复复核：[属性报告](attributes-debug-control.md)补充PIDL来源与请求mask差异，独立正控和host结果尚不是同输入对照；已向root提交下一项同对象对照建议，未执行新现场操作。78c0a23的27份原件工作区/HEAD哈希一致，G1仍partial。

最新：[单GetAttributesOf实机](attributes-debug-control.md)取得4对有效输入/输出。call1请求FOLDER→S_OK/0，call4含FOLDER→S_OK/26；call2/3未请求FOLDER不作非folder推断。捕获约33秒提前结束但5断点清零/Detach/存活及注册/UI清理成功。未改mask、接口或注册，根因待root结合真实属性路径裁决。

最新：[完整请求IID元数据](bind-iid-control.md)已补齐，callId1为BC110B6D…/无pbc→80070490/null，callId2为886D8EEB…/有pbc→80004002/null；与旧类别轮一致。仅记录已匹配数据，具体接口由root按主源解释。捕获异常保护提前结束但两断点/Detach/存活清理成功，双通知/四根/自有UI已清理，G1仍partial。

最新：[单绑定实机观测](bind-debug-control.md)取得两对官方PIDL匹配Bind调用，other IID分别在无/有pbc时返回80070490/80004002及null接口。约34秒callback异常保护提前结束，非完整60秒；不能把other直接解释成必需folder接口或认定pbc根因。两断点清零/Detach/目标存活无debugger成功，注册和自有UI已清理，G1仍未关闭。

最新：[ASSOCCHANGED完整通知对照](assoc-notify-control.md)已在官方四根有效期内执行，原字段/F298/STA/600秒不变，注册和cleanup两通知均实际完成，但ThisPC仍缺官方项、唯一Browse仍无关联模态/超时，实际view仍ThisPC。现场已清理；不将void返回当所有缓存处理证明，不改写旧proof已有ASSOC调用事实，G1仍partial。

最新：[v2目标内受限观察](target-debug-bounded.md)已真实附加并在ready后立即触发一次Browse，但约31秒达到4096入口上限，目标三API匹配为0；不是完整60秒或完全未激活证明。断点清零、Detach、存活无debugger均成功；注册和自有UI已清理。原v1准入失败保留，不扩大上限或重复场景，后续由root选择前置Shell路径检查。

最新：[目标内观察器首次准入](target-debug-admission.md)在CheckRemoteDebuggerPresent检查处拒绝，未附加/ready/Browse，属于工具句柄权限缺陷而非产品激活结果。root已独立确认查询权利差异，旧工具冻结等待v2。自有窗口与注册正常清理，原窗口/Chrome保留。

最新：[注册先于新Explorer的完整控制](register-first-control.md)已完成，02:25:12注册早于新PID24488创建02:25:32，SDK/ThisPC正控均通过，但官方仍缺项、唯一打开无关联模态/超时，实际view仍ThisPC。有效期内模块读回未见样例，现场正常清理，原窗口与Chrome保留。仅此时序改变不足以修复入口，不声称broker冷启动；旧未完成项与父级发现结果继续分别保留。

恢复检查点：[独立父级发现](parent-discovery-control.md)已证明含/不含hidden都3项含官方，匹配项不HIDDEN/NONENUMERATED，独立绑定正控成功。旧“先注册后新进程”只完成SDK正控，ThisPC/目标步骤因额度中断未发生，不能计失败；旧注册已到期清理，旧自有窗在新鲜身份核对后关闭。接下来只完成此唯一未完控制，既有E0/Browse/官方两轮保持原证据。

最新：[正确STA通知后的官方完整控制](official-sta-runtime-control.md)仍未发现/打开样例；一次原生打开在“无关联应用”模态阻塞到10秒外限，实际view仍ThisPC/2项，目标未观察到样例模块。F298原DLL与11原字段不变，通知及清理均真实成功，现场已清理。此前[通知未执行那轮](official-runtime-control.md)独立保留，不能混作同一前置。现需目标内精确激活/加载证据，未盲改本体接口，G1保持partial。

最新稳定结果：[真实活动view与原生BrowseObject对照](active-view-dispatch.md)已完成。两项实际目标view均为Shell File System Folder、路径匹配、0项，observer和目标Explorer均未加载proof；原生BrowseObject虽S_OK也未激活扩展。SDK正控及创建时间负控验证了观察匹配。两轮guard/空目录/测试窗口均清理，原用户窗口保留；证据指向分派层，未修改原DLL实现或自动切换官方注册。G1仍partial。

本轮最新：[官方固定提交对照研究](official-sample-review.md)已完成，[相同注册下的两路径绑定](path-bind-comparison.md)均实际绑定原CLSID并枚举1项，DLL均在Bind阶段出现。原注册/目录已清理，未运行GUI；下一优先项是E0真实活动view的Folder/PIDL身份，不继续猜测接口或属性。官方源17份通过Git blob核验，缺Category.cpp，官方样例未构建/注册/运行。G1仍partial。

最新：[第五轮用户态ETW对齐](explorer-user-trace-aligned.md)进行了两次GUI提交，首次晚于捕获结束、第二次最终Return有效对齐（预填除外）。两次均为空目录，三个计划进程无proof DLL/trace；主协调报告有效捕获仅见control、无目标COM/UserLoader失败事件，不能据此断定未尝试加载。注册/空目录/测试窗口已清理，原窗口和管理员控制台保留。G1仍未完成，不继续接口/注册试探。

此前[四轮ETW前置准备](explorer-etw-preparations.md)均未提交GUI入口，分别经历scope结束、UAC取消、Kernel-Registry PID异常及用户态候选UAC取消，全部已撤销现场。未导航的准备态不计G1结果；随后用户批准的确认会话才支持上述第五轮。

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
