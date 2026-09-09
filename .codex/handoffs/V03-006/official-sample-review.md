# ExplorerDataProvider官方样例对照研究

本轮没有发现足以宣称“已定位真实Explorer阻断”的源码差异。新增的有效进展是补测了实际文件系统入口：它和CLSID根在独立进程中都绑定原扩展并枚举1项。下一步应读取真实Explorer活动视图的Folder/PIDL身份，优先于补齐可选接口或继续改注册。研究及唯一获准实验均已收尾，未运行GUI、管理员工具或样例注册。

## 固定来源与证据级别

Microsoft Windows-classic-samples固定提交为`434f6002bdf9cf9829406c3ff2b33387982d6168`，通过git ls-remote取得，再fetch提交/树并以git ls-tree中的blob SHA1核验下载字节。17/18份所需文件通过；缺Category.cpp，旧.doc未作为构建输入下载。[源码清单](official-sample-review/sources.json)含每份固定URL、Git blob、SHA256及本地文件hash；[许可证](official-sample-review/LICENSE-Microsoft.txt)、[原Dll注册源码](official-sample-review/Dll.cpp.original.txt)、原.def及旧vcproj已保留。GitHub REST遇限流，部分raw/Git blob请求连接重置，未以网页摘录拼凑缺失源码。

下文行号来自固定raw原件，GitHub永久链接与其一致；web阅读器折叠空行后的内部行号不作依据。本地对照为`tests/windows-shell`当前源码，hash在清单中。主协调提供本机OS26100.8037、Explorer10.0.26100.7840；2004年官方文章和Win7样例不是Windows11多标签行为的实测保证。

## 激活前的注册与发现条件

| 对照项 | 本地与固定官方源码 | 判定与最小验证意义 |
| --- | --- | --- |
| COM注册与基础接口 | registration.ps1:60–68写HKCU Classes、Apartment及DWORD属性；[官方Dll.cpp:209–224][registration]也是HKCU。ExplorerProof.cpp:88–99与[官方:185–195][qi]公开同组IShellFolder/2、IPersist/Folder/2 | 已证实基本模型一致。不能因本DLL不导出DllRegisterServer就判激活无效：本任务外部脚本承担注册；原GetClassObject导出与实际绑定已验证。 |
| 挂载父级 | registration.ps1:10/72挂Desktop；[官方Dll.cpp:166、276–293][mount]挂MyComputer并刷新CSIDL_DRIVES；本地刷新Desktop | 是激活前/发现差异，但[官方位置文档][junction]同时支持两者；不是Desktop无效的证据。对照前须识别真实活动view，不能把独立Desktop对象视作目标GUI。 |
| 根属性 | 本地`A8040000`=FOLDER/HASSUBFOLDER/BROWSABLE/READONLY；官方`A0000020`=FOLDER/HASSUBFOLDER/CANDELETE（Dll.cpp:209）。本地另有IsPinnedToNameSpaceTree=1 | 有差异，尚未证实是阻断原因。[SFGAO][sfgao]及[GetAttributesOf][attributes]允许Browsable对应Bind/View能力，不能把SDK旧注释解读成必错；也不应为贴近样例擅加删除能力。 |
| 根Open与子项菜单 | 本地没有自定义根Shell/Open；官方也依赖默认Folder关联。官方另注册独立的菜单类与FolderViewSampleType（Dll.cpp:217–224） | [NSE注册说明][nse]确认默认Folder verbs。样例菜单类的自定义命令是display（ContextMenu.cpp:26–29），不是“根入口必需的open实现”。不要把子项菜单缺口当成根DLL未加载的已证原因。 |

本地没有WantsFORPARSING，官方注册表同样未设置。该值与根解析名特例有关，不应仅为试错添加。官方回调、搜索和属性schema也不是激活前的强制条件：样例对PSRegisterPropertySchema只作机会性调用（Dll.cpp:299–306），[README][readme]说明未提升注册仍可运行基础namespace。

## 激活后的具体实现缺口

| 缺口 | 本地行号与官方对应 | 对当前阻断的解释边界 |
| --- | --- | --- |
| 解析名/多层文本路径 | ExplorerProof.cpp:114–124仅匹配任意id的单个Name，162–164忽略SHGDNF；[官方:215–277、733–777][parse]按组件递归并区分INFOLDER与绝对解析名 | 已证实子项路径与节点归属处理不足；例如根解析可接受只属于下层的id名称。影响激活后的地址栏/子项导航，不能解释调用本代码之前就未加载。文档允许命名差异，但要求按[ParseDisplayName][parse-api]及[GetDisplayNameOf][name-api]契约审视实际往返。 |
| 多层PIDL绑定/验证 | ExplorerProof.cpp:47–49、135–140仅检查第一个id是否文件夹，随后整段合并并从末段决定node；[官方:312–355][bind]逐层绑定，[1060–1075][pidl]验证自有签名 | 静态可见非法尾项没有逐层拒绝，例如首项为folder而末项为file时仍可能返回Folder对象。是后续契约缺口；本轮两个真实根输入均枚举1项，未出现根node误判。 |
| 子项关联与菜单 | ExplorerProof.cpp:161全部E_NOINTERFACE；[官方:659–729][uiobject]提供默认菜单、图标、IDataObject和IQueryAssociations，文件夹关联加入ASSOCCLASS_FOLDER | 将影响DefView子项的交互/默认动作。root-bind从不请求这些对象；但根的菜单由父Folder/注册关联管理，[GetUIObjectOf][uiobject-api]针对子项，不能直接归因于当前根入口失败。 |
| 实际视图生命周期 | ExplorerProof.cpp:146–151创建DefView对象；Probe.cpp:21–26取得后立即Release | [CreateViewWindow][viewwindow]是创建视图窗口的另一步。对象创建S_OK没有覆盖宿主、枚举展示、后续属性/菜单调用，不能算UI成功。 |
| 可选回调 | 本地SFV_CREATE的psfvcb为空；[官方:573–594][view]及1401–1413使用回调启用搜索行为 | [SFV_CREATE][sfv]明确psfvcb可为NULL，官方注释也称可选；不是足以直接修复发现/激活的必补接口。 |
| 日志含义 | ExplorerProof.cpp:17默认result=S_OK；95/197/105在决策前记录QI/CreateInstance/Initialize | 当前日志的`00000000`常是入口标记，不是所请求接口成功返回。只有例如151的CreateDefView.result记录实际返回。不能根据一串QI零值认为本类支持可选接口；新路径探针记录的是调用返回后实际HRESULT。 |

上述项目可在后续实现中按真实触达的用例修复，不能捆绑成一轮“全补接口”来宣称解决根因。GetDetailsEx对未知key也过度返回BSTR（ExplorerProof.cpp:170–173），属于激活后属性契约审查范围，当前没有到达该阶段的GUI证据。

## 独立正控到底覆盖了什么

旧`--root-bind`（Probe.cpp:7–31）固定解析`::{GUID}`，直接Desktop.BindToObject，跳过父目录发现、ShellExecute/default verb选择，并且不创建实际view window。默认模式的Desktop枚举发生在显式CoCreate之后（Probe.cpp:42–86）；新root-bind模式没有父Desktop枚举。因此“独立绑定成功”原先既未覆盖实际FolderName路径，也未覆盖冷启动发现和GUI宿主条件。

本轮唯一新增[路径判别实验](path-bind-comparison.md)已经补了第一个缺口：同一原注册下，两独立进程分别解析CLSID根与实际sandbox路径，均在Bind阶段加载原DLL，GetClassID为原CLSID，GetCurFolder成功并枚举1项。两者Parse后均尚未加载本DLL；排除了本次调用先在Parse中预加载的猜测。没有运行View/GUI，所以真实活动view身份仍未知。

PIDL长度与ILIsEqual现象须按原记录保留：实际路径Parse后920B、GetCurFolder882B，ILIsEqual返回true。官方文档定义[ILIsEqual为二进制比较][equal]；我已撤回“语义相等”说法。长度取样时点不同，尚未证明原始字节关系或规范化，不为此追加试验。

## 下一项E0：真实活动视图身份（只提出，未执行）

采纳主协调提供的[Raymond Chen官方链路][activeview]：从精确自有Explorer HWND匹配ShellWindows的IWebBrowserApp，再取SID_STopLevelBrowser/IShellBrowser→QueryActiveShellView→IFolderView.GetFolder(IPersistFolder2)→GetCurFolder；并取该Folder的IPersist.GetClassID。[GetFolder][getfolder]和[GetCurFolder][curfolder]是对应API契约。

限制必须具体：新鲜确认唯一自有窗口/原生PID，不能把CUA的opaque id默认当HWND；Windows11若同frame有多标签或匹配多个对象则停止，不取第一项。每个外部探针10秒，上限枚举ShellWindows项目，不读取其他窗口路径/焦点文件名；仅对匹配的自有view输出类GUID、是否匹配自有目标、PIDL长度/比较结果和方法HRESULT。先在自有已知SDK目录取得基线，再在目标视图读取；失败/歧义必须保留，不启新管理员工具或重启Explorer。

判读：真实view若为其他Folder类且目标PIDL匹配，优先定位宿主入口分派；若为本CLSID但0项，才检查Initialize/node、枚举过滤与实际view生命周期；若PIDL/活动tab不匹配，先修观察对象对应关系，不能称缓存根因。即使E0可读，也不扩大成全桌面扫描。

## 官方样例构建准备状态

`.runtime/explorer-official-review`保存17份已验证原件，Category.cpp仍缺失；官方样例未构建、未注册、未运行，未产出官方DLL。主协调把唯一路径判别置于更高优先级后要求收尾，本轮没有为样例追加长编译。不能把源下载或本任务探针构建当作官方样例通过。

后续最小适配应只建立外部VS2022/CMake构建描述，使用原6个cpp及rc、固定SDK10.0.26100和x64，映射旧Release配置的Unicode、/W4 /WX和RuntimeLibrary=0（/MT），去除不存在的VC70升级property sheet及硬编码SDK6.0库目录。原Release|x64未显式列ModuleDefinitionFile，适配须明确链接原.def并核实DllCanUnloadNow/DllGetClassObject/DllRegisterServer/DllUnregisterServer四导出，不能假设项目列入.def就已验证导出。旧x64 postbuild也没有复制propdesc；后续应仅复制资源，不调用注册。以上是从原vcproj/def推导的适配计划，不是实际构建结果；若需语义重写须另行裁决。

本轮只改自己的研究/证据交接文件；原产品、tests、注册属性和平台设置均未改。唯一运行的是获准的两路径只读绑定控制，清理完成；不重跑未变化的全仓库/图片矩阵。研究结果不能关闭G1或宣布Windows可交付。

[registration]: https://github.com/microsoft/Windows-classic-samples/blob/434f6002bdf9cf9829406c3ff2b33387982d6168/Samples/Win7Samples/winui/shell/shellextensibility/explorerdataprovider/Dll.cpp#L209-L224
[mount]: https://github.com/microsoft/Windows-classic-samples/blob/434f6002bdf9cf9829406c3ff2b33387982d6168/Samples/Win7Samples/winui/shell/shellextensibility/explorerdataprovider/Dll.cpp#L276-L293
[qi]: https://github.com/microsoft/Windows-classic-samples/blob/434f6002bdf9cf9829406c3ff2b33387982d6168/Samples/Win7Samples/winui/shell/shellextensibility/explorerdataprovider/ExplorerDataProvider.cpp#L185-L195
[parse]: https://github.com/microsoft/Windows-classic-samples/blob/434f6002bdf9cf9829406c3ff2b33387982d6168/Samples/Win7Samples/winui/shell/shellextensibility/explorerdataprovider/ExplorerDataProvider.cpp#L733-L777
[bind]: https://github.com/microsoft/Windows-classic-samples/blob/434f6002bdf9cf9829406c3ff2b33387982d6168/Samples/Win7Samples/winui/shell/shellextensibility/explorerdataprovider/ExplorerDataProvider.cpp#L312-L355
[pidl]: https://github.com/microsoft/Windows-classic-samples/blob/434f6002bdf9cf9829406c3ff2b33387982d6168/Samples/Win7Samples/winui/shell/shellextensibility/explorerdataprovider/ExplorerDataProvider.cpp#L1060-L1075
[uiobject]: https://github.com/microsoft/Windows-classic-samples/blob/434f6002bdf9cf9829406c3ff2b33387982d6168/Samples/Win7Samples/winui/shell/shellextensibility/explorerdataprovider/ExplorerDataProvider.cpp#L659-L729
[view]: https://github.com/microsoft/Windows-classic-samples/blob/434f6002bdf9cf9829406c3ff2b33387982d6168/Samples/Win7Samples/winui/shell/shellextensibility/explorerdataprovider/ExplorerDataProvider.cpp#L573-L594
[readme]: https://github.com/microsoft/Windows-classic-samples/blob/434f6002bdf9cf9829406c3ff2b33387982d6168/Samples/Win7Samples/winui/shell/shellextensibility/explorerdataprovider/README.md
[junction]: https://learn.microsoft.com/en-us/windows/win32/shell/nse-junction
[nse]: https://learn.microsoft.com/en-us/windows/win32/shell/nse-implement
[sfgao]: https://learn.microsoft.com/en-us/windows/win32/shell/sfgao
[attributes]: https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellfolder-getattributesof
[parse-api]: https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellfolder-parsedisplayname
[name-api]: https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellfolder-getdisplaynameof
[uiobject-api]: https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellfolder-getuiobjectof
[sfv]: https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/ns-shlobj_core-sfv_create
[viewwindow]: https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellview-createviewwindow
[equal]: https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-ilisequal
[activeview]: https://devblogs.microsoft.com/oldnewthing/20040720-00/?p=38393
[getfolder]: https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifolderview-getfolder
[curfolder]: https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ipersistfolder2-getcurfolder
