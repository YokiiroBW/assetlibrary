# 本项目Shell proof的包外验证计划与代码修正

本轮只修改测试注册/验收工具、上下文回归及说明，没有操作GUI、注册、通知或重启。root仍是唯一GUI操作者，须待明确移交后才执行本计划。微软样例恢复仅解除已证实的环境入口阻断，本项目自身G1..G4及Windows首版仍未完成。

## 根因与修正范围

root的同周期A/B显示包内323 guard写后20字段正确，普通同用户/会话reader却全部missing；同样guard/F298/四根改由已观察Explorer的ShellExecute启动后，原生reader20字段正确，真实Explorer进入BA16类、42B PIDL/10项。随后包外清理字段缺失、样例项消失，原桌面未重启。原始材料位于本worktree `.runtime/explorer-live/20260911-procmon-01`、`20260911-native-reg-01` 及root `.runtime/procmon-20260911`，大PML/CSV留runtime，不纳入Git。

本项目最小改动：

- `registration.ps1 register`和`verify.ps1`在任何注册写入前要求已核验的普通用户系统Explorer直接父进程路线；核对父进程系统路径、创建先后、同用户/会话、x64、普通权限和无包身份。没有包身份仅是必要条件，不是视图一致的证明；本执行器只读API曾返回15700，也不能据此宣称包外。
- 所有注册读回保留原presence/owner字段，但明确`RegistryView=current-process`；原生启动来源独立记录，`SystemExplorerRegistrationVerified`与`SystemExplorerCleanupVerified`始终false。真实Explorer的类/PIDL/内容与清理证据另取。
- unregister仍可清理当前视图中的本owner根，避免父Explorer退出后阻断恢复；包内absence或exit0不代表包外清理。verify仅清理由本次成功登记的根，不在首次登记失败时盲目删除另一份注册。
- 原proof通知加入显式STA和成对COM初始化/释放，失败返回主线程触发清理；保留原两个事件、顺序和flags，不修改CLSID、Attributes、显示名、Provider或Shell C++实现。C#仅在外部测试PowerShell中运行。

这是选择已实测Windows启动路径的test-only约束，不是通用MSIX/Silo检测器或安全权限模型。没有修改Codex包manifest、虚拟化设置、HKLM/UAC、系统策略或硬编码本机Silo/包版本。apps/windows-client目前没有实际Explorer安装器，本次不新增一个产品安装器。

## 下一次唯一实机周期

1. root明确移交GUI后，重新取得活动/解锁会话及当前系统Explorer窗口。原SDK broker PID15848/HWND788038已关闭，不复用；重新交叉核PID、创建时间、系统路径、同用户/session、普通权限和窗口身份。
2. 先准备全部文件/命令，再开始时限。构建入口保持 `cmake -S tests/windows-shell -B .runtime/explorer-proof -G "Visual Studio 17 2022" -A x64` 和 `cmake --build .runtime/explorer-proof --config Release --parallel 2`；本次若C++输入不变，优先复用已验证构建，核DLL/probe完整SHA，不无故重编译改变样例字节。DLL/trace使用本次新runtime副本，不写旧证据目录。
3. 通过**已观察窗口**的`Document.Application.ShellExecute`启动固定普通用户`pwsh.exe -NoProfile -NonInteractive -File ...`。用已确认的ShellFolderView.Application实例，不用包内Process.Start替代，也不使用管理员runas。记录实际外部进程PID/creation、直接父ExplorerPID/creation和same-user/session结果。路径参数按Windows argv逐项引号转义；不要在Explorer地址栏执行命令。
4. 先在该包外进程执行现有 `tests/windows-shell/verify.ps1 -BuildDirectory <已核新构建目录>`：本项目独立COM/PIDL/DefView、重复登记拒绝和同进程finally清理。此处验收通过仍不是G1；实际上下文正例尚待本阶段取得，不能把合成布尔回归当实机来源证明。
5. GUI部分采用单一包外600秒临时guard，复用当前 `registration.ps1`：同一外部进程先验证启动来源，再register，保存完整读回；以真实登记时刻开始600秒，响应本次stop文件，finally在同进程unregister并读回。仅本项目两个固定HKCU根（4FF8301D…的Classes/CLSID和Desktop/NameSpace），字段保持原值；不能拿官方BA16四根guard冒充本项目guard。薄guard需在执行前作为本次runtime文件冻结审查，本计划不声称该文件已生成或执行。
6. 字段必须在同一包外视图逐项核存在/类型/实际值：类默认名、Owner、pin DWORD1、Inproc默认DLL路径/Apartment、ShellFolder DWORD A8040000、DefaultIcon、Desktop挂载默认名及Owner；共7个原字段+2个Owner。检查默认值missing与空串差别，DLL路径及原文件强hash一致。包内同名读回可以作为对照，但不能代替该步骤。
7. 在有效期内创建新的独占Explorer测试窗口，SDK/身份正控后，从Desktop发现本项目“AssetLibrary 集成验证”，只执行一次打开。用真实活动IShellView/IFolderView的IPersistFolder2确认类`4FF8301D-2E73-4D49-9FE5-868D5F1EA302`、目标根PIDL及实际内容；根枚举应为1个“示例资源库”，与C++源码一致。可补独立root-bind作对照，但不得用它替代actual view。探针均有10秒外限，GUI输入/模态失败即保存原始错误，不追加猜测变体。
8. 清理先完成测试工具/调试器退出（本计划不要求新debugger）、关闭本次自有窗口，再由**原包外guard同进程**注销/双通知；新包外reader复核两个根及固定字段缺失，Desktop真实列表中入口消失。保留原用户窗口/桌面，不重启Explorer。若包内清理成功但外部仍存在，状态必须是清理未确认，不以包内absence覆盖。只关闭测试窗口不保证DLL已卸载，记录模块实际状态，不删除仍加载的DLL，不借此关闭G2..G4。

以上生命周期按任务既有owner/冲突/finally原则执行。具体SDK窗口创建仍用computer-use的sky API，必须新清单选择及原生身份匹配；不得复用本机历史PID或从文档中的示例推定窗口所有权。

## 本轮验证与限制

新增 `pwsh -NoProfile -File tests/windows-shell/test-registration-context.ps1` 使用合成上下文逐条件允许/拒绝，覆盖NoPackage+same-user/session仍拒绝、非布尔/缺失证据拒绝、包内/原生来源的presence和absence均不晋级系统证明、缺失owner保持null；另对当前测试进程做一次只读令牌路径回归，不注册或通知。实际父进程来源与通知正确性仍须包外下一周期验证。

首次获准包外独立verify在来源查询阶段遇SecurityException，尚未登记；原失败保留。只读对照确认WindowsPrincipal成员查询在QUERY-only self token上失败，QUERY|DUPLICATE可成功，因此仅self token访问由8改10，parent仍8，所有准入判据不变。修正后当前包内执行器QuerySucceeded=true、OrdinaryUser=true，仍因父来源非Explorer而拒绝。没有据默认false误判提权，也没有通过修改准入降级。

后续薄guard的600秒起点在调用register之前，包含登记和通知耗时。包外另查两个同名HKLM根不存在，作为独立冲突前置，不混入9个HKCU字段或写HKLM。verify的probe输出以新GUID目录流式保留，exit/timeout和捕获状态落盘后才断言；旧失败输出不覆盖。

复用现有HKCU owner注册、原COM/DefView probe、系统进程/令牌API和普通Explorer执行方式，没有公开契约、数据库、核心权限或Provider变化。每次来源检查只涉及执行器和一个父进程，CIM查询限5秒；无需50万资产索引或扫描。当前成功/失败字段属于局部工具证据，不宣布产品Windows完成。

依据：[Flexible virtualization](https://learn.microsoft.com/en-us/windows/msix/desktop/flexible-virtualization)明确Windows11新配置优先和私有视图；[MSIX运行时排查](https://learn.microsoft.com/en-us/windows/msix/manage/troubleshoot-msix-container)说明Explorer离开container；[GetCurrentPackageFullName](https://learn.microsoft.com/en-us/windows/win32/api/appmodel/nf-appmodel-getcurrentpackagefullname)只描述包身份。Raymond Chen的既有Explorer ShellExecute方案由root已读并实测，本轮网页返回403，未把未取到的新页面内容当作新增证据。
