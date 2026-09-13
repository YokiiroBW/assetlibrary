# 同 NAS 图片预览交付

状态：已部署并验收。2026-09-13，运行地址仍为 https://192.168.31.210:5443，部署ID及原账号、两份库、195792项完成扫描保持。最终包源码7f092904dbadcf1276bb9daf5b565946ecccbdad；后续Git提交只记录交付。

Core、PostgreSQL及图片服务在同一NAS的同一Compose内运行。图片服务没有网络、资产、Core私密状态或Docker socket挂载，Core只发送已授权的有界图片字节。额外主机仅用于构建，运行图片不依赖它。

最终包CLI initialize/start/status通过；真实Core38项图片请求（含正常出图、格式拒绝、4000万像素、32MiB像素数据、PNG附加块边界），图片服务停止时基础目录仍200、图片503，恢复后回到预期415。桌面1440px与手机390px最终页面无注入样式，缩略图、大图、Esc和退出清图通过。20份合成文件hash/mtime/长度不变；3测试容器、5卷、2网络、临时部署目录、原型IPC/四镜像及NAS合成目录已清。

上线后另以原账号查询两张库内真实图片的512/1600共4次PNG，验证CRC/尺寸成功，不保存其图片、文件名或路径。三个正式容器健康，Core仍1654及读取组101、原资产只读；image为仅持有4项明确能力的可信PID1，其decoder为1655/cap0。Socket选择已持久保存。

原NAS没有seccomp；ADR-0023新增不同身份/namespace的同机隔离路径，没有移除原seccomp检查。最大图问题另由GC虚拟地址预留占用修复；PNG巨大非IDAT块的渐进拷贝代价以4MiB准入限制控制。未提高AS512MiB、memcg512MiB、CPU3秒或GC提交64MiB。详见ADR与V03-027/028交接。

软件冷备包含四个旧专用卷和原配置，共5个归档，逐个tar compare与SHA256验证；不包含资产原文件。备份与正式包保留在NAS私密运维目录。回滚流程已写明但没有故意回滚已上线环境。

NAS直连官方口令风险服务的旧网络问题在新建合成管理员时再次出现；只为此次初始化使用一次性、不解密TLS的限定转发，之后进程和监听关闭。现有账号登录及图片服务已在关闭转发后验证，不依赖该转发。

desktop-clipped-before.png是此前样式的失败证据。新测试断言图片元素必须完整落在78px缩略图框内；修复后复用同一图片尺寸规则。最终desktop-gallery/mobile-gallery截图为已部署样式，browser.json的candidateStyleInjected=false。

文本证据按UTF-8去BOM、CRLF规范为LF后计算digests.json；PNG等二进制按原字节。包images.tar的SHA256按原字节，不做文本规范化。一般测试里的平台/缺夹具跳过仍保留，不能视为完整V0.3或Alpha通过；G4用户豁免未执行。

归档的browser-clipping-reproduction.log与nas-parent-host-proof.py.txt仅另去除行尾空白/多余末尾空行，.runtime保留采集副本；诊断内容与脚本语义未改。
