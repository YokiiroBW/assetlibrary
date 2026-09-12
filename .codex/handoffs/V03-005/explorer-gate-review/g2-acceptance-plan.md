# G2 剩余实机验收

状态：V03-012 工具已完成构建、自动测试和审查；真实场景尚未执行，不作为 G2 通过证据。恢复后由 root 独占桌面验收。2026-09-12 准备窗口的截图两次报 `IGraphicsCaptureItemInterop.CreateForMonitor 0x80070057`，因此未注册入口或启动任何管道 Host；不能通过无截图模拟点击补填实机结果。

后续只读 WTS 查询确认 Session 2 为 `WTSDisconnected`（4），枚举值以本机 Windows SDK 头文件核对；见 [会话记录](g2-session-readonly.json)。仅为收尾读取了 SDK 窗口的 17 项可访问性内容，尝试一次 Alt+F4 又收到 `GetCursorPos 0x80070005`，窗口关闭未确认且未强杀 Explorer。该新建测试窗口 HWND 133536/PID 18976；重连后须重新检查身份和界面再清理。所有实机用管道 Host/注册均未启动，已启动的合成 Core 则完整停止并验证 138 文件不变、6 账号与服务清理。见[准备结果](g2-preparation-outcome.json)及 [Core 清理](g2-core-cleanup.json)。

## 固定范围

沿用 ADR-0018 的自有 4FF 测试 CLSID、两处 HKCU 根和包外普通用户登记；保留 600 秒 guard 和独立九字段清理读回。所有窗口、进程均新建并核验 PID/创建时间/会话；禁止重启或强杀原 Explorer。真实 Core 仅使用既有 138 文件合成支架，原件 hash/mtime、临时账号和服务清理须确认。

故障进程使用 `ExplorerFaultHost.exe --execute --mode <mode> --lifetime-ms 119000 --max-connections 32`，预留取消后总寿命不超过 120 秒；每次新进程、新输出文件。等待 `listening`，记录 SHA、PID、创建时间及事件。其 `request_received` 的 `client_pid` 必须对应已核验的目标 Explorer。该工具与真实 AssetHost 互斥，停止退出后才启动下一进程。

| 场景 | 故障动作 | 真正需要观察的结果 |
| --- | --- | --- |
| crash | 自有故障进程读取目标 Explorer 请求后，异常终止自身且不走正常清理 | 目标错误状态、可继续操作；故障进程退出；真实 AssetHost 启动后根重新可读 |
| timeout | silent 接收请求但不返回响应；partial-frame 只返回部分帧后不完成 | 目标有界错误状态、可继续操作；停止故障进程后真实 Host 恢复 |
| invalid frames | invalid-version 返回错误版本且关联实际请求 ID | 显示无效响应状态、没有把错误帧当资产；真实 Host 恢复 |

真实根恢复必须读取实际视图的 4FF class/PIDL/count/合成库名并保存原始截图；不以工具的合成 `ready` 空页代替真实 Core 恢复。组件测试里的 ready 仅为协议正控。故障后的错误页允许一次明确 F5 重试或重新进入根，须记录实际操作；不声称错误页会无限自动轮询。

每个场景记录实际视图观察、可操作性、同一 Explorer 身份存活、故障/恢复进程日志、源与二进制 SHA、Application Error/WER 相关事件的时间及 PID 对应。稀疏截图只能提供观察上界；没有连续计时证据就不写成 G3 UI/取消/重连性能通过。G2 注销通过后仍不得宣称所有 COM 引用已经释放。

本轮不计作 G4 的 20 轮验收，也不声称已有八小时稳定性证据。崩溃/重启循环和 soak 另按原协议执行并保留原时间标准。
