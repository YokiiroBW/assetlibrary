# ADR-0018 — Windows 首版以原生 Explorer 为入口

状态：Accepted for implementation，2026-09-08。用户在 V03-001 执行中明确纠正：Windows 必须嵌入原生资源管理器，不能设计成必须另开一个独立客户端。此决定替代 ADR-0017 的 Windows 独立工作区交付范围，Android 和服务端只读范围保持不变。

## 实际入口与实现边界

Windows 的日常入口是原生 Explorer 导航树中的“资产库”。顶部、地址栏、搜索框、命令栏与左侧均归 Explorer；右侧以系统原生列表/图标/属性视图呈现授权库与真实目录。不能用外跳 EXE、浏览器、悬浮覆盖窗口或单独应用截图作为“已嵌入”的证据。

首版优先采用受支持的 Shell namespace、IShellFolder2/IPersistFolder2 与 SHCreateShellFolderView（DefView）。C++ Shell DLL 仅适配 PIDL、导航、列/图标、命令及本机 IPC 的有界快照；C# AssetHost 在进程外处理现有 AssetLink 会话/网络、取消、响应校验和只读查询。Shell 不连接服务端、不哈希或解码媒体，不加载 .NET、WinUI、数据库、Provider 或第三方重型运行时。

此选择使用 ADR-0012 已冻结的 C++/Windows SDK Shell 与 C#/.NET Host 技术预算，没有加入第二桌面 UI 框架。独立 WinUI 工作区只可作为开发验证/将来视图复用方式，不是本次用户启动路径。本轮不为了保留独立窗口而复制 Explorer 顶部与导航。

Windows App SDK 的 DesktopWindowXamlSource 官方承载模型在 host UI 线程/进程内，不能直接放进 Explorer 达成进程隔离。跨进程 SetParent 的 WinUI Window 也不能仅凭截图视为稳定受支持；因此不将该实验方式纳入本次默认入口。首版采用原生 Shell 视觉；专业瀑布流、自定义三栏和复杂预览视图继续需要独立可验证的嵌入/隔离方案。

## 功能与交互诚实性

目录归属、权限、分类和查询来自既有核心，PIDL 不携带账号/密码/Cookie/CSRF。分页有明确当前页语义；系统列排序只能表示当前加载范围，若提供完整目录排序、筛选或搜索，必须通过实际服务器查询命令完成。不得声称未接管的原生搜索框已经提供跨库搜索。只读视图不提供会被误认为真实写入成功的拖放、改名、删除或粘贴。

初始化、枚举、图标与列查询不能阻塞 Explorer 等待网络。Host 丢失/超时/崩溃时维持可交互错误/重试视图，失效会话或拒权不得继续暴露上一个身份的数据。Host 恢复需要重新授权与查询；后台快照不改变物理资产。

## 可逆验证与正式门禁

V03-002 独占 `tests/windows-shell/**` 的具体兼容性验证、测试 GUID 与脚本，复用 `apps/windows-client` 的进程外适配。先证明 HKCU 自定义 COM 在真实 Windows 11 Explorer 中发现、加载与导航，再接入真实 Core/PG 样例。仅注册明确属于本任务的 GUID，必须具备 owner 标记、拒绝覆盖现有注册、有界 Shell 通知与卸载读回。不使用 HKLM 后备，不改 UAC/系统策略，不重启用户桌面 Explorer。

M0-002-G1..G4 保留原有证据标准：批准的每用户注册与真实发现；实际视图和故障恢复/干净卸载；250ms 返回及有界取消/重连；真实恢复循环与八小时稳定性。未完成的门禁不能因用户选择嵌入入口而变成通过。`apps/windows-shell` 生产实现/默认启用与正式发行仍以 `explorer-v0.5` 结果为准；兼容性验证只在专属测试命名空间进行。无法获得的运行证据须明确报告，不能用独立 Host 循环或隐藏 IShellBrowser 代替真实 Explorer。

## 2026-09-12 受控数据接入

自有test-only入口及原生注册/注销闭环已取得真实Explorer证据。用户随后要求接入，V03-005冻结 [只读快照IPC](../../contracts/windows-shell/read-only-snapshot-v1.md)：进程外AssetHost复用现有HTTPS会话与ReadOnlyClient，Shell显示授权库/物理目录分页。真实冷页面证明clone枚举通知不足以驱动实际视图；最终使用公开IFolderView的呈现PIDL观察，只有Loading时执行有界刷新，500ms/10秒/20次/4活动视图，Core5秒有效期不变。根、分页、目录及Host停止/重启/旧位置拒绝已完成受控实机验收。主动权限失效清空、正式登录设置/安装与CViewSettings保留对象的最终卸载仍未完成；生命周期和稳定性门禁独立保留。

## 2026-09-12 原入口门禁裁决

M0-002-G1 两项原退出标准均已满足，现关闭该项。批准的每用户部署路径明确为：普通用户、包外执行器写入自有 `HKCU\Software\Classes\CLSID` 与 `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace`，原生 `InprocServer32` 指向已校验的 C++ DLL；保留 owner、拒覆盖、HKLM 冲突只读检查。本次受控验证另外使用限时 finally 注销与独立原生读回。当前 Codex 打包进程的注册表视图与 Explorer 不同；受控验证通过已核验的自有 Explorer 窗口 `ShellExecute` 启动包外执行器。无需 HKLM 或提权，不能以“无包身份”单一条件替代来源与实际读回证据。

自有 DLL 的真实发现与进入采用 V03-006 原始证据，真实数据接入采用 V03-005 最终周期证据，详见[门禁逐项裁决](../../.codex/handoffs/V03-005/explorer-gate-review/README.md)。正式安装器、签名和登录设置仍是产品交付缺口，但不是 G1 原条款。G2 尚缺真实异常故障恢复；G3 包含最终 DLL 生命周期与取消/重连计时；G4 保留规定循环和八小时运行。没有改变门禁退出标准或启用生产 Shell。

## 官方依据

2026-09-12 后续状态：G3已完成[真实进程量测验收](../../.codex/handoffs/V03-005/explorer-g3-live/README.md)。用户明确不做G4，其20轮/8小时条款由[用户决定](../decisions/2026-09-12_G4用户豁免与G3验收范围.md)豁免，未实测；不得把G4记为通过或重新安排。G1/G2/G3实测完成加G4行政豁免使Explorer门禁目标允许继续，但不自动完成正式登录、安装、预览等产品功能。

- [IShellFolder::CreateViewObject](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellfolder-createviewobject)：Shell 的视图创建接口。
- [Shell folder object](https://learn.microsoft.com/en-us/windows/win32/shell/nse-implement)：PIDL、导航和默认视图适配。
- [WinUI XAML Islands](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/host-controls-existing-desktop-apps)：WinUI 的承载、输入和生命周期责任。
- [SetParent](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setparent)：跨进程窗口关联的 DPI 和窗口样式限制，不等同于 WinUI 的完整受支持承载模型。
