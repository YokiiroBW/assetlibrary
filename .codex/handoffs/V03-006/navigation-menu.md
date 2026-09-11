# V03-006 — 默认目录导航与系统图标

2026-09-12。协调任务已观测新 DLL 的真实根视图显示一个 Core 库，但双击只选中，未进入目录；GetUIObjectOf 原实现始终返回 E_NOINTERFACE。此次只改 test-only Shell 与本任务交接，GUI和注册仍由协调任务独占。

## 实现

单个 Library/Directory/NextPage 提供 IContextMenu + IObjectWithSite，只有“打开”。CMF_DEFAULTONLY设置默认项，支持数字offset0和ANSI/Unicode open；无效ID、写入/复制/新窗等其他verb拒绝。多选、背景、文件、链接、状态项不提供导航菜单，不添加注册类或默认Folder类菜单。

菜单持有创建时校验并复制的完整absolute PIDL及原Folder引用。InvokeCommand通过站点SID_STopLevelBrowser获取IShellBrowser，调用BrowseObject(SBSP_ABSOLUTE | SBSP_SAMEBROWSER)；无站点、服务或导航失败原样失败，无路径执行回退。创建/命令解析无IPC，仅后续枚举仍使用原快照协议。

IExtractIconW/A使用SHGetStockIconInfo的SIID_FOLDER（库/目录/下一页）或SIID_DOCNOASSOC（文件/链接/状态），交给SHCreateDefaultExtractIcon。普通/打开图标仅引用Windows系统资源；不读资产、不查资产文件关联或调用媒体handler。

## 官方依据

已阅读原Microsoft ExplorerDataProvider.cpp/ContextMenu.cpp。原样例的默认菜单会回调IQueryAssociations，本受控slice选择自有单open菜单，以精确限制命令。

- [Microsoft样例](https://github.com/microsoft/Windows-classic-samples/tree/main/Samples/Win7Samples/winui/shell/shellextensibility/explorerdataprovider)
- [QueryContextMenu默认双击和offset](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-icontextmenu-querycontextmenu)
- [InvokeCommand数字/ANSI/Unicode规则](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-icontextmenu-invokecommand)
- [BrowseObject站点与当前窗口/absolute规则](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellbrowser-browseobject)
- [系统图标位置](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shgetstockiconinfo)、[标准提取对象](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-shcreatedefaultextracticon)

## 验证和边界

严格Release全目标build（/W4 /WX /analyze）通过。新`explorer_navigation_menu` CTest通过，10秒外限：实际DLL/工厂/GetUIObjectOf/QueryContextMenu/InvokeCommand，菜单未显示，浏览器服务为无窗口IShellBrowser/IServiceProvider记录器。三种可导航项验证默认项、命令范围、数字/ANSI/Unicode、NODEFAULT、负verb、无站点/清除/替换/服务与导航失败、输入Folder及PIDL释放后仍可invoke、DLL/site引用回收。六类图标实际读取IExtractIcon普通/打开位置，与独立系统stock查询相等，A/W接口均通过。无pipe、注册、可见GUI或资产访问。

受影响既有`explorer_snapshot` CTest通过，wire/PIDL/管道/COM/公开probe仍可用。无共享契约、新依赖或业务逻辑变更；单菜单、单有界PIDL和系统图标对象，复杂度与资产总量无关。

分支codex/v03-006-windows-explorer-native-integration。建议协调端合入本交接包所在提交后构建新DLL并记录hash，独占全新原生周期验证双击/Enter、下一页与图标，不复用旧目标进程/guard。COM记录器不能替代真实Explorer目录证据，G2..G4仍未关闭。

最终`git diff --cached --check`与`python -I -B scripts/verify_repository.py`通过：交接、架构、契约、依赖与35项既有回归保持通过，Alpha仍blocked。注册脚本与原spike源码未变，不重跑已通过的原注册策略/17项spike测试或无关.NET整套。
