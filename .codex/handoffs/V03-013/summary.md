# V03-013 显式刷新后 Loading：机制研究

状态：partial，已完成本轮技术验证，体验问题未解决。基线4059e40，分支 `codex/v03-013-explicit-refresh-loading`。root于2026-09-12仅批准公开通知的有界机制测试，未批准DLL行为或共享契约改变；构造正控失败后裁决停止扩展。

## 最终结论

系统确实送达一次公开BACKGROUNDENUMDONE，并在owner线程执行延后的一次读取，但合成Folder的空Desktop PIDL使系统DefView重绑定成Desktop：实际33项而非预期合成1项。“实际合成呈现”正控因此失败，不能把此次通知作为本扩展等价接线成功。只记录计数，没有读取33项的名称或内容；尚未到达外部Refresh/内部自动Refresh阶段。

原始失败保留在tests.md，未改断言为通过、未配置预期失败、未用skip冒充平台通过。可选研究入口保留15秒自有进程监护上限（超限exit70），当前构造正控失败为exit1；不登记到默认CTest。默认DefView初始/重开观察和F5单次重试的运行时完全未改变。

两个现有Loading回归通过，严格构建通过。它们不证明新通知机制、本次体验修复或G2/G3/G4通过。root明确不再添加temp filesystem对照或新变体；下一步必须在已验证的真实自有namespace内获得最小通知计数证据，再裁决NonLoading→Loading增强，不在本任务继续。

## 官方依据与现有调用链

先查询Microsoft官方资料，再核查SDK10.0.26100.0与代码：

- [IShellView::Refresh](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellview-refresh)是F5刷新入口；[Implementing a Folder View](https://learn.microsoft.com/en-us/windows/win32/lwef/nse-folderview)同样描述View菜单刷新，并建议优先复用DefView。文档不证明当前工具栏按钮的全部内部路由。
- [SHCreateShellFolderView](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shcreateshellfolderview)返回系统DefView。现有Folder::CreateViewObject调用loading::CreateView并直接返回该接口，所以我们的Callback不接收其Refresh入口。
- [MessageSFVCB公开通知表](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-ishellfolderviewcb-messagesfvcb)和SDK `um/ShlObj_core.h:2891..2921` 都有BACKGROUNDENUMDONE（48），没有SFVM_REFRESH或SFVM_LISTREFRESHED。本次核查不使用私有常量。
- [SFVM_BACKGROUNDENUMDONE](https://learn.microsoft.com/en-us/windows/win32/shell/sfvm-backgroundenumdone)只承诺后台枚举完成，无参数、用户来源或周期ID；不能把每次DONE视为一次显式刷新。[ShellFolderViewOC.EnumDone](https://learn.microsoft.com/en-us/windows/win32/shell/shellfolderviewoc-enumdone)也不能增加这些语义。
- [IFolderView::ItemCount](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifolderview-itemcount)及[Item](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifolderview-item)可读取当前视图的条目元数据，现有LoadingRefresh已使用这两个入口。
- [SFV_CREATE.psvOuter](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/ns-shlobj_core-sfv_create)仅供common dialog使用，不能据此推断Explorer支持透明Refresh包装器；本任务不采用包装器。

现有Callback只处理WINDOWCREATED与site，初次window/site就绪时启动一次500ms/10秒/20次观察。Tick核活动视图HWND和首项私有PIDL，只有实际Loading允许Refresh，Ready/错误停止。EnumObjects的clone Signal已经被现场证据证明不能作为实际视图状态权威，继续仅供诊断。

正确实机来源在V03-005工作区 `.runtime/explorer-host-integration/desktop-07/`；crash-recovered记录当前Root呈现Loading，crash-ready-final记录重新进入后的Ready，归档见 `.codex/handoffs/V03-005/explorer-g2-live/cycle-07/evidence.json`。root核对的单次工具F5输入未见请求，而原生Refresh按钮立即触发真实目标进程请求，不能据此宣称产品F5损坏。本任务不追查输入焦点，也不把重开恢复称为单次Refresh恢复；现行契约本就要求Host重启后重开根。

## 待裁决的Contract Change Proposal

每视图记住最后核验的实际类别Unknown/Loading/NonLoading。仅当已确认NonLoading→Loading且无活动周期时，才考虑开始新的固定10秒周期；不推断触发者是用户。NonLoading包括普通项及错误：错误本身不自动Refresh，后续事件看到实际Loading才有资格开始，意味着视图已有新的查询结果。空列表/未知PIDL不授权新周期，初始空视图观察保留。

活动周期的重复Loading或任何通知不重置deadline/attempts。Loading耗尽后保留Loading标记；迟到DONE或仍呈现Loading的F5不能重启，必须先确认中间NonLoading。因此该候选不承诺持续Loading时每次F5都获得新十秒。

事件只合并为一个owner线程的延后检查，不直接查询Host；不增加Ready后的常驻轮询。事件未送达、尚未呈现、未知状态、COM失败或无名额时保留现有行为：初始观察照常、停止后F5单次、重开新周期。所有COM调用及Release后重核siteRevision/HWND/episode；重入不嵌套刷新，站点或窗口改变撤销资格和排队工作，新周期有独立timer代次，旧消息不能消费新预算。沿用4活动视图、150ms查询、5秒Core缓存、本页首项O(1)和真实COM/DLL保活。

实现前仍需root裁决。回归应覆盖NonLoading→Loading、Error→Loading、持续Loading十秒与迟到DONE、旧timer、跨COM撤站/换窗、双视图和四名额，并具备会失败于旧行为的正控。本次不实现这些新行为。

## 已批准的机制测试范围

复用LoadingRefreshTests的系统DefView/隐藏窗口/RefreshBrowser/现有加载Callback；合成内存Folder提供私有PIDL，不使用pipe或真实资产。仅测试初始枚举、外部直接Refresh和现有内部自动Refresh，记录系统DONE的线程/顺序及事件返回后一次延后500ms的实际视图读取。未交给DefView的回调作负控，测试不得自行调用DONE；不强制后台枚举。固定次数、阶段时长和CTest外限。

无事件时如实记录未送达，而非伪造支持；独立隐藏DefView结果不能代替真实Explorer工具栏验收。当前阶段不操作GUI、注册、服务、默认pipe、Host/Core或门禁，不重查注册/COM保留旧问题。

## 边界与下一步

所属模块为test-only Windows Shell验证；复用既有PIDL、Budget和生命周期机制，没有复制权限/查询业务，没有新语言、框架或依赖，无数据库或共享契约变化。资产安全、权限、产品运行性能及兼容性不变。新增代码只编入测试EXE，未改变DLL行为；即使50万资产，产品运行复杂度不变，候选设计仍只读本页首项O(1)。

修改文件为本任务包、三个标准handoff、`tests/windows-shell/LoadingRefreshTests.cpp`、`EnumDoneTestFolder.h`、README。默认CTest清单没有变化。建议先审研究及失败证据，再决定是否保留诊断；不能据本提交合入FSM增强。commit以本分支最终Git记录及交付消息为准，避免在同一提交中写不可成立的自引用hash。
