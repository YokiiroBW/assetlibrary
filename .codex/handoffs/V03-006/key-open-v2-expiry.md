# 异常预算V2：成功参考保留，登记到期前未启动实机

本周期的新异常处理参考normal/cancel均通过，但后续实机采集调用发生在原600秒登记期限之后，被包装前置拒绝；observer未启动。原guard自然清理成功、20missing确认、专属SDK窗口已关闭。按root排他协调，本窗口完成此交接后停止GUI/登记/实机操作，保留代码owner身份，由root确认后指定新操作者接管。

## 已完成与未执行

冻结observer `75F1AB60E25283FAB42A87DDBC716339DA94417E18315054C021FA53E60541FC`，target `EC9936C5A4B2B5D6D20F6E6C2B69B8C19FAB2C46DC8BC99817E2F88DC5C13EF9`，manifest `F9CA737F2BB5C0E4B977FFD5DB28F1C40DCCEDA4E7C0EA945F71BDE201935E32`的19件启动前核对。原F298/323四根登记UTC16:32:42.9942356Z，期限16:42:42.9942356Z，20字段匹配。

| 参考 | 目标PID | C++请求/自行捕获/调试器观察 | 总异常/详日志 | 其他first/second | 开键/句柄/自行关闭 | 清理 |
| --- | --- | --- | --- | --- | --- | --- |
| normal | 48100 | 32 / 32 / 32 | 33 / 16 | 1 / 0 | status0 / 已读且非空 / true | passed、Detach/健康/协作退出 |
| cancel | 47360 | 32 / 32 / 32 | 33 / 16 | 1 / 0 | status0 / 已读且非空 / true | passed、cancel/Detach/健康/协作退出 |

两种已通过参考在输入不变时不重跑。原准备里的超限参考保留首次触发与最终计数：阈值256不是立即硬上限，不能把16条详细日志当异常总数，也不能由参考中已处理的异常推断真实Explorer异常良性。

专属SDK窗口6097724/PID48420创建于16:33:45.4447886Z；SDK原生读回开始16:48:17.9162908Z、exit0，但已在登记到期之后，仅是物理SDK正控，不是有效登记窗口内的实机证据。capture前置因此报`Registration prerequisite/time failed`；16:48:45Z再次读回原期限已过362秒且cleanup文件已存在，capture-launch文件不存在。虽然生成了新plan文件，它没有用于启动observer，不能在后续复用。

登记、窗口创建和后续动作间的间隔如实保留；部分界面工具曾延迟/环境更新中断，但没有足够记录将全部间隔归因于某一工具、CPU或网络。本窗口没有延长guard、补做过期动作或重试实机。

## 最终clean状态

guard于16:42:43.0126223Z自然结束，exit0、双通知成功、8根absent；独立reader随后20条missing-key通过。关闭专属SDK窗口后环境更新中断了首次最终读回，恢复后仅做只读确认：该窗口未出现在CUA清单，记录PID48420也已不存在。最新CUA只暴露ChatGPT，不据此推断其他原用户窗口当前状态。先前只操作专属窗，未重启原Explorer；接管者必须重新盘点当前桌面、用户窗口和活动会话。

没有实机observer、额外notify、首次ThisPC或Browse发生，因而没有待detach的实机调试器。原始/明确脱敏资料见[45项索引](key-open-v2-expiry/evidence.json)，4份含身份/profile的JSON副本逐项记录原件和副本SHA；原始19件候选含二进制另存本周期runtime的candidate-frozen。旧周期文件和成功参考不覆盖。

## 给下一位唯一操作者的执行入口

当前工作树：`C:/Users/Administrator/.codex/worktrees/6f7b/AssetLibrary`。下列文件的相对位置均相对该目录，脚本自身从PSScriptRoot解析此repo，不能只改调用cwd就假定输出写往另一个worktree。

- 采集/活跃门/原生时钟：`.runtime/explorer-reg-open-stage-v2/`。capture SHA `4B2C576DF9C81C912217CC20549DB995BD9C8540654BCC2420DC1C17F5C27D33`；assert-live `ECAD486483A5847692647F79BF203DCF2320E33147558D3BE3C72039C5827A53`。只启动collector持续排空，不自动操作界面。
- 视图/Browse：`.runtime/explorer-official-observer/run-clsid-key-open-v2.ps1`，SHA `F261CEF902CCD7745DD3BFCBA1A3CA12A3579493E51F9E9040978229DC261C2A`。原C6ED活动视图和508C Browse二进制不变。
- 通知：`.runtime/explorer-notification-trigger-plan/run-notify.ps1`（D0FA51C5）及invoke-notify.ps1（8B7C6F3D）；保持原NotifyDrives一次STA双通知对，原3秒join/10秒sender外限。
- 字段：`.runtime/explorer-registry-readback/run-readback.ps1`，11官方+4Owner+5外部HKCR。原guard可以从已完成周期复制，必须SHA323CD843，F298 DLL仍在`.runtime/explorer-live/20260910-official-sample/ExplorerDataProvider.dll`。
- 固定新observer/plan根：`C:/YOKI/Codex/AssetLibrary-worktrees/V03-005/.runtime/explorer-clsid-key-open-debug-v2`，同目录bin的V2 exe；每次新11行official plan，PID/creation/窗口/epoch必须新取。

需要新输出目录时，仅在新副本中一致替换capture、assert-live和view的固定cycle目录，保留已完成目录；原notify wrapper通过RunDirectory参数接新目录。不要复用本周期plan、identity、ready、SDK窗或reader标签。全部准备好后才开始600秒；在周期内只执行已经冻结的动作，结束后再写长文和封存。无需重跑不变的已通过参考或重新索取已有用户授权。

创建SDK窗口的实际方法是computer-use的`@oai/sky`：先list_windows保存既有窗口，记录ownership epoch，再`sky.launch_app({app:'C:\\Windows\\explorer.exe'})`，刷新清单并唯一选中新出现的Explorer。此前这个入口实际打开了SDK目录；不能保证新环境仍如此。若不是SDK，按两步观察/输入规则，用Ctrl+L、`C:\Program Files (x86)\Windows Kits\10`、Return导航物理正控，不能在地址栏执行终端命令。用新窗口标题和原生MainWindowHandle/PID/creation交叉核对，要求创建时间晚于epoch、非桌面进程及单一自有窗口，随后原生SDK正控17项。

后续严格保持：SDK→collector ready→每一步fresh assert-live并保存回执→原通知对→首次ThisPC及原生正控→一次Browse；失败或时间不足即停止依赖动作并合作清理。capturing期间不要等待root重复批准。root收到本窗口clean移交确认后才接管GUI，本窗口之后保持静止，避免两个执行者同时操作。
