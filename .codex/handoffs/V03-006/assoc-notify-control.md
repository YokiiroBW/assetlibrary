# 官方四根注册有效期内的ASSOCCHANGED对照

本轮仅在已验证的STA/MyComputer UPDATEDIR之后追加ASSOCCHANGED，注册及清理均实际执行并返回；仍未发现或成功打开官方样例。两种void通知完成不等于所有客户端缓存已经处理，也不能将此结果扩大为排除所有缓存机制。G1保持partial。

微软现行[Registering Shell Extension Handlers](https://learn.microsoft.com/en-us/windows/win32/shell/reg-shell-exts)指出创建/改变handler后应发送SHCNE_ASSOCCHANGED，否则变更可能延至重启才识别。本任务审计8份封存官方guard/补通知源，只见UPDATEDIR；旧proof registration.ps1:24确有ASSOCCHANGED，不能说全项目从未发过，也不能把旧proof调用等同于官方四根注册有效期的对照。不能排除其他软件发过全局通知。

根协调审查并核准候选323CD843…；最小patch保留原UPDATEDIR，之后调用SHChangeNotify(0x08000000,0,null,null)，同一显式STA线程，增加ASSOC进入/返回/耗时和managed/native thread记录。Success要求两通知均完成，注册与清理主体逐字保持不变；F298、原11字段/4根/owner/600秒及Inproc路径不变，不加schema、关联注册值、系统策略或调试器。

2026-09-10 04:02:34.754Z注册后，Notification为STA/type0、CoInitialize=S_FALSE、Folder=S_OK；UPDATEDIR与ASSOC的Invoked/Returned、Joined均true，ManagedThread9/NativeThread29504，ASSOC于elapsed2ms进入并返回。此后创建唯一自有2558892/PID17396，创建04:03:00.2178147Z，SDK与ThisPC实际view正控成功。

04:05:59.914Z F5后ThisPC仍2项，无FolderView SDK Sample。唯一Browse提交04:06:12.137Z：正确ThisPC前置、官方MyComputer子根42B PIDL、普通flags1。实际出现自有“无关联应用”模态23923778，控制器10秒到期；未收到browse.return HRESULT，不把外部超时改写为函数返回失败码。

模态关闭后且注册仍有效，实际view为MyComputer类20D04…/22B，与官方42B目标不匹配；针对ThisPC的只读匹配成功，ItemCount2。观察器各阶段未载入样例，独立目标模块快照也未见样例。不伪造原样例没有的Factory日志，不由模块快照断言从未尝试加载。

stop/finally于04:08:30.5611275Z正常exit0，四HKCU根与四HKLM检查全absent、CleanupErrors空。清理同样在STA上完成UPDATEDIR与ASSOC，ManagedThread15/NativeThread30208，调用/返回/join成功。自有窗口和模态已关闭，原1247028与Chrome8128618保留。

[原件索引](assoc-notify-control/evidence.json)保存源审计、核准diff/hash、实际双通知、正控、唯一超时/模态、有效期view/模块及清理。没有重跑旧业务/合成测试或扩大观测；结论仅为这一具体通知补充未修复本机入口阻断，下一步由主协调裁决。
