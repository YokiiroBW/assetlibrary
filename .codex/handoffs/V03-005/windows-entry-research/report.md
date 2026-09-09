# Windows Explorer 入口阻断：官方资料与判别方案

研究日期：2026-09-09。集成基线 `7f5d15f`。范围为 ADR-0018 的 C++ Shell namespace / DefView 与进程外 C# AssetHost；Windows 为当前优先事项。本次不改变客户端架构、系统策略或原资产。

## 结论与可信度

继续现有原生 Explorer 方向。微软的 namespace 文档明确允许 HKLM 或 HKCU 的虚拟挂载点，官方 ExplorerDataProvider 示例说明非提升注册仍能使用 namespace（自定义属性注册另有要求）。本轮没有找到微软确认 Windows 11 24H2 全面禁止这一路径的公告；这不等于当前构建已经兼容通过。[挂载点文档](https://learn.microsoft.com/en-us/windows/win32/shell/nse-junction)、[官方示例说明](https://learn.microsoft.com/en-us/windows/win32/shell/samples-explorerdataprovider)

当前尚无已证实的 Explorer 阻断根因。本轮发现并补测了独立探针与GUI入口形式不同的缺口：两类入口在独立进程均可绑定本组件。因此下一优先项明确为实际Explorer视图对象身份与导航分发，随后才决定修改注册、Shell接口或调查宿主策略。

## 1. 已找到的具体证据缺口

`tests/windows-shell/Probe.cpp:11` 只解析 `::{4FF8301D-2E73-4D49-9FE5-868D5F1EA302}`；最近 GUI 使用的是 sandbox 下 `AssetLibraryProof.{GUID}` 的绝对物理路径。两种入口都由微软文档描述，但不能假定它们在当前系统一定绑定相同处理类。独立 GUID 根绑定成功，尚不能把物理路径显示空目录完全归为 Explorer 进程差异。[两类入口](https://learn.microsoft.com/en-us/windows/win32/shell/nse-junction)

`RootBindProof` 在 `CreateViewObject` 返回后释放 `IShellView`，没有在真实 Explorer 创建视图窗口。微软将取得视图对象与随后 `CreateViewWindow` 初始化分为不同阶段。因此当前 S_OK 只支持对象创建，不能覆盖宿主回调、默认打开动作、界面和生命周期。[DefView API](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shcreateshellfolderview)、[视图生命周期](https://learn.microsoft.com/en-us/windows/win32/lwef/nse-folderview)

现有组件的 `GetUIObjectOf` 总返回 E_NOINTERFACE，`GetDisplayNameOf` 不区分显示名称与解析用途，解析/绑定只覆盖极简样例。官方完整示例提供更完整的关联、默认菜单和解析行为。这些是激活后的已知实现缺口；目前不能把其中任何一项直接宣布为“类工厂未观察到”的根因。[基础文件夹接口](https://learn.microsoft.com/en-us/windows/win32/shell/nse-implement)、[GetDisplayNameOf 契约](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellfolder-getdisplaynameof)

另外，原Trace函数的result参数默认S_OK，许多QI/Factory/Initialize记录发生在方法入口。日志里的00000000常不是实际返回值；只把明确的返回后记录用于接口成功判断。新PathBind阶段记录在实际调用返回后采样。这是观察方式的改进，不是已定位的产品故障修复。

Windows owner 已用 `git ls-remote` 将官方 Windows-classic-samples 固定到 `434f6002bdf9cf9829406c3ff2b33387982d6168`；审计必须使用该版本的逐文件来源与 hash。该示例来自旧 SDK，不把“官方示例存在”当作本机 Windows 11 实测。微软还明确记载旧 x64 Release 工程遗漏 .def 的问题；本项目已有 .def 且独立导出调用成功，没有证据把这个旧问题套成本机根因。[固定样例目录](https://github.com/microsoft/Windows-classic-samples/tree/434f6002bdf9cf9829406c3ff2b33387982d6168/Samples/Win7Samples/winui/shell/shellextensibility/explorerdataprovider)

root独立重算已取得17项官方文件的长度、SHA256及带Git blob头的SHA1，全部与固定版本清单一致。Category.cpp在本轮有限下载中未取得；官方样例本轮未编译、未注册、未运行。以下零告警构建属于自有PathBind观察器，不属于官方样例。

## 2. 直接读取实际视图身份

Raymond Chen 的微软工程档案给出按 HWND 找到 Explorer，再取得当前 Shell view 和文件夹对象的方法。文章正文可从搜索索引读取，本轮直接抓取返回403；下列关键接口另由当前 Microsoft Learn API 文档核对。[工程档案](https://devblogs.microsoft.com/oldnewthing/20040720-00/?p=38393)

建议读取链路：`ShellWindows → 精确 HWND → SID_STopLevelBrowser / IShellBrowser → QueryActiveShellView → IFolderView.GetFolder(IPersistFolder2) → GetCurFolder / GetClassID`。它能识别窗口当前对应系统文件夹还是自有 namespace，而不是从标题、空目录外观或独立进程的 Desktop 对象推测。[QueryActiveShellView](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellbrowser-queryactiveshellview)、[GetFolder](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifolderview-getfolder)、[GetCurFolder](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ipersistfolder2-getcurfolder)

实施时只匹配新鲜确认的自有测试窗口及活动标签；多个同 HWND 结果或身份歧义即停止。外部子进程期限10秒，输出限制为阶段 HRESULT、类ID、自有路径/标记与有界枚举计数。不得输出用户已有窗口内容，也不能把读取API成功直接算作自定义视图通过。

## 3. 本机与官方更新核对

只读记录见 [host-readonly.json](host-readonly.json)。本机为 Windows 11 24H2 EnterpriseS，OS `26100.8037`，Explorer 文件版本 `10.0.26100.7840`。不同组件修订号本身不构成损坏或缺补丁的证据。

与 OS 构建对应的 KB5079473（2026-03-10）包含 WDAC COM allowlist 与端点策略优先级的修复。本机已处于该构建，不能把已修复问题认定为当前故障，也没有据此执行更新、回退或修改安全策略。[KB5079473](https://support.microsoft.com/en-us/servicing/os/windows-11/2026/03/march-10-2026-kb5079473-os-builds-26200-8037-and-26100-8037)

App Control 确有按宿主应用限制 DLL 的机制，所以独立探针加载成功不能完全排除针对 Explorer 的规则。只读 `CiTool.exe -lp -json` 本次返回 `0x80070005`，未得到策略列表；记录中的空数组表示未知，不表示没有有效策略。[宿主定向规则](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/app-control-for-business/design/use-appcontrol-policy-to-control-specific-plug-ins-add-ins-and-modules)、[CiTool 查询](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/app-control-for-business/operations/citool-commands)

针对第五轮实际时段13:18–13:29 UTC查询现存8036、3077、3033事件，没有返回事件。固定CLSID在HKCU/HKLM的32/64位 NonEnum、HideDesktopIcons两种子键共12项查询均无对应值。这削弱已查隐藏设置的解释；无事件仍不能证明曾尝试加载或全部安全规则允许。8036的COM宿主规则文档也不能直接扩大成“Explorer必须额外加COM白名单”。[COM策略范围](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/app-control-for-business/design/allow-com-object-registration-in-appcontrol-policy)

此前包身份、SID/Session/位数/完整性级别及动态/静态CRT对照继续保留，不重复未改变条件的检查。旧管理员诊断进程32932在15:26 UTC查询时已不存在；本轮未新开UAC，也未把旧受限会话扩展成通用管理员执行器。

## 4. 有明确判别结果的执行顺序

| 顺序 | 验证 | 结果如何决定下一步 |
|---|---|---|
| E1（已完成） | 同一个短时注册窗口、原DLL下，独立进程分别 Parse/Bind GUID根与实际物理路径，读取处理类/PIDL并有界枚举 | 两条实际都返回自有类，继续E2；不再把物理入口自身解析失败作为已观察原因 |
| E2 | 从自有Explorer窗口读取实际活动视图身份，再做一次有完整前后记录的导航 | 判断失败发生在解析/发现、激活、视图创建还是子项交互；覆盖路径预填，不只记录最终Return |
| E3 | 固定官方完整样例做本机对照，记录构建适配、注册字段和挂载点差异 | 样例也失败时优先宿主/部署条件；样例成功时逐项比较最小原型。不同挂载点是独立变量，不把一次差异混称某个接口修复 |
| E4 | 根据已定位阶段修复必要接口/注册接线，完成真实嵌入 | 默认打开/子项解析修复与激活修复分别验收；成功后再接入AssetHost和Core |

E1已完成本轮唯一新增受限验证：15:34:46 UTC，同一注册窗口内两个新进程分别处理GUID根和真实sandbox绝对路径。两次Parse为S_OK且原DLL尚未加载；Bind为S_OK之后原DLL加载且路径匹配；GetClassID均为原自有CLSID，GetCurFolder成功，均枚举1项。两个进程exit0，无stderr或超时；root审查了原始stdout、PathBind.cpp和外部10秒期限脚本，而不是仅依赖返回码。原被测DLL SHA仍为0174db9b…2fd15，观察器SHA为021f3cdb…75d5c；观察器Release构建零告警。

这项实测没有出现“物理入口在独立进程仅绑定系统FS类”的情况，也证实单纯Parse不会在本次样例中加载组件。下一步是E2实际活动view身份；不能把这次独立成功当作G1通过。仍须检查实际Explorer是否走到Bind和取得哪个处理类。

原始记录还保留：GUID路径PIDL长度22/22；物理路径Parse后记录920字节、随后GetCurFolder记录882字节，ILIsEqual返回true。长度取样时点不同，没有Bind后原parsed快照，不能把这组数值解释为已证实的语义等价或系统规范化；这不影响两次实际GetClassID与模块匹配的独立观测。使用既有owner保护HKCU脚本和有期限清理，没有GUI导航、DLL改动、目录属性/desktop.ini改动。15:34:46.188 UTC两键false、guard正常退出，已验证空目录移除。E2/E3仍是后续方案，本轮不运行官方DllRegisterServer或全局日志工具。

微软 Process Monitor 可以观察文件、注册表和进程/线程活动，但其公开说明强调可调整的非破坏性过滤，不能据此宣称“筛选条件确保非目标事件从未进入采集器”。若后续需要该工具，应先核实采集/保存范围；不沿用先前已失败的Kernel-Registry PID过滤假设。[Process Monitor](https://learn.microsoft.com/en-us/sysinternals/downloads/procmon)

完成标准仍是真实Explorer发现自有组件、显示并浏览原生视图，随后完成Host故障恢复、卸载、响应/取消界限、20轮恢复及8小时稳定性。官方资料、独立探针和诊断链路成功都不能替代这些证据。

完整逐行对照见[Windows owner研究](../../V03-006/official-sample-review.md)，两项实验源/原始输出/清理见[对照索引](../../V03-006/path-bind-comparison/evidence.json)。owner提交38274de已整合；root独立核对17项归档原件SHA，研究未改变生产实现或发布门禁。
