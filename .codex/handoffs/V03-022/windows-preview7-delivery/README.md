# Windows Explorer preview.7 统一验收

最终安装包源码 addcaf205aa88af19d858eeae9770bb2f5c9b1ef，原生DLL在新的Explorer进程28748内实际加载，SHA256与包一致；再开进程17596验证偏好恢复。542个安装文件大小/强哈希全部匹配。完整包标识见evidence.json/build-evidence.json。

## 可观察行为

- explorer-large-jpg.jpg、explorer-large-png.jpg、explorer-large-webp.jpg：实际Core→隔离WIC→ALP1→Explorer大图，原生地址栏仍在图片目录。
- explorer-rotated.jpg：EXIF方向6得到竖图；explorer-alpha-narrow.jpg：透明PNG及窄窗口fit。
- explorer-invalid.jpg：坏PNG保留文件名/导航并清旧图，提示不支持预览。
- explorer-return-selection.jpg：Esc恢复图库，landscape.jpg仍选中，摘要10项/已选1。before-logout-preview.jpg→after-logout.jpg：空格重开后退出登录，旧图/页清空回根。
- list-persisted.jpg：列表选择跨分页及进入图片目录保留。preferences-list记录mode1/density176；gallery-200为mode0/200；after-preview记录相同。reopened-process证实新进程；新窗缩小后保存176（若未恢复200会变152），再放大回200。已开旧窗不强推。
- harness-*仅同源合成控件证据，不替代上述真实Explorer。图像截图是捕获的JPEG，未编辑。

## 验证及历史失败

Windows离线120（V03-024）、Setup23、Shell13、Gallery12、包6、锁16、最终CLI8通过。Shell view单独1项通过1.10s，其余12项69.81s；Gallery完整12项1.16s。新增快捷键测试在V03-023同源额外通过，未改运行时代码。

NativeLive初次4行3过1失败：三个类并行登录竞争Core的2个登录并发位（同时还有20次/分钟规则），并非图片解码错误。保留initial日志/TRX。图片测试改为单次fixture登录顺序验证512/1600，并用DoNotParallelize避免第三个并发登录；最终该方法1/1通过，其余两个未变方法沿用初次通过。没有放宽安全配置或自动重试登录。

Cua的即时辅助技术摘要有时滞后一帧，截图状态与下一次读取可不同；未把该缓存摘要误当产品数据。独立MTA用例直接验证当前UIA Pane/Image、旧provider拒读及恢复List。

## 清理与边界

core-cleanup确认148个合成源hash/mtime未变、6角色/临时运行目录/HTTPS监听/服务进程全部清理。connection-cleanup/windows-cleanup确认测试配置、Host、Settings与5个自有窗口已移除；原Explorer6212创建时间未变。安装与显示偏好（Gallery/200）保留；不强制终止空闲Explorer或删除被占用旧DLL。

G1/G2/G3沿用既有实测，G4用户豁免未做。本轮不改变NAS图片隔离限制，不宣称签名、原件编辑/同步、缩放/平移、时间轴或完整V0.3/Android真机完成。性能只有有界组件和一次进程采样，未将采样称为长期峰值证明。
