# Explorer preview.8 实机交付

最终67affa7包已安装，540文件大小/强哈希逐项匹配；新测试Explorer PID5880/HWND724396实际加载preview.8 DLL。原Explorer6212/HWND133910创建时间不变。所有输入只针对本轮窗口；不操作原窗口、NAS或个人资产。

真实Core→Host/WIC→Explorer图片确认JPG/PNG/WebP、方向修正和透明。截图记录适应83%、预览100%、滚轮125/195、鼠标/Shift箭头平移；窄窗口923截图像素宽仍100%，按0重算适应69%。+键后86%切PNG回69%；无效图片立即清旧像素并禁用缩放。Esc恢复原选择和滚动位置；退出清理放大的透明图。更换图片、关闭和缩放均不写偏好，原Gallery/200记录逐字不变。

保存的UIA树可能比同次WGC画面滞后一帧，如preview-actual的树仍旧83%而画面已100%；未篡改树来制造一致。后续wheel等稳定树与画面一起确认状态。preview-narrow-actual是最初拖边未改变窗口的尝试，真正resize证据是preview-resized-actual/fit。gallery-before来自第一次不完整测试worker的失败，不当作最终图库通过；gallery-complete是正确Core实例。

首个fixture错误指向native编译输出，仅含EXE而缺libSkiaSharp.dll。发现全图不可用后立即disconnect/明确stop，148原件与6角色/Host/PG/HTTPS/runtime清理通过。改用V03-005/.runtime/gallery-server-20260913/worker完整已验产物重开新有界实例，最终再次148原件hash/mtime不变及6角色等清理通过。没有把测试包装问题当客户端修复、没有更改NAS或降低解码隔离。

三个自有窗口4984286、724396、2164000均关闭；正式安装保留，Host进程0，测试connection.json仅匹配该fixture origin后删除，remembered.bin不存在。旧preview.1占用仍列pendingCleanup，exit3010不表示新安装失败，未强删DLL。没有自动化认证表单/系统卸载UI，不声称完整V0.3、G4或新物理多显示器DPI验收。

同源隐藏控件与几何13个CTest、Shell13个（包括root新增无重复I/O/偏好写入断言）、Setup23、锁16、包6及最终真实Setup CLI8通过。具体构建与子任务结果见summary/tests及V03-031/032。新源码只有Windows呈现/构建与文档，服务端和Host协议复用既有行为。

二进制文件按原字节SHA256；文本哈希按UTF8去BOM/CRLF规范LF。构建/诊断原日志保持，少量行尾空白只在归档副本规范化，采集原件留在.runtime。

原本扩展名为.json的空CLI输出流在归档中命名为.log，保持空内容，避免把空流伪装成结构化JSON。
