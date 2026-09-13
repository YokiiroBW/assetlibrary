# V03-030 — Explorer 缩放版交付

状态ready_for_review，preview.8已构建、安装并完成真实Core/Explorer验收，V03-031/032已并行集成。运行包源码67affa7ddbb43f633aa611aae0f5a1860c7e8582；后续仅交付记录。证据见[windows-preview8-delivery](windows-preview8-delivery/README.md)。

Windows入口仍为原生Explorer右侧：滚轮锚点缩放、左键平移、缩小/放大/适应/预览100%按钮，+/−、0/1和Shift箭头键盘替代，双击切适应与100%。显式10%..400%；100%按当前派生预览设备像素，不读取原件。窗口缩放保留显式比例/中心或重算fit；切图/失败/隐藏/关闭/权限清理复位，Esc恢复原选择与滚动。

几何与控件复用原Surface/GDI/nativeBUTTON/UIA/MSAA；PreviewViewport只有少量数值状态，无新位图/文件/网络。仍为单张1600 PBGRA、16MiB持久容量及源尺寸10,240,000B临时DIB；放大只改目标矩形并clip，不按400%分配新位图。Root View计数测试确认zoom/reset不增加preview/thumbnail/page请求或偏好写入；控制器保持捕获丢失/失焦/cancel及同步销毁安全。没有新依赖、框架、服务器/Host/协议/数据库变化。

严格MSVC构建，13个Shell CTest、13个Gallery/Geometry CTest、Setup23、发行锁16、包6及最终SetupCLI8通过。已安装540文件大小/强哈希一致；新Explorer实际加载.8 DLL，真实三格式、旋转、透明、100/125/195%、鼠标与键盘平移、窄窗、切图清状态、坏图及Esc/退出均留证。两轮独立Core样例各148原件hash/mtime不变、6角色及Host/PG/HTTPS/runtime清理通过；第一轮测试worker缺DLL的包装错误已记录并改为完整既有产物，新实例不冒用旧ready/PID。

最后Host0、测试连接删除、remembered不存在、3自有窗口关闭；原Explorer6212创建时间未变，Gallery/200偏好逐字保持。安装保留，旧占用preview.1仍待清理；不强杀Explorer、不操作NAS、不自动输入认证表单。

性能O(1)变换/当前101项有界浏览，50万资产不新增工作；无原件写入或业务规则复制。实际新物理跨显示器DPI/触摸设备未测；纯几何设备像素与已有DPI边界测试不当作该实机证据。G4用户豁免，G1/G2/G3沿旧实测，不宣布完整V0.3/Android真机/签名/同步/原件编辑完成。

推荐将此协调分支整体fast-forward到main，不重复cherry-pick子任务；发行锁唯一变化为project版本范围，第三方包hash不变。当前无本任务实现TODO。
