# Windows 可安装浏览版统一验收

本轮交付 `0.3.0-preview.2`，源码 `c25689371b07e84499a13274da4b088a58d4a789`。实际 ZIP、使用方法和保留限制见 [交付说明](../../../../docs/releases/WINDOWS_EXPLORER_PREVIEW.md)。本目录记录最终包与此前失败候选的界限，不把组件通过当 GUI 通过。

## 修复与复验

内部候选 preview.1 从普通 Explorer 上下文安装后，Settings 两次启动退出 `0xC000027B`；应用 PRI 缺失。启用 WinUI/MRT 打包工具后，最终 PRI 为 2,188,208 字节，含 App/MainWindow 编译资源；发布目标和组包入口同时拒绝缺失 PRI。

原 Host 通知助手退出 2。独立 .NET 实验复现 MTA 调用 STA CoInitializeEx 得到 `0x80010106`，并非 Shell 入口再次失效。助手改为显式 STA，最终普通上下文实测 initialize=1、parse_root=0、dispatched=0、exit=0；真实根刷新和子目录返回根验证了通知确实送达。另修复生产绝对解析名误用 Proof GUID，保留双身份往返负控。

preview.1 已实际注销。关闭测试窗口后旧 DLL 仍被 Explorer 持有，安装器如实返回 3010/待清理；未覆盖已安装的同版本文件、强杀 Explorer 或放松 UAC。修正版递增 preview.2，已实际安装；最终 EXE 另在完全隔离的文件注册适配中实跑 8 步，包括升级、卸载和包损坏拒绝。未自动点击 Windows 设置里的卸载入口。

## 实机证据解释

- 最终专用 Explorer PID33380 / creation134337004322449199 / HWND34868474，普通上下文按精确 PID、创建时间、路径、同用户/会话绑定。用户原 Explorer PID6212 / creation134335976399732731 保持不变。
- Settings 从自包含包正常启动，右键菜单产生的实际 Settings PID35556 父进程33380；重复启动返回0，原单实例不变。设置默认不保存登录。工具没有操作认证表单；正式控制客户端复用生产 Session 库连接合成测试服务。
- `root-connected-initial` → `root-after-logout` → `root-after-reconnect` 是同一视图，无手动 F5/重开干预。`child-before-logout` → `child-after-logout` 显示中文层级与文件被清掉并回根。`history-after-logout-stable` 证明后退不能恢复文件行；历史面包屑由 Windows 自己保留。
- `library-page-1` 为100条加下一页，`library-page-2` 为38条；列表虚拟化使某次 UIA 只返回可见项，不能拿 UIA 输出行数冒充总数，截图状态栏与真实服务端分页测试相互印证。
- `settings-native-launch` 是最终退出测试并停止 Host 后的故障状态截图，用于证明后台缺失可见，不是登录成功截图。设置成功打开的原件为 `settings-disconnected`。
- 第一个窗口正常关闭；第二个新建 SDK 窗口在后续观察中已不在窗口列表，未声称它通过任何最终行为。重新建立身份绑定后由第三个专用窗口完成最终实机流程；未复用失效 HWND。没有发现对应 Application1000/1001/1002 记录，也不据此推断消失原因。

## 验证与清理

最终77 Windows、23 Setup、9 CTest、6 packaging、8 final EXE sandbox步骤、16 release-lock tests通过；17项基础依赖策略回归在其修改完成时通过。2项 NativeLive真实服务测试在最终 STA/PRI小修复前通过，此后未改 Core/传输逻辑，最终 Explorer 会话流程已重新实跑。Windows与Setup两套格式检查返回0，Windows格式器报告工作区加载警告，未隐藏；Release构建均无编译警告。

最终 Windows默认锁审计6项目31包、Setup2项目15包，RID发行锁6项目17包许可通过；locked restore启用NuGetAudit及NU1901..NU1904错误。结构与实际包审核不代替平台实机。

Core验收原件明确 `sample_hashes_and_mtimes=unchanged`、6角色回收、HTTPS关闭及进程/临时目录删除。退出时无remembered.bin，清理前核对连接配置只属于本轮localhost夹具，再删除该测试配置。Host与Settings结束，自有GUI窗口关闭；preview.2正式安装保留，下一次用户打开设置将按需启动Host。旧版本pending清理如实保留。

文件索引及 SHA256 见 `evidence.json`。完整V0.3未完成；G4是用户豁免而非实测。主要未完成项为内容预览/传输、标题与目录导航体验、签名、人工认证表单和系统应用页卸载验证、Android指定真机；不再重跑已解决的注册虚拟化或G3调查。
