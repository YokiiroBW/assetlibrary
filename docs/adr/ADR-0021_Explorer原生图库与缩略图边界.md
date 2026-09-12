# ADR-0021 — Explorer原生图库与缩略图边界

状态：Accepted for controlled implementation，2026-09-13。用户要求先完善Windows浏览体验，尤其图库瀑布流。最终默认启用须先通过同源独立窗口验证，再通过真实Explorer与真实Core图片夹具验收。G4仍为用户豁免未测，不重新安排。

## 视图

保留原生Explorer外壳、树、地址栏和历史，C++ IShellView在CreateViewWindow中创建同进程Win32子HWND。仅右侧内容区自定义；不跨进程SetParent、不加载WinUI/.NET/网页、网络或媒体解码器到Explorer。采用Windows GDI绘制已校验未压缩PBGRA，透明背景使用标准AlphaBlend；新增依赖限系统gdi32/msimg32/oleacc，不引入第二UI框架。纯布局计算与同一控件先在独立测试harness验证，该harness不是交付客户端。

按照视觉稿实现保宽高比、行边缘对齐的拼图图库，可调密度并切换列表布局；文件夹有独立清晰的导航呈现，分页继续明确标为导航。仅处理当前有界页，绘制和辅助技术查询不访问IPC；视口外图片及时释放，只对当前可见普通文件尝试缩略图。图片端点决定真实格式，不从扩展名推断身份或支持能力。无尺寸时用占位比例，收到真实派生图尺寸后重排并保留滚动锚点。

复用Folder/PIDL/快照v1、当前页排序与既有导航用例。自定义IShellView/IFolderView负责选择、键盘、上下文菜单、刷新和焦点，不能假定DefView自动提供这些行为。保持根目录原有DefView入口可用；图库视图与列表模式在右侧提供明确选择，不用全局可变单例串联不同窗口。自绘控件提供IAccessible/MSAA列表/项目语义，由系统UIA桥接读取，或实现等价的原生UIA；不把无语义的画布当可访问界面。UIA/MSAA查询必须只读内存、失效后不能返回旧名称/图片；用实际外部客户端验收。

失效复用同一生产root的公开SHChangeNotify，原生自定义视图使用SHChangeNotifyRegister/NewDelivery及Lock/Unlock。收到通知先清图像、选择、详情和可访问数据，再有界刷新或回根；晚到结果按view generation和epoch丢弃。销毁不等待网络/解码/工作线程，不得释放仍被异步I/O或辅助技术持有的DLL；四活动视图和显式资源上限保持约束。常驻界面不加入无限Ready轮询；加载重试有截止时间，F5明确开始新一轮。

## 缩略图

保留现有64KiB snapshot v1。新增独立 [thumbnail v1](../../contracts/windows-shell/thumbnail-v1.md)，单图片请求仅在后台执行，管道连接的关闭就是取消，不另造Host视口表或图片磁盘缓存。Host保留现有node对应的Core稳定双UUID，始终使用已批准派生PNG端点。页面查询与图片共用同一认证HTTP会话及总计两网络许可；第一版只允许一个图片任务占用网络许可，保留导航容量。JSON仍8秒/1MiB，图片独立20秒/2MiB。

系统WIC只在短时无凭据的Host辅助进程模式运行，固定PNG解码器和PBGRA格式。常驻Host仅做有界PNG容器验证，不在持有会话的进程里调用图像解码器；辅助进程只接匿名管道字节、不接路径/URL/账号，使用Job、内存/子进程限制和最多3秒解码外限（且服从剩余20秒总期限）。辅助进程是崩溃/资源隔离，不声称同用户进程构成强安全主体隔离。NAS原始文件的Provider硬隔离要求不改变。

Shell每视图保留最多16MiB可见图片、整个模块最多64MiB，最多两个后台图片IPC操作；当前页最多101项。真实可见集合包括当前页全部可见普通文件，每视图最多16个未完成图片请求、整个模块最多64个，完成后继续接纳余下可见项；成功和失败在当前视口ticket内不自动重试。达到图片字节预算的条目明确显示预览不可用，不能一直等待。2026-09-13宽屏实机发现原先把整个可见集合截成16项会令同屏第17项以后永久饥饿，故将16明确为未完成请求批次上限，协议字节和内存上限不变。UI等待网络为零，原快照查询150ms契约不变。关闭/导航/退出/视口变化取消无用请求；Host检测客户端断开并取消对应HTTP和解码。无Provider、404未部署、415、损坏/超限等诚实保留文件信息和预览不可用，不读取原文件替代。401/403撤销图像并复用会话清空；图片404不触发整个账号退出。

## 交付与验证

V03-018处理原生标题/排序；V03-019拥有图库控件/布局/可访问性及独立harness；V03-020拥有Core客户端二进制传输、Host投影/管道/解码辅助；V03-021拥有Shell视图与后台IPC接线。root单一拥有ADR/contracts、根依赖/CI/版本、集成和实机。每任务独立worktree、先头文件/契约再并行代码，完成各自handoff。

先在无注册harness验证宽窄、高DPI、不同宽高比、空/加载/错误/键盘/选择/可访问性，再接真实Core派生图与真实Explorer验证导航、可见范围取消、旧图清理、缩放/滚动响应与实际资源回收。无原始NAS写入或隔离降级；现NAS缺预览能力保持显式限制，不用假图片冒充NAS派生图。

官方依据：[命名空间自定义视图](https://learn.microsoft.com/en-us/windows/win32/shell/nse-works)、[IShellView](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ishellview)、[AlphaBlend](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-alphablend)、[MSAA与UIA桥接](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-msaa)、[公开Shell通知](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shchangenotifyregister)、[WIC系统类](https://learn.microsoft.com/en-us/windows/win32/wic/-wic-guids-clsids)。
