# V01-021 — NAS Docker 服务端与第一版 Web 交付

已完成用户指定里程碑。真实 NAS 入口为 **https://192.168.31.210:5443**，Core 与 PostgreSQL 两个容器 healthy；不是开发机替代部署。图片 `/volume1/Pictures` 与文档 `/volume1/Documents` 已按只读挂载登记，均未自动开始全库首次扫描。登录和手动扫描说明见 [NAS Web 使用说明](../../../docs/releases/NAS_READ_ONLY_WEB.md)。

## 来源与架构

独立分支 codex/v01-021-nas-docker-web-milestone，基线69d42b7。V01-022部署、V01-023 Web已集成；不可变包源码818d02827ee41f4e7f1e6bbb29d3841b110ab3c8、tree85abae49a6c3746bae874f84bf54dbd306aa93ad。后续提交只补交接与门禁事实；最终镜像/应用字节复用关系和归档SHA在[构建证据](../V01-022/build-evidence.json)，实际镜像ID与NAS验收在[nas-deployment-evidence.json](nas-deployment-evidence.json)。

复用同一Host/Trial/operator、认证/会话、Library/Scan/Task、SDK/Web和18条迁移；业务不搬入脚本或UI。仅修复NAS健康探针本机路由、POSIX反斜杠显式拒绝、运行时定位、标准CA与共享读取组；在线风险预算4秒仍受整体5秒截止约束。没有新增业务服务、数据库、主语言或框架，也没有改变共享wire/SQL/身份权限模型。

## 验收与门禁

先集中实现，后统一验收。受影响Packaging63、Pwned25、Linux POSIX2、既有Web40通过；新增回归限于1个健康探针用例和既有文件2个POSIX参数行，不增加Web数量或测试框架。真实NAS完成登录、登记、首次扫描、浏览搜索、拒权、恢复、重启持久性及文件保护检查。完整命令和边界见[tests.md](tests.md)。

独立验收project的2个容器、2个网络和4个持久卷已回收并读回不存在；只保留正式assetlibrary-nas部署及可审查的ignored交付包/日志。临时Windows出站relay已停止，之后原账号登录与查询仍成功。未修改其他NAS服务、全局网络、防火墙、SSH策略、系统信任或个人资产ACL。

M0-004-G2的真实Docker两条退出条件满足，单独关闭；Alpha政策将Docker证据记为passed，其余五个发行target、生产写入与完整Alpha继续阻断，V01-008保持partial。没有以skip、模拟或构建机结果代替NAS验收。

## 运维与保留边界

初始管理员口令仅在操作者电脑受限目录，不入Git/镜像/日志；公有证书需用户手动信任并在90天有效期内安排续期。NAS到风险服务的直连仍不稳定，初始化/恢复需要可用出站通道；没有长期依赖电脑relay的服务。NAS内核没有CFS/PID硬限，保留内存限制、CPU shares和应用并发上限。

预览、下载原内容、增量/通用重扫、资产写入及其他客户端未扩入本里程碑。没有50万资产、断电耐久或其他平台生命周期新声明。Git工作区检查包含未跟踪文件但排除ignored运行产物；此前已被自动审批拒绝清理的历史残留未再次操作。

扫描仍为O(N)逐条枚举与分批持久化，浏览/搜索复用既有分页与索引，无新全表加载或新查询模型；50万资产性能门禁仍需后续专用证据。

正式部署目录为 `/volume2/homes/agent/assetlibrary/V01-021/live`，移出两个资产根；26个配置/交付文件复制后逐一hash核验，旧自有部署目录已移除。更新Compose管理路径并重建两容器后原会话和两库保留、健康通过。9个自有样例核验hash/mtime后回收，避免非法名称验收夹具阻断文档全库扫描。
