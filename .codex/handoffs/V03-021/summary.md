# V03-021 — Explorer 图库视图接线交接

状态ready_for_review（组件/接线；不等于默认安装或真实Explorer验收）。工作区C:/YOKI/Codex/AssetLibrary-worktrees/V03-021，分支codex/v03-021-explorer-gallery-view。

## 本任务提交与依赖

- client/Requests：a1021f2c585fe7750b8cade770987f8d26565af4，root已合为0112fb4。
- View接入：dbb4a42fd44db38829a1251a8cced4b7e6d40714。
- 同屏完整加载与共享像素预算：d6ea45fc4776544b17c07d86b2ba3a33052fcc7c；消费Surface f9acd9f和裁剪/测试frame 037457a。root已有前两笔实现，只需取此修复及交接更新。
- provider独立Folder保活接线：b57c7f250c56f812df8e6c7ed263d58c2415c27d（root已合）；最终状态栏与MTA UIA回收：2515309b5c0d265da997995a1b8f788c4b8dd9a0。
- 本交接为后续独立提交；请按上述本任务提交合并，不重复合入分支中其它owner的共享提交副本。

明确消费的其它owner源：Surface.h582b907；thumbnail独立向量7fae7a0；主题8ec1fed/4697374；Surface实现31f26c2、生命周期fd17678、空候选显隐通知831dc34、Shown接口406c958。Surface/Layout/Accessible代码由V03-019维护，Host/Session/解码由V03-020维护，本任务未修改其实现或根contracts/CI/版本。

最终原生UIA消费Surface391f3ba、62364c2、ab50a05、124df8e、4eede49、7446e99、d215431、f2ad8e3；root明确批准回放9d46429/669f494的uiautomationcore/ADR/import和Uia.cpp构建接线。这里不重复创作或合并其它owner源。

## 完成内容

生产根保持原DefView；生产非根library/directory/next-page返回真实IShellView/IFolderView，创建同进程Win32子HWND并使用同一Surface图库/列表控件。Proof保留原DefView。当前页在UI内复用Folder.CompareIDs排序，PIDL、导航菜单、系统图标语义与Settings入口复用现有适配；不通过文件名判断支持格式或身份，不打开原文件、不提供写入/剪贴板/拖放成功假象。未实现的视图持久化、自由摆放等明确E_NOTIMPL。

自定义View负责窗口/焦点/选择/模式、IFolderView内存枚举、GetItemObject、默认导航/背景菜单、Refresh与销毁。UIActivate失去焦点不隐藏页面。首次/每次进入非根默认Gallery；目录切换暂重置模式/密度，按root决定不推断其它自定义视图或新增全局偏好。

状态栏采用公开IShellBrowser::SetStatusTextSB：Loading/失效复用Snapshot状态文案，Ready显示“当前页 N 项，已选 M 项”，不把NextPage/StatusRow当资产计数，分页另提示。Clear/Apply/选择/激活刷新文字，Destroy只在仍active时清空。每次写前QueryActiveShellView核当前View，并在COM调用后复验HWND/generation/browser/revision；重入最多重读一次，旧视图不覆盖新active视图。host不支持状态接口时不干扰浏览，不操作分栏、Explorer私有XAML或全局设置。此公开接口为微软[自定义Folder View状态栏契约](https://learn.microsoft.com/en-us/windows/win32/lwef/nse-folderview)，活动视图判定依据[QueryActiveShellView](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellbrowser-queryactiveshellview)；最终Windows11宿主显示由root实机验收。

page和thumbnail均在后台；Paint、辅助技术和输入不等待IPC。后台只持plain共享State、取消event、不可变结果及HMODULE pin，绝不从后台AddRef/Release Folder/View/Surface COM。page最多4工作，Loading固定10s/20次尝试，Ready不轮询；图像专用池最多2线程、最多64待处理ticket/module。当前页最多101个可见File候选，每视图最多16个未完成ticket；正常结果经UI取走且任务真正退出后释放slot，取消任务也必须退出才归还slot。离视口ticket立即取消，窗口实际隐藏/销毁取消page与图像；显示或F5开始新generation。已取出的完成批次也逐项复验当前ticket，防止重排/滚动重入后旧响应恢复像素。

同一视口成功/失败结果均保留terminal ticket，避免反复请求。View完成消息处理后，在相同HWND/generation且未retired时补一次Viewport，候选集合不变也继续后续批次。旧generation取消任务收尾只发当前generation的无payload唤醒以回收slot，不重新发布旧图片，不增加timer/轮询或线程。Surface超预算本项主动标不可用，View不重复调用。

thumbnail-v1严格校验magic/version/type/长度/request-id、epoch/node、1..512尺寸、stride/format、最多1MiB及真实premultiplied BGRA。独立20s总后台预算、不等EOF，取消后至真实I/O完成前保留缓冲/句柄/DLL；最多2个在途或待回收操作。SnapshotPipe的150ms/64KiB/frame parser/G3回收主体不放宽；实际TokenUser、session、持有服务进程和Identification SQOS共用LocalPipePeer。没有网络库、媒体解码器或新Host视口表。

root DefView与custom View共用LoadingRefresh的同一四活动视图atomic配额，准确一次归还。DllCanUnloadNow纳入后台工作及thumbnail保留I/O。每RequestState持有独立plain原子像素预算，CAS按pixels.capacity()接纳validated图片；aliasing shared_ptr的lease持原始图片和预算，最后引用先销毁原图片再归还bytes。预算跨generation不重置，不持COM/HWND/View/RequestState，覆盖Surface、完成队列和已取出的批次合计16MiB/view，而不是各自16MiB。预算不足产生无像素Busy终态，旧lease真正释放才允许新请求。四活动视图上限对应64MiB；Surface本地16MiB仍为第二校验。最多两个后台图像I/O回调，每个可暂持最多1MiB像素的wire帧（另有固定协议头）及最多1MiB尚未接纳的PBGRA校验副本；这些受I/O并发限制的临时缓冲独立于leased驻留预算，不宣称整个进程图像相关内存只有16MiB。

公开SHChangeNotifyRegister(NewDelivery)+Lock/Unlock监听实际资产库root，清像素、选择、page和可访问数据后才回root；不携带借用完成指针。生产notification root仍取actual absolute的首段。root批准的内部非导出测试参数只供用自有Temp目录的完整系统PIDL验证通知，不从COM、配置、环境或注册接收覆盖值。

## 生命周期修复与验证

- 最后Release在真实引用尚存时先Destroy Surface，避免零引用时callback owner复活/double-delete；正常与外部HWND销毁均幂等。
- Create在第一次外部COM调用前设置creating guard并捕获generation；GetWindow嵌套Create、incoming AddRef重入Destroy及Destroy期间重建均拒绝，不重复占配额。
- Apply在入口捕获HWND/generation；CompareIDs前后及赋page前复验。旧页不能被贴上重入Refresh的新generation，E_ABORT也不清掉新Loading。
- Surface的Shown()在WM_SHOWWINDOW时已更新，View联合祖先原生可见性统一用于Viewport/Refresh，避免事件早于本窗口style更新造成隐藏漏取消/显示漏查询。
- callbacks.lifetimeOwner仍是View；providerLifetimeOwner是独立Folder，只保DLL，不拥有View/Surface。外部MTA client持root/child跨显式Destroy和最后Release时，View引用可归零并释放slot；客户端离场后有界STA pump验证Folder/browser回1、provider/pendingRetirements/dispatcherWindows全0，旧Name及Selection无旧数据。实际生产DLL的隐藏空View也经过MTA root访问，View返回0后DLL暂S_FALSE，client释放和退役后DllCanUnloadNow恢复S_OK；隐藏父窗没有启动IPC。

严格Release /W4 /WX /permissive- /analyze /utf-8构建通过。完整12CTest全部通过69.14s；之后仅补强incoming AddRef前generation捕获及专门负控，最终严格重建和gallery_view重验通过0.23s。最终verify_repository通过（482架构输入、21迁移manifest和14架构测试），Alpha保持blocked。详细命令和失败纠正见tests.md。

同屏修复后严格构建和受影响Requests/View/imports 3/3通过1.46s；新增真实View假Sources的29同屏、4×29管道、失败不重试、旧取消slot唤醒新generation及跨generation驻留预算回归。最终2515309严格构建与View/imports 2/2通过0.63s，包括全部旧View回归、新100+分页状态/多选/重入及外部MTA UIA回收。没有重复运行未改的旧9项或真实pipe20s测试。最终生产DLL：.runtime/explorer-gallery/product-shell/Release/AssetLibrary.Explorer.dll，SHA256 E054E952ABD6B6EFEB767055143923919363977F92F97909885548773EDDEF58。受限imports含root批准的gdi32/msimg32/oleacc/uiautomationcore；无动态CRT、网络/媒体/.NET依赖。最终整包须按实际重建hash验收。

## 架构与安全影响

依赖仍为Shell展示适配→版本化本机投影→Host/既有Core用例。复用Folder/PIDL/排序/导航/Settings和同源Surface，无权限/路径/传输领域规则复制，无数据库、主语言/框架或第三方依赖新增。两种独立frame/预算的OVERLAPPED回收机制沿用相同已证明模式；安全peer检查仅一份。

当前页≤101项，排序/名称仅处理有界PIDL链；无50万项遍历或全量索引。原文件、真实资产、NAS、凭据与Cookie未进入Shell操作。本任务仅native pipe合成测试、自有Temp目录通知、批准的无激活屏外测试窗口；没有操作前台/已有窗口或注册/安装生产类。

## 未完成/风险

真实Explorer+真实Core图片、图库/列表输入和实际退出清理、祖先窗口行为及外部辅助技术运行环境仍由root整包验收。本任务的假Sources/屏外测试不是实际Explorer或NAS预览。先前系统MSAA桥的保留引用方案已被Surface自有原生UIA替换；本任务的外部client跨View退出和实际DLL回收测试已通过，不把旧失败方案继续当现实现。最终状态栏文字在Windows11实际宿主是否显示仍需root验收，接口不支持时保持浏览可用。

G4仍用户豁免未测；未跑20轮/8小时。模式跨目录暂不保留，完整预览/下载/资产写入/全部V0.3未交付。root已有此前checkpoint，最终建议合并最新Surface/root构建提交 → b57c7f2（已合）→ 2515309 → 本交接；root负责版本和安装包，不覆盖已安装同版本DLL。

公开依据：[IShellView](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ishellview)、[SHChangeNotifyRegister](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shchangenotifyregister)、[WM_SHOWWINDOW](https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-showwindow)、[CloseThreadpoolWork](https://learn.microsoft.com/en-us/windows/win32/api/threadpoolapiset/nf-threadpoolapiset-closethreadpoolwork)、[CloseThreadpool](https://learn.microsoft.com/en-us/windows/win32/api/threadpoolapiset/nf-threadpoolapiset-closethreadpool)。
