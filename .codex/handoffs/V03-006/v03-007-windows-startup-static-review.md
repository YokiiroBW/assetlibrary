# V03-007 Windows 解码进程启动静态复核

2026-09-08，由 V03-005 主协调委派 V03-006 进行，只读审查代码与官方 API。结论已反馈主协调并由其转交 V03-007 owner。本记录不表示修复完成：**未证实 CreateProcessW 返回203的根因，未运行任何 AppContainer、安全探针、解码器或故障测试。** 没有修改021c工作区、生产/共享源、HKLM、UAC或系统策略，没有读取完整环境、凭据、用户资产或全量日志，没有GUI/CLSID动作。

## 审查快照

工作区：`C:/Users/Administrator/.codex/worktrees/021c/AssetLibrary`。当时 HEAD 为 `12b718226bdfbe159781fc70692095e2dc561d40`，以下五个文件均为未跟踪文件；因此该 HEAD **不代表这些文件的源码版本**。本表保留当时已读取的 SHA256，不重新读取owner后续实现来替换历史快照。

| 文件（相对审查工作区） | SHA256 |
| --- | --- |
| services/core-server/Infrastructure/ReadOnlyWorkers/WindowsImageStartup.cs | F80322E66766D82F868EFDAD66E50F92D05B03F8BC354A0A6F07C6CCB5F4330F |
| services/core-server/Infrastructure/ReadOnlyWorkers/WindowsImageProcess.cs | C5D32F796538FFD2108100BAD826521C9F01B2770A1FD9B554AD9D2277A7AB8C |
| services/core-server/Infrastructure/ReadOnlyWorkers/WindowsImageProfile.cs | EE518E5BF7BBB95EF4C9DD596DD0629602DC8762098304990B6BC79151CFE48E |
| services/worker-supervisor/ImagePreview/WindowsImageIsolation.cs | 18F53D16C6335351A90C4288D19D8B4F75E9088864ED4DF817FD56FFF673C345 |
| tests/dotnet/AssetLibrary.Preview.Tests/WindowsImageProcessTests.cs | E3BE50DCD2381823D46E94A8FAD9522D04F90BC781154377DACB3B6C2170E1A9 |

补充只读查看了同工作区的 WindowsWorkerJob.cs、ImagePreview/Program.cs 和 worker csproj，以理解Job、Ready消息和NativeAOT调用链；当时未记录这三个辅助文件的独立hash，不能将其视为已固定的源码快照。下面的行号对应上表快照，后续变更可能移动行号。

## 203：最高优先级假设仍是环境构造

203是 `ERROR_ENVVAR_NOT_FOUND`，属于环境变量查找错误。它不能单凭数值指出缺少哪个变量。[Microsoft错误码](https://learn.microsoft.com/en-us/windows/win32/debug/system-error-codes--0-499-)

WindowsImageStartup.cs:76–86显式构造最小环境；当前快照已经包含LOCALAPPDATA、SystemRoot、WINDIR、TEMP/TMP，不能继续断言当前实现遗漏LOCALAPPDATA。USERPROFILE等其他上下文项未包含，只能列为候选，未找到证明此调用必须提供某一完整变量清单的官方依据。应先将历史203对应的源码、EXE哈希与本快照对齐。

官方说明AppContainer配置文件与LOCALAPPDATA相关，并会调整LOCALAPPDATA/TEMP/TMP；因此最小环境与AppContainer初始化之间的交互值得优先验证。[AppContainer/LPAC启动说明](https://learn.microsoft.com/en-us/windows/win32/secauthz/implementing-an-appcontainer)

最小建议由owner在既有授权测试边界执行：先做纯托管环境编码断言，覆盖名称白名单、指定必需项非空、名称/值无意外NUL、排序、双NUL结尾及Unicode flag；然后保持LPAC、JOB_LIST和HANDLE_LIST不变，一次仅增加一个经OS API获取的特定非敏感变量，记录变量名和存在/非空布尔。不要通过继承完整Host环境来引入凭据，也不要用修改多个环境项后的成功宣称单一根因。

在记录的故障阶段，CreateProcessW已经返回false。Skia加载、worker的IsEnforced与Ready发生在成功创建并恢复主线程之后，不能拿这些后续阶段的缺口解释该次203。官方也区分CreateProcess返回与子进程后续初始化失败。[CreateProcessW](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-createprocessw)

## Win32声明与隔离属性核对

未发现支持“修改属性常量或撤掉隔离约束”的明显ABI错误：

- 冻结Windows SDK10.0.26100.0的WinBase.h确认HANDLE_LIST=0x20002、SECURITY_CAPABILITIES=0x20009、JOB_LIST=0x2000d、ALL_APPLICATION_PACKAGES_POLICY=0x2000f，OPT_OUT=1。对应Startup.cs:29–38与官方LPAC组合一致。
- x64默认布局静态推导为SECURITY_CAPABILITIES=24、STARTUPINFO=104、STARTUPINFOEX=112字节；现有Sequential字段的指针和DWORD宽度匹配。这里是声明核对，不是本次运行探针的测量结果。[SECURITY_CAPABILITIES定义](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-security_capabilities)
- cb取STARTUPINFOEX大小、可写Unicode命令行、CREATE_UNICODE_ENVIRONMENT与EXTENDED_STARTUPINFO_PRESENT、四项属性的内存生命周期以及SID持有范围均合理。属性值必须一直存活到删除属性表；当前分配列表满足这一点。[属性API](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-updateprocthreadattribute)
- 三个client pipe句柄为真实可继承句柄，CreateProcess的inheritHandles=true，符合HANDLE_LIST要求。.NET管道实现将server端复制为不可继承句柄，并保留client端；并未发现此处错误地继承所有server端的证据。[.NET实现](https://github.com/dotnet/runtime/blob/main/src/libraries/System.IO.Pipes/src/System/IO/Pipes/AnonymousPipeServerStream.Windows.cs)
- JOB_LIST在创建时绑定，job控制句柄不需要加入子进程继承白名单。不能通过移除Job绑定或LPAC来绕过启动失败，再把普通进程成功当成隔离成功。
- StringToHGlobalUni会复制字符串内部的NUL；当前环境编码不能仅因使用该API就被判为“在第一个NUL截断”。[Marshal文档](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.marshal.stringtohglobaluni?view=net-10.0)

WindowsImageIsolation.cs:16–18所用TokenIsAppContainer、TokenCapabilities、TokenIsLessPrivilegedAppContainer对应29/30/46；DWORD或TOKEN_GROUPS起始GroupCount的读取方式与SDK声明匹配。零capability及LPAC检查有意义，但不等于本次做过实际拒权或网络负向测试。[Token信息类别](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ne-winnt-token_information_class)

## 具体诊断、清理和期限风险

以下为代码可见的条件性风险，**没有据此定位203，也未在本窗口复现**。

1. **原始错误信息丢失。** Startup.cs:17–24、96–100丢弃Initialize/Update失败的原生错误，Profile.cs:41–46丢弃CreateAppContainerProfile的HRESULT。建议保留阶段、属性编号与即时数值错误，区分HRESULT与Win32错误；不记录环境内容、路径或凭据。当前CreateProcess失败处读取LastPInvokeError的方式没有发现明显问题。
2. **清理异常可覆盖启动错误。** Process.cs:76–89忽略WaitForExit(2000)返回false，并继续调用可能抛错的profile.Dispose；Profile.cs:82–102先标记disposed再删除。若进程退出未确认或清理失败，原始启动错误可能被替换，且同一对象不能重新进入Dispose。建议补“创建失败+清理失败”和“终止后等待超时”的最小故障回归，保留原错误与清理状态；未确认退出时保留owner记录供有界恢复。不是建议忽略清理失败。
3. **同步启动缺少独立期限证明。** cancellation回调在WindowsImageProcess构造函数中注册，而构造发生在ResumeThread之后。15秒CTS不自动为此前同步profile创建、文件复制和CreateProcess提供独立截止保证。建议owner测量并覆盖启动阶段取消、死/卡worker及清理结果，再据实修正；不能把后续管道I/O取消测试外推为完整启动期限。

当前WindowsImageProcessTests.cs主要验证真实缩略图成功、Ready、退出与owner记录清理；不覆盖上述启动故障组合、非白名单句柄不可用或完整LPAC文件/网络拒绝行为。不要为运行成功直接加入lpacCom、registryRead等能力；那会改变已批准的隔离边界。

## 结论与交接边界

没有静态审查已证实的203根因。优先对齐失败版本，再验证最小环境；ABI、四项属性和管道白名单未发现明显错误。诊断信息保留、清理错误溯源与同步启动期限是具体后续验证点，由V03-007 owner实施，root统一验收。

本审查不新增任何运行测试计数，不改变V03-006原四项loader矩阵、桌面失败记录或Explorer partial状态。V03-006继续等待主协调明确桌面恢复，不做相同代码、GUI、CLSID或基线轮询。

归档校验：`python -I -B scripts/validate_handoff.py`通过，diff空白检查通过；没有重复构建、loader矩阵或完整仓库回归。
