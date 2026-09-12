# V03-021 — Explorer 图库视图接线交接

状态ready_for_review（组件/接线；不等于默认安装或真实Explorer验收）。工作区C:/YOKI/Codex/AssetLibrary-worktrees/V03-021，分支codex/v03-021-explorer-gallery-view。

## 本任务提交与依赖

- client/Requests：a1021f2c585fe7750b8cade770987f8d26565af4，root已合为0112fb4。
- View接入：dbb4a42fd44db38829a1251a8cced4b7e6d40714。
- 本交接为后续独立提交；请按上述本任务提交合并，不重复合入分支中其它owner的共享提交副本。

明确消费的其它owner源：Surface.h582b907；thumbnail独立向量7fae7a0；主题8ec1fed/4697374；Surface实现31f26c2、生命周期fd17678、空候选显隐通知831dc34、Shown接口406c958。Surface/Layout/Accessible代码由V03-019维护，Host/Session/解码由V03-020维护，本任务未修改其实现或根contracts/CI/版本。

## 完成内容

生产根保持原DefView；生产非根library/directory/next-page返回真实IShellView/IFolderView，创建同进程Win32子HWND并使用同一Surface图库/列表控件。Proof保留原DefView。当前页在UI内复用Folder.CompareIDs排序，PIDL、导航菜单、系统图标语义与Settings入口复用现有适配；不通过文件名判断支持格式或身份，不打开原文件、不提供写入/剪贴板/拖放成功假象。未实现的视图持久化、自由摆放等明确E_NOTIMPL。

自定义View负责窗口/焦点/选择/模式、IFolderView内存枚举、GetItemObject、默认导航/背景菜单、Refresh与销毁。UIActivate失去焦点不隐藏页面。首次/每次进入非根默认Gallery；目录切换暂重置模式/密度，按root决定不推断其它自定义视图或新增全局偏好。

page和thumbnail均在后台；Paint、辅助技术和输入不等待IPC。后台只持plain共享State、取消event、不可变结果及HMODULE pin，绝不从后台AddRef/Release Folder/View/Surface COM。page最多4工作，Loading固定10s/20次尝试，Ready不轮询；图像专用池最多2线程、最多64待处理ticket/module、每视图16个当前可见File候选。离视口ticket立即取消，窗口实际隐藏/销毁取消page与图像；显示或F5开始新generation。已取出的完成批次也逐项复验当前ticket，防止重排/滚动重入后旧响应恢复像素。

thumbnail-v1严格校验magic/version/type/长度/request-id、epoch/node、1..512尺寸、stride/format、最多1MiB及真实premultiplied BGRA。独立20s总后台预算、不等EOF，取消后至真实I/O完成前保留缓冲/句柄/DLL；最多2个在途或待回收操作。SnapshotPipe的150ms/64KiB/frame parser/G3回收主体不放宽；实际TokenUser、session、持有服务进程和Identification SQOS共用LocalPipePeer。没有网络库、媒体解码器或新Host视口表。

root DefView与custom View共用LoadingRefresh的同一四活动视图atomic配额，准确一次归还。DllCanUnloadNow纳入后台工作及thumbnail保留I/O。Surface可见像素缓存限制16MiB/view、64MiB/四视图由其同源实现执行；本机在途帧另有2×1MiB的独立硬上限。

公开SHChangeNotifyRegister(NewDelivery)+Lock/Unlock监听实际资产库root，清像素、选择、page和可访问数据后才回root；不携带借用完成指针。生产notification root仍取actual absolute的首段。root批准的内部非导出测试参数只供用自有Temp目录的完整系统PIDL验证通知，不从COM、配置、环境或注册接收覆盖值。

## 生命周期修复与验证

- 最后Release在真实引用尚存时先Destroy Surface，避免零引用时callback owner复活/double-delete；正常与外部HWND销毁均幂等。
- Create在第一次外部COM调用前设置creating guard并捕获generation；GetWindow嵌套Create、incoming AddRef重入Destroy及Destroy期间重建均拒绝，不重复占配额。
- Apply在入口捕获HWND/generation；CompareIDs前后及赋page前复验。旧页不能被贴上重入Refresh的新generation，E_ABORT也不清掉新Loading。
- Surface的Shown()在WM_SHOWWINDOW时已更新，View联合祖先原生可见性统一用于Viewport/Refresh，避免事件早于本窗口style更新造成隐藏漏取消/显示漏查询。

严格Release /W4 /WX /permissive- /analyze /utf-8构建通过。完整12CTest全部通过69.14s；之后仅补强incoming AddRef前generation捕获及专门负控，最终严格重建和gallery_view重验通过0.23s。最终verify_repository通过（482架构输入、21迁移manifest和14架构测试），Alpha保持blocked。详细命令和失败纠正见tests.md。

生产DLL：.runtime/explorer-gallery/product-shell/Release/AssetLibrary.Explorer.dll，SHA256 596F736528ADFC5C913D8F88B8B6C414C5A5121E26A4624E1745880E0054EEE7。受限imports允许原八系统库加ADR批准的gdi32/msimg32/oleacc；无动态CRT、网络/媒体/.NET依赖。最终整包须按实际重建hash验收。

## 架构与安全影响

依赖仍为Shell展示适配→版本化本机投影→Host/既有Core用例。复用Folder/PIDL/排序/导航/Settings和同源Surface，无权限/路径/传输领域规则复制，无数据库、主语言/框架或第三方依赖新增。两种独立frame/预算的OVERLAPPED回收机制沿用相同已证明模式；安全peer检查仅一份。

当前页≤101项，排序/名称仅处理有界PIDL链；无50万项遍历或全量索引。原文件、真实资产、NAS、凭据与Cookie未进入Shell操作。本任务仅native pipe合成测试、自有Temp目录通知、批准的无激活屏外测试窗口；没有操作前台/已有窗口或注册/安装生产类。

## 未完成/风险

真实Explorer+真实Core图片、图库/列表输入和实际退出清理、祖先窗口行为及外部辅助技术运行环境仍由root整包验收。本任务的假Sources/屏外测试不是实际Explorer或NAS预览。Surface owner正在调查v6/PMv2环境下外部MSAA provider的一份保留引用，root明确该项未过前不默认安装；本交接不把它记为已回收或关闭。

G4仍用户豁免未测；未跑20轮/8小时。模式跨目录暂不保留，完整预览/下载/资产写入/全部V0.3未交付。本任务建议合并顺序：共享主题/Surface/contract → client a102 → View dbb4 → 本交接；root通过后续真实门禁再改版本/安装包。

公开依据：[IShellView](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ishellview)、[SHChangeNotifyRegister](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shchangenotifyregister)、[WM_SHOWWINDOW](https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-showwindow)、[CloseThreadpoolWork](https://learn.microsoft.com/en-us/windows/win32/api/threadpoolapiset/nf-threadpoolapiset-closethreadpoolwork)、[CloseThreadpool](https://learn.microsoft.com/en-us/windows/win32/api/threadpoolapiset/nf-threadpoolapiset-closethreadpool)。
