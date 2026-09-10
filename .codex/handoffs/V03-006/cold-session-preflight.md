# 当前用户Explorer冷启动：准备完成，尚未执行

用户已明确回复“做吧”，主协调确认授权测试前及清理后各重启一次当前用户Explorer，包括关闭原窗口和重载桌面/任务栏。授权无需重复询问；本轮暂停的原因是执行环境不满足活动桌面条件，不是权限缺失。

## 新鲜事实

最初只读预检识别22个当前用户/Session2的系统explorer.exe，均持有句柄核对了创建时间、SID、系统image、存活与无debugger；桌面GetShellWindow和Shell_TrayWnd同属PID6284。未使用旧PID列表终止进程。

随后WTS明确返回ConnectState=4（Disconnected），输入桌面OpenInputDesktop只读查询失败Win32 5。最后原件仍为SessionActive=false、InputDesktopIsDefault=false、Eligible=false，详见[状态](cold-session-preflight/status.json)及[脱敏快照副本](cold-session-preflight/before-preflight.json)。工具时钟与原始UTC正常；PowerShell读JSON后的14:34+08与06:34Z是同一时刻，未发生已证实时钟跳变。

CUA只读list/UIA仍能读取原“此电脑”窗口的2项和地址栏焦点，不能推翻WTS非活动状态。一次请求返回的截图显示Codex而非目标Explorer，已排除。没有GUI输入、注册、停止进程或重启。冷启动注册目录和stop marker均不存在，原Explorer、Chrome、Codex及NAS保持未操作。

## 工具边界与主协调审查修正

临时工具在 `.runtime/explorer-cold-session-control/`。默认inspect仅收集只读身份与窗口元数据，stop为显式动作。本轮只运行inspect、C# Add-Type编译及PowerShell AST解析；没有调用Stop，不把准备或只读结果计为冷启动通过。

- 固定快照最多128个Explorer候选；仅当前SID/活动session/系统路径且无debugger的目标可进入范围。持有原生句柄并验证创建时间，完整重新比对集合后才允许终止，避免PID复用。其他用户/会话不停止。
- 每个目标终止前重新核对WTS Active及Default输入桌面、目标身份/存活/无debugger。固定集合中非桌面先、桌面最后；不动态循环追杀Windows自动恢复的新PID，不按泛进程名kill，不启动Explorer，不改AutoRestartShell或提权。
- 部分终止后发生失败即停止后续请求，finally在关闭句柄前对已请求目标做总计≤5秒退出核验。不能把部分失败写成全部退出；剩余桌面不追加终止。
- 原生窗口清单包含隐藏项且不保存其他项目名称/路径。仅已观测的ThumbnailDeviceHelperWnd/DummyDWMListenerWindow，在cloaked、空标题、≤1×1时分类为辅助窗。隐藏OperationStatusWindow只在精确100%完成标题下成为候选；stop前必须与≤60秒inspect同PID/创建身份/HWND/class/完成态两次匹配，消失、新增或其他操作/错误/未分类窗口均拒绝。最初宽拒绝快照原样保留。
- 首次stop要求原F298与323CD843四根owner注册、双通知、期限及4LM absent；第二次stop要求注册撤销/双通知成功。每阶段单次CreateNew marker，UI与scope记录都必须≤60秒且非未来时间。UI观察**不能证明没有任何后台IO**。

最终源SHA256 `F36F1833BF16E36108DAAE7B87EA4B2F6249969A36BAF768FCC9BE12FB4817BF`；wrapper `448AB744EC8F0A7354A9871D92C5F5FBD158192A0315BFF92FC06BDF68958AAF`。[证据索引](cold-session-preflight/evidence.json)保留源文本、各次只读快照的脱敏副本及派生状态。含SID/账号的原件只保留于runtime，版本库副本用当前用户标记替换并内嵌`redacted_copy_not_original`说明；索引分别记录原始runtime SHA与副本SHA，不将副本当原件。

## 恢复入口

用户重新连接并保持Windows桌面活动、解锁后，由主协调继续既定一次冷启动方案。必须重新做CUA文件操作检查和原生inspect，使用全新快照；旧PID、旧UI清除记录和旧preflight不得用于stop。注册完成后第一次重启，先确认所有旧句柄退出并优先识别Windows自动恢复的新桌面Shell，在重启工具退出后再确认其存活。只有未自动恢复时，才用CUA以普通用户交互方式启动已验证系统Explorer。其后独占窗口/正控/一次入口检查，最后清理并第二次重启；尚未执行这些步骤。

复用既有F298样例、323CD843 owner guard、Windows原生身份/调试/窗口元数据模式。没有产品代码、共享契约、依赖或权限模型变化，无资产读取/写入，G1..G4仍未完成。本轮不重跑已通过的产品测试。
