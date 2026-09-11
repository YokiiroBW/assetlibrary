# V03-006 — 有界 Loading 自动刷新

2026-09-12。Root已在真实Explorer证实默认open可进入库，但5秒Ready有效期与手动F5容易使Loading长留；本轮按Root批准扩展，仅改test-only Shell。共享contract由Root更新，Core TTL/权限清空/150ms本地IPC均不变。真实GUI执行权属于协调端指定操作者，本窗口只执行自有测试窗口。

每次CreateViewObject创建独立Folder/Signal/DefView，采用IShellFolderViewCB与IObjectWithSite。SFVM_WINDOWCREATED给出的真实view HWND只在其UI线程挂原生subclass和500ms窗口timer；没有TIMERPROC、线程池UI操作、网络或解码。首次Loading启动固定10秒/最多20次预算，连续Loading不续期；Ready、错误、站点清空、销毁停止。超时后连续Loading不自启，F5仍可单次查询，重新打开使用新view。

异步枚举取单调generation，过期结果不能覆盖新状态；通知只含唯一cookie，无借用指针并合并待处理消息。Tick经站点查询活动IShellView后，再核HWND、有效Loading generation、episode、active预算及site revision，才Refresh。SetSite先交换再Release旧引用，尊重释放时重入撤销；call-out始终保self/site/view。KillTimer后已排队消息会被状态/代次检查拒绝。

自动刷新名额与callback保活分开：最多4个保留/活动view名额，确认Detach后幂等归还，旧callback即使被外部保留仍可开下一view。callback用原子COM引用计数，保持Folder/DLL owner直至实际最后Release；不为清零计数强行释放。超出名额仍可手动F5。复用Win32 Comctl32，无第三方依赖、共享业务契约、路径、权限或数据写入改动；每页/每次查询上限沿用已冻结值，与50万资产总量无关。

## 验证

严格Release全目标build通过。四项CTest通过：explorer_snapshot、explorer_navigation_menu、explorer_loading_refresh、explorer_loading_view_wiring。新增预算/消息代次、工作线程只投递状态、Ready/Error停止、重入销毁、旧site.Release撤销、QueryService/活动view/GetWindow变更后的再校验、两个view独立、关旧留callback而开第5view、真实DLL owner最终Release/可卸载边界均通过。

系统机制测试使用真实Desktop owner、SHCreateShellFolderView/CreateViewWindow。没有手动调用SetSite或SFVM_WINDOWCREATED：系统确实赋site、给出准确child HWND；受控Loading信号自动刷新活动view记录器一次后停止；停用/销毁窗口、站点清空与名额归还通过。这不是proof数据加载或真实Explorer验收，不读取Desktop项目名。

## 严格卸载反例保留

```powershell
& .runtime/explorer-snapshot/Release/ExplorerLoadingRefreshTests.exe (Resolve-Path .runtime/explorer-snapshot/Release/AssetLibraryExplorerProof.dll).Path --proof-owner-lifetime
```

此单独诊断当前exit1：UIActivate(DEACTIVATE)/DestroyViewWindow成功、窗口消失、site为空、活动名额0；测试COM作用域及CoUninitialize结束后仍有callback1，DLL仍S_FALSE，最终卸载不通过。测试保持一次显式module pin到进程退出，避免先FreeLibrary再执行剩余回调；未强Release。普通机制测试和G2严格诊断为不同验收范围，不能将四项CTest通过写成完整卸载通过。

临时引用观测与Root既有只读符号工具把未配对的Windows调用定位到windows.storage.dll RVA23DFB1、匹配PDB的CViewSettings构造函数内。只能说明实际持有来源，不能证明系统bug、其最终释放时机或已解决增长。最终源无私有追踪/调试输出，不调用弃用SetCallback或私有SFVM消息。G2最后COM释放/增长、G3/G4和8小时证据继续开放。

最初尝试以空proof PIDL做真实隐藏DefView数据测试时，系统重绑定Desktop：33项、Host查询0，Loading断言失败。该反例保留在empty-root-control.txt；没有把它算作proof加载，也未猜造namespace PIDL或自行注册。符号与最终严格诊断记录同目录归档。

## 依据与后续

[原Microsoft样例](https://github.com/microsoft/Windows-classic-samples/tree/main/Samples/Win7Samples/winui/shell/shellextensibility/explorerdataprovider)、[WINDOWCREATED](https://learn.microsoft.com/en-us/windows/win32/shell/sfvm-windowcreated)、[窗口subclass线程约束](https://learn.microsoft.com/en-us/windows/win32/api/commctrl/nf-commctrl-setwindowsubclass)、[KillTimer队列行为](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-killtimer)、[停用视图](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellview-uiactivate)是公开机制依据。

另读[ReactOS原始兼容测试](https://raw.githubusercontent.com/reactos/reactos/master/modules/rostests/apitests/shell32/IShellFolderViewCB.cpp)，未复制GPL代码；其末尾1是IShellView Release返回值，且另有仍在作用域内的QI引用，并非当前callback1的等价证据。

分支codex/v03-006-windows-explorer-native-integration，本交接所在提交为本轮代码。Root合入后在新构建目录记录新DLL hash，以真实注册PIDL验收冷Loading→Ready/物理目录/分页；本轮不关闭Windows完整交付或发布门禁。

最终仓库交接/架构/契约/依赖检查通过，Alpha仍blocked；原35项仓库回归通过。限定4项CTest通过，严格proof-owner诊断单独exit1保持未通过，不把不同范围合成全通过。
