# E0真实活动视图与BrowseObject分派对照

结论：真实目标视图明确为系统`Shell File System Folder`，路径匹配但0项；由独立控制器提供同一实际路径的PIDL并调用一次BrowseObject后仍如此。故本轮优先定位宿主分派层，不能把0项归因于原扩展的Initialize/Enum代码；也不能把BrowseObject的S_OK当作扩展激活成功。两项现场均已清理，原用户窗口保留，G1保持partial。

本轮依据主协调明确授权推进两个稳定步骤，不重复询问桌面许可；无新管理员工具/UAC、系统策略、HKLM、注册属性或被测DLL修改。原DLL SHA256保持0174DB9B1B4CCD4925D3A28470930FA6FAEBD4070348CC374A7CB87313E2FD15。所有代码只在自有.runtime构建，产品和tests源码没有改动。

## 观察器与边界

ActiveView观察器严格校验由CUA新鲜确认的新窗口对应的**原生**HWND、Explorer PID及创建FILETIME，并确认进程映像为系统explorer.exe。CUA id不被默认转换为HWND，关联来自新窗口集合、唯一标题与原生进程主窗口读回。ShellWindows最多64项，必须唯一匹配、无不可读项、枚举前后计数稳定；取得active view后再次验证view HWND属于目标frame及PID。不是取首个匹配或全桌面路径扫描。

链路为IWebBrowserApp→IServiceProvider/SID_STopLevelBrowser→IShellBrowser.QueryActiveShellView→IFolderView.GetFolder(IPersistFolder2)→GetClassID/GetCurFolder，再做期望路径匹配及ItemCount。输出只有类GUID、布尔匹配、大小/计数、实际HRESULT和时间，不读取其他窗口的路径或焦点文件。每个进程外部10秒期限；比较前后PIDL大小分别记录，不把二进制比较与canonical比较混为一谈。

expectedPathParse和CompareIDs在获取活动Folder/类ID之后执行，每一步均记录观察器本进程是否已加载proof；另在调用观察器前后独立读取目标Explorer的模块。这避免把观察器中的重建/后段绑定误当作Explorer加载。两次目标读回中所有观察器阶段均proof=false，目标模块快照也均无proof。

Release x64、SDK10.0.26100.0、MSVC19.44.35228、/MD，最终`/W4 /WX /analyze /permissive- /utf-8`零告警。初版PIDL的unaligned限定符及大栈缓冲警告均在临时源码中修正，未压制检查。观察器EXE SHA256为2F3287A25CA18D33B90DA510967F8FD34C64A1D35EE2910DD9BAA5B9589F17B8。

## E0：地址栏进入后的实际处理类

自有窗口4458392、PID36676、创建16:03:48.788786Z，原窗口1247028保留。SDK正控16:04:47.359–.427Z全链S_OK：ShellWindows2项/精确匹配1项，当前路径匹配SDK，类F3364BA0…，view ItemCount17。将期望创建FILETIME加1的负控于16:05:36.451–.473Z在identity.before返回8007051b/exit1，未枚举ShellWindows；无timeout/stderr。

600秒原owner guard于16:05:37.2523231Z注册。一次完整地址栏导航：聚焦16:07:03.568Z、预填及路径读回16:07:16.527Z、Return16:07:40.491Z，UIA16:07:40.831Z为目标目录0项/空文件夹。没有只覆盖Return的trace条件；注册/模块的前后记录完整保留。

目标观察器16:07:41.235–.308Z成功匹配同一frame/PID/创建时间，全部链路S_OK：

- 类ID为`{F3364BA0-65B9-11CE-A9BA-00AA004AE837}`，不是原扩展。
- GetCurFolder为856B，物理路径精确匹配自有sandbox目标；独立期望PIDL920B，二进制不同但canonicalCompare相等，比较前后大小未改变。
- 实际view ItemCount0；observer全程未加载proof，独立目标模块前后均无proof，当前DLL目录无trace；注册两键有效。

本机只读HKCR64精确类键进一步确认F3364BA0…的默认名为`Shell File System Folder`，Inproc为系统windows.storage.dll。此名称依据来自实际注册键，不是误称为已找到SDK常量。完整处理类和路径匹配证据把“观察到错误窗口/只有独立Desktop正控”的缺口收窄；不解释为何Explorer选择该类。

为避免旧guard临期，16:14:23.4233018Z stop/finally正常exit0，两键false；经fresh确认关闭4458392，空目录核验后删除。未沿用过期现场赶跑后续调用。

## 一次原生BrowseObject对照

主协调批准普通的[IShellBrowser::BrowseObject](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellbrowser-browseobject)调用。使用独立控制器，保留原E0源码与EXE hash不变；控制器复制已审查的只读匹配前段以冻结原观察器，未引入生产业务重复。只用`SBSP_SAMEBROWSER|SBSP_ABSOLUTE`（数值1），没有信任、ActiveX或绕策略标志。

新鲜自有窗口49349910/PID30304，创建16:18:48.1966293Z。SDK真实view正控17项、唯一身份/路径匹配。新原样guard于16:19:29.8831642Z注册，原DLL不变。控制器在调用前再次要求当前view确为SDK目录，排除同canonical位置短路；剩余guard不足20秒则拒绝调用，外部仍是10秒进程期限，dispatched文件拒绝再次发起。

16:19:56.249Z，控制器PID36292在外部SHParse同一sandbox绝对路径得到920B PIDL，仅调用BrowseObject一次；返回S_OK、exit0、无timeout/stderr。没有改为CLSID字符串根。原生调用作为用户明确批准的工程接口对照执行，其余窗口操作使用CUA。

16:20:17.068Z UIA仍为空目录；之后原E0观察器重新读取实际view：类仍F3364BA0…、物理路径精确匹配、canonical相等、ItemCount0。当前PIDL882B，期望920B，大小在比较前后稳定；不据此推断规范化或上下文根因。控制器和后读观察器全过程proof=false，目标Explorer独立模块快照为空，trace不存在，注册仍有效。

因此外部解析PIDL+同frame原生浏览也未激活扩展，不仅是地址栏文字输入问题。两项使用不同新窗口（旧guard不足以完成准备），并未声称形成严格同进程地址栏/原生A-B；每项内部的SDK→目标匹配独立成立。

16:21:39.0489402Z stop/finally正常exit0，两键false；49349910关闭、空目录核验后非递归删除，最终只剩原1247028及Codex。无错误模态、新窗口残留、官方样例注册或后续变体。

## 交付与未决项

[证据索引](active-view-dispatch/evidence.json)包含原始源码、控制器、构建日志、身份负控、两份SDK正控、完整导航/实际view/模块/清理记录。源与产物在`.runtime/explorer-active-view`及`.runtime/explorer-browse-control`，控制器EXE SHA256为9142AEBB468868F1655DF3108291ABD7E25964A62BC2F0B709061593A856FDB3。实际构建均使用既有VS CMake，目标分别ExplorerActiveView、ExplorerBrowseControl，Release x64。

这验证了观察链路和安全拒绝条件，产品入口仍失败；没有把成功读回或Browse的S_OK计作G1通过。没有新增主语言、包、共享契约或生产框架；枚举最多64窗口且不读其他窗口路径，view查询只对自有匹配对象，不能推广为大资产库性能结论。未重跑未变化的业务/图片/全仓库套件。

下一步由主协调针对分派层裁决；公开的绑定上下文机制只是候选解释，未观测即不认定。无证据支持现在修改原扩展的Initialize/Enum或盲补可选接口。
