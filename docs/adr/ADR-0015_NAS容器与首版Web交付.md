# ADR-0015 — NAS 容器与第一版 Web 交付

状态：Accepted / implemented and NAS-validated，2026-09-07。用户要求按既有架构与设计完成NAS服务端和第一版Web，集中实现后统一验收。Owner V01-021；部署V01-022，Web V01-023。

## 交付边界

NAS运行Docker/Compose，电脑通过NAS HTTPS使用现有第一版Web。沿用ADR0014已实现的认证、物理库登记、首次只读扫描及浏览/搜索契约。此次完成部署落点和已有Web流程；不附加预览、原内容下载、写文件、通用重扫、标签、查重、Provider或新客户端。保留既有Windows交付。

## 部署结构

- 两个长期服务：同一.NET CoreServer托管编译Web，PostgreSQL16.15保存现有模块数据。必要初始化/迁移工具是一次性部署作业，不形成第二业务服务。
- 固定现有.NET10.0.111/10.0.11、Node24.20.0、pnpm11.19.0。部署工具沿用Python3.12+标准库、PostgreSQL工具和OpenSSL；镜像版本/来源可追溯，不新增应用框架或数据库。
- 资产是NAS物理目录，bind mount必须只读；Web登记配置允许的容器路径。配置、证书、授权密钥、DP和数据库分别持久化，不和资产混放。停止/重建容器不删除数据；删除卷只允许显式自有验收资源回收。
- PostgreSQL不发布宿主端口。跨容器连接保持既有VerifyFull，数据库证书匹配内部服务名并有受控CA；不以“内网”为理由禁用TLS或赋予业务超级用户权限。迁移复用migration_tool的所有权、备份验证、前进迁移和漂移拒绝，现有1–18不改。
- Web/API同源HTTPS，沿用Origin/Host、Cookie/CSRF/限速、持久密钥与带外operator。不新增匿名初始化、任意API地址、CORS或认证绕过。Origin与证书SAN一致；说明包含浏览器信任和RSA≥2048/历史解密要求，不自动改系统信任。
- 运行时非root、根文件系统只读、最小capabilities、no-new-privileges、限额和正常停机时间。初始化额外权限只作用于新建自有卷。Linux容器生命周期覆盖已有只读子进程，不引入独立Worker服务。

Linux文件名中的字面反斜杠会与现有跨端路径契约的分隔符归一化冲突。本轮不扩展路径编码协议：配置源、登记根和发现条目在物理适配边界明确拒绝这种输入；扫描以entry_path_unsupported失败并中止暂存，不静默改成虚构目录、不跳过后宣告成功。Windows路径分隔符语义保持兼容。

容器内健康探针保留公开URI、Host/SNI和精确证书核验，仅将TCP连接路由到配置监听对应loopback，避免依赖NAS外部地址回绕。公开端口与容器监听端口保持一致。

## Web与设计

以docs12和assets/visuals/10_web_admin_asset_browser.png为依据，复用当前设计令牌、导航/搜索/详情、虚拟列表及同源SDK。仅补齐已开放流程的NAS路径说明、部署语义和必要布局问题；不能伪造未来统计或提前开放写入/预览。

## 验收

先集中实现、最后统一验收，不逐文件写测试或反复跑完整套件。稳定diff审查后统一执行必要的真实静态/构建命令、受影响既有回归与NAS容器/浏览器流程，验证持久性、资产只读、拒权与恢复。构建机不能替代NAS验收；SMB文件拷贝不是部署。NAS访问不足时继续独立实现，实际部署仍为未完成项。

Docker门禁仅在相应真实生命周期证据满足后由协调器裁决，不自动关闭完整Alpha、生产写入、Windows SCM、Linux systemd、容量或Explorer门禁。

## 实测网络预算裁决

NAS真实初始化中，既有2秒口令风险网络预算会在收到完整padded响应前截止；同一固定非秘密prefix的合法响应约100KiB，经可用出站通道超过2秒。将风险请求上限/默认值调整为4秒，仍受现有5秒整体管理员/账号操作截止约束。此处更新ADR0014的2秒实现参数，不更改TLS、固定provider origin、padding、256KiB/5000行、缓存、取消或失败关闭条件；不影响已有账号的离线登录/浏览。必要出站代理仅作用于本应用/一次性operator，不改NAS全局网络。

## NAS 实测适配与交付裁决

目标 Synology 内核支持内存限制与 CPU shares，不支持 CFS/PID 硬限。Core1024、PG512、setup256 是相对 CPU 权重；不声称 CPU 百分比/PID 硬隔离。固定 UID1654 的 Core 可通过 deployment.env 中受校验的可选数字读取组访问现有共享 ACL，默认1654，本 NAS 实测101；仍只挂载两个用户允许的只读资产目录，无 root 扫描、宿主 ACL 修改或 Docker socket。

隔离 Worker 保持环境清理及同一宿主入口；运行镜像在 /etc/dotnet/install_location_x64 注册 /opt/dotnet，避免依赖未继承环境定位运行时。Core 和一次性 operator 镜像均安装发行版标准 CA roots，保留远端 TLS 校验；实际初始化/恢复使用过的临时 CONNECT 出站辅助已停止，普通登录/浏览不依赖它。

V01-021 的真实 NAS 浏览器、重启、恢复、只读边界和自有验收项目回收证据满足 M0-004-G2 两条退出条件，协调器仅关闭该 Docker 环境门禁。正式图片/文档资源库已登记、未自动扫描；完整 Alpha、容量、生产写入和其他平台门禁不因此关闭。
