# RegOpen观察前的通知与首次导航：纯准备

主协调已选定公开RegOpenKeyExW的特定HKCR/BA16 ShellFolder key观察方向，真实承载模块/forwarder及SHCORE IAT映射由候选工具另行冻结。本窗口本轮只评估并准备触发流程，没有注册、GUI、notify、attach或Explorer重启。

## 复用结果与实际入口

323CD843 guard只有run/verify：verify在Add-Type前返回，run会登记，因此不能把guard直接当notify-only命令或dot-source。可独立复用的是其C#文字块中的`OfficialSampleRegistryControl.NotifyDrives()`。旧notify-diagnostic/notify-cleanup脚本只有UPDATEDIR，且前者还执行MTA轮，不能作为本次原样双通知复用入口。

新包装在 `.runtime/explorer-notification-trigger-plan/`：

| 文件 | SHA256 | 职责 |
| --- | --- | --- |
| invoke-notify.ps1 | 8B7C6F3D7701CA637FA9E02D340952B69F9254ED76E191C50B86C24EA216C69D | 仅从校验后的guard提取C#文字块、编译原类型并调用一次NotifyDrives |
| run-notify.ps1 | D0FA51C55D9E3A5C4F71609C18B3AC3716F6AE87A120FFC5AF2114483D14F57B | 单次marker、隐藏通知sender、外部10秒上限和结果保留 |

guard SHA固定`323CD843CE402405C07102B7950A201617B372AE7A3E7DA0A62ED602BACD3A76`，AST确认只有一个Add-Type中的单引号here-string；C#文字块SHA为`5D65A4295A3915FD241E3101A8835F08F2F03EB4B845F950F061A031ECA9DE6A`。只编译该文字块不会运行guard的PowerShell登记/cleanup；原类型中的CreateExclusive定义虽存在，包装器没有调用它。实际发通知仅有一处NotifyDrives调用，不执行其他guard代码。

候选及阶段握手就绪后，采集driver可异步启动下列现有入口；这里是准备的命令说明，本轮**未执行**：

```powershell
pwsh -NoProfile -File .runtime/explorer-notification-trigger-plan/run-notify.ps1 -RunDirectory <本次新周期目录> -PlanId <本次新plan-id>
```

sender在原方法调用前检查新鲜capture-ready的PlanId/PID/creation、ready时间0..30秒、原registration deadline至少剩30秒、原F298及初始通知成功、未stop/cleanup。保存原Notification全部字段，以及sender/target身份、ready/start/end UTC和原期限前后剩余秒数。Notification.NativeThreadId是sender线程，不能当成目标Explorer事件线程。单次CreateNew marker阻止重试覆盖；旧周期路径或ready不得复用。

原实现保持：新线程明确STA，CoInitializeEx及MyComputer PIDL成功后，先`SHChangeNotify(0x1000,0,pidl,null)`，再`SHChangeNotify(0x08000000,0,null,null)`；一次双通知对中ASSOC恰好一次，不另叠ASSOC-only，不加FLUSH等新flags。原Join(3000)、异常记录、PIDL释放/CoUninitialize及Invoked/Returned字段都不改。

## 固定最小触发顺序

1. 新周期按原F298/323四根登记及600秒，独立reader核20字段；新专属窗口仅SDK17项正控和身份检查，先不进入ThisPC、不F5、不做官方Browse。
2. 在SDK视图启动新的公开RegOpen observer，等待准确ready并继续读取其日志。注册产生的原双通知发生在这之前，照常保留。
3. ready后异步启动一次run-notify，持续排空observer stdout；不得在采集循环里同步等待通知进程或CUA。父包装10秒外限只约束自己新建的通知sender，绝不终止debugger、Explorer或guard；日志保留后据结果推进。
4. 只有sender退出0、未超时、原Notification.Success及期限仍有效时，写出“可首次进入ThisPC”阶段信号；由唯一GUI操作者完成首次ThisPC导航及原生活动视图正控，再向新driver确认同PlanId/PID/creation的ThisPC阶段完成。
5. driver继续读取日志，检查ready/capture剩余时间与原注册deadline，首次且唯一启动已有Browse控制器。所有阶段记录UTC/原期限和自身结果，将notify、首次ThisPC、Browse分开，不把阶段时间相近当作同一API调用关联。
6. 任一前置失败就保留原件并合作取消/清理observer，不重复通知或导航；后续原注册cleanup/双通知及字段缺失读回仍由周期owner负责，600秒不延长，原用户Explorer不重启。

已有 `explorer-live/20260910-cache-attributes-v2/capture.ps1` 的ready分支会马上Browse；新RegOpen driver必须在新目录中按上述阶段改接，不能原样运行该旧周期包装或旧identity。视图和Browse实现可复用 `.runtime/explorer-official-observer/run-cache-attributes-v2.ps1` 的原生C6ED563F/508C3E5B工具，复制到新周期仅适配新输出/identity目录，保留ThisPC自检和10秒Browse上限。本轮没有修改旧capture或发出阶段信号。

## 结论边界与本轮验证

SHChangeNotify返回void，没有成功HRESULT；Notification.Success只汇总本方法的线程/COM/PIDL及调用返回，不能证明Explorer已处理完通知，更不能证明所有私有缓存都刷新。此处flags=0也不是FLUSH；官方文档讨论了关联变化对图标/缩略图缓存的通知，不给所有内部缓存清空保证。[Microsoft SHChangeNotify契约](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shchangenotify)。

“首次ThisPC”仅指本轮可见主视图导航；SDK窗口的导航树或其他Shell动作仍可能预先构造对象/填缓存。零匹配仍只代表选定API/key/时间范围未观察到调用，不为零匹配重复盲采。新的单API观察器不含旧outer属性断点时，阶段时间戳也不能伪造outerCallId关联。

本轮实际只校guard hash、解析AST、提取并校C#文字块hash、写两份薄包装并做AST/静态审查；没有编译提取出的C#、执行包装、注册、发通知或附加进程。原文字块中的未调用方法及准备脚本不计运行时成功。[准备索引](notify-trigger-preparation/evidence.json)保存来源、原文字块、包装及提取审计；新候选完成并合并阶段driver后才验证实际通知路径。无新语言/框架/依赖、产品代码、公开契约或权限改变。
