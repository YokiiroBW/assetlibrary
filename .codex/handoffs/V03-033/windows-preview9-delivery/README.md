# Explorer preview.9 统一交付证据

最终来源1003157，已安装并实测。完整证据见evidence.json与各图片/UIA原始配对；package-manifest为540文件大小/hash读回依据。源包155529819字节，SHA256 574f9c2d307886225f0602c117aa1156253a2850716b21ef9844c054eaaf3d8d。

图片目录120项：images-first/second记录100/20项与相同地址栏；preview-101、preview-cross-back-100、preview-100-zoomed、preview-cross-forward-101记录双向跨页及125→适应100复位；preview-102与gallery-return-102记录键盘同页继续及Esc选中当前文件。images-second-narrow和preview-before-disconnect记录923宽窗口及透明图。gallery-previous-page记录前页重查且旧选择清空。

UIA可能比WGC落后一帧，保留原输出，不把Loading树改写成Ready。after-disconnect是通知处理前立即帧；after-disconnect-settled才是回根清空成功证据。首个未激活窗口截图捕获了遮挡内容，未纳入交付；激活后的窗口画面和实际进程/DLL读回相符。

Core由V03-036 afdc0df构建的真实Host/WebGateway.Tests提供，worker使用已验完整V03-005目录，包含libSkiaSharp；无解码绕过。core-acceptance确认258合成原件hash/mtime、6角色、Host/PG/HTTPS/runtime全清。ready为已退休记录，严禁复用。测试用户Host/connection与两个自有窗口均清，原Explorer PID6212/creation保持；偏好Gallery/200不变。

12图库+13Shell、Setup23、发行锁16、包6、实际SetupCLI8、夹具Python13/C#9通过。V03-035原始焦点/wide夹具失败及夹具分析器初次失败保留，最终修正结果另列。并行代理触发用量限制后root接管V03-034、完成Escape直接路由和同generation重入回归，未把中断当通过。

不宣称无限瀑布流、完整V0.3、签名、Android真机或G4。宽屏33项可见时16MiB预算导致末尾部分缩略图明确不可用；本轮未放宽预算，后续可改进按显示尺寸请求和缓存调度。原Windows11底部计数仍以自有摘要为准。
