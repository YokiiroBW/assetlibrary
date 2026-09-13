# V03-032 — Explorer预览交互

ready_for_review。分支codex/v03-032-explorer-preview-interactions；源码checkpoint410b5f0e74b1a28cfc1587c3ef316e8b3fdbf2f2，随后补全启用8项Tab负控与本交接。没有操作GUI、Explorer注册/安装、Host/IPC/NAS或G4，真实Explorer验收由root继续。

## 实现与复用

只编写Surface.h/.cpp、PreviewTests.cpp和RenderingTests.cpp。复用root冻结ADR0024/PreviewViewport接口与V03-031真实几何实现；按明确授权回放31d81c7为efee00a、root构建1ba6314为f7d7705，不自行维护数学/共享CMake或改View。

Surface State持有PreviewViewport、滚轮余量和拖动点，变换O(1)，无放大像素缓存。原生按钮107返回/108上一张/109下一张不变，新增110缩小/111放大/112适应窗口/113预览100%；公开常量PreviewZoomOutControlId、PreviewZoomInControlId、PreviewFitControlId、PreviewActualControlId。Tab容纳canvas+7，跳过disabled并在首尾返回宿主。

滚轮120刻度1.25，零刻度不变、余量累计，按有符号屏幕坐标转换锚点。Canvas消费自身wheel；原生按钮获焦时依默认父链到Surface root消费一次，不手工重复转发。Ctrl滚轮可用，Alt滚轮交宿主。+/−及数字键盘以图片区中心缩放，0/Fit、1/Actual、双击切换Fit/100%，Shift方向平移；普通左右仍只发切图回调，Ctrl/Alt快捷键不占用。

摘要由当前几何显示Fit/缩放百分比、当前派生图尺寸及100%含义，保留View传入状态；失败/Loading无旧比例。工具栏原生文本和既有预览Model提供辅助功能信息；缩放/拖动只更新文字、按钮和重绘，不触发图片请求或偏好写回。任务不增加状态getter或COM接口。

绘制仍创建原图尺寸的单次DIB，最多10,240,000B；仅改变AlphaBlend目标矩形并clip到图片区。原thumbnail contain路径保持原值/调用行为，持久16MiB容量不变。没有在Explorer内网络、原件访问、解码或原件写入。

Begin/switch/SetPreview/Clear/Hide/End/Destroy清图/几何、复位滚轮并释放capture；CancelMode、实际失焦和捕获丢失只终止拖动。所有捕获/焦点操作允许同步删除Surface；State/owner沿用既有保活，预览局部revision和pageRevision防止callout后继续旧操作。End仍恢复原浏览选择/焦点/滚动，不保存每图缩放。

## 验证与边界

MSVC19.44、SDK10.0.26100.0、/W4 /WX /permissive- /analyze /utf-8 /MT：Surface静态目标先通过，再用真实几何链接所有同源测试目标。最终本线程12个既有控件CTest全部通过，预览新回归和渲染各1项，其余10项含原生UIA/MSAA外部客户端/退役/选择、预算、浏览恢复；不把复跑一项计数为新增。纯geometry专项另由V03-031证明，未冒记本线程执行。

定向测试用真实隐藏自有HWND和合成PBGRA：绿色中心标记证明锚点与拖动位移；碎片/反向/按钮父链wheel；10%/400%禁用边界；实际SetFocus/SetCapture丢失及CANCELMODE；SetCapture/SetFocus同步删除Surface；7按钮全部启用加canvas8项Tab和disabled跳过；switch/失败/hide/权限/迟到拒绝；1600²图在400%只覆盖图片区；32次放大绘制GDI/USER不增长。最终owner/provider/dispatcher均回基线。

官方依据：[WM_MOUSEWHEEL](https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-mousewheel)的120/碎片、signed坐标和默认父链；[WM_CAPTURECHANGED](https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-capturechanged)明确ReleaseCapture自身也发送此消息，因此先退休拖动状态，回调内不重获capture。

建议root已有31d81c7/1ba6314后，仅pick410b5f0和本任务收尾提交，不重复回放其它owner代码。最终真实Core→Host→Explorer、不同DPI/窗口尺寸/输入设备由root验收；此次没有扩大授权、源图片和请求预算，也没有宣布完整V0.3。
