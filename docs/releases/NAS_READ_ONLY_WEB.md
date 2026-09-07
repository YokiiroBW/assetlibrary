# NAS 服务端与第一版 Web

V01-021 按 ADR-0015 交付 NAS Docker 服务端和现有第一版 Web。入口为 `https://192.168.31.210:5443`，浏览器直接连接 NAS；Core 和 PostgreSQL 是两个常驻容器。Windows 试用交付继续保留。

V01-024..026 在此基础上完善当前核心对应的 Web 产品交互，使用方式见 [Web 工作区说明](WEB_WORKSPACE.md)。最初 V01-021 交付证明了部署与最小只读链路，并非完整产品交互已经完成；新的交互及升级证据由 V01-024 单独记录。

## 登录与目录

初始账号为 `admin`。随机初始口令保存在部署操作者电脑的 `%LOCALAPPDATA%\AssetLibrary\NAS\V01-021\initial-admin.json`，目录仅当前 Windows 用户可访问；口令不在 Git、镜像或部署说明中。公有 HTTPS 证书在同目录 `browser-certificate.crt`。本次证书有效至 2026-12-06 15:22:36 UTC，SHA-256 指纹为 `96:B0:BB:7A:59:3D:59:48:27:18:18:4E:C8:EC:5D:B9:59:30:B0:32:54:2B:E5:41:DB:9A:28:87:F9:BB:83:C1`。首次访问需核对该指纹，再由使用者配置当前用户信任；部署没有修改系统证书信任。

| Web 资源库 | NAS 物理目录 | 容器登记路径 | 初始状态 |
| --- | --- | --- | --- |
| 图片 | `/volume1/Pictures` | `/assets/pictures` | 已登记，未扫描 |
| 文档 | `/volume1/Documents` | `/assets/documents` | 已登记，未扫描 |

登录后选择资源库，点击“开始首次扫描”才会建立索引。可以查看实际扫描计数、取消未完成扫描或重试失败任务。扫描成功后可以浏览目录、按文件名或相对路径搜索；“刷新”读取已提交快照，不会再次扫描原目录。没有自动进行这两个目录的全库扫描，验收仅使用独立的自有样例子目录。

本版开放认证、目录登记、首次只读扫描、浏览与搜索。预览、原内容下载、通用或增量重扫、文件整理和写入仍待后续里程碑。物理文件是事实来源，本版不会将数据库快照当作实时同步。

## 部署与日常维护

部署目录：`/volume2/homes/agent/assetlibrary/V01-021/live`。配置目录位于两个资产根之外，避免私密目录和部署文件干扰首次扫描。Compose project 为 `assetlibrary-nas`，资产以只读 bind mount 挂载；数据库不发布 NAS 端口。配置、证书、授权/会话密钥和索引持久化在此 project 的四个专用 Docker 卷中。

通过 NAS 的既有 SSH 登录后，在部署目录执行：

```sh
sh nasctl.sh status
sh nasctl.sh stop
sh nasctl.sh start
```

`stop` 正常停止 Core 和数据库，`start` 使用原卷恢复；`down` 仅移除此 project 的容器和网络，保留四个卷和资产。不要删除卷、重新生成 deployment ID 或清空密钥来重置管理员。详细初始化、恢复、证书续期和停机备份说明见 [NAS 部署手册](../../infra/docker/nas/README.md)。软件备份不包含资产原文件。

管理员恢复沿用带外 operator，口令交互输入：

```sh
sh nasctl.sh operator recover --account admin --new-attempt
```

初始化和恢复必须完成在线口令风险检查；网络失败会拒绝操作。可仅对该次命令设置可用的 `HTTPS_PROXY`，不更改 NAS 全局网络。已经建立的账号登录、扫描、浏览和搜索不依赖该风险服务。部署时的临时出站辅助不会作为常驻服务留下。

Core 使用 UID/GID `1654:1654`，本 NAS 的 `deployment.env` 额外指定 `ASSETLIBRARY_ASSET_READ_GROUP=101`，以读取现有共享目录 ACL 允许的内容；没有修改主机 ACL、使用 root 扫描或挂载 Docker socket。根文件系统只读、全部 capabilities 移除、no-new-privileges 和内存上限保持启用。目标内核不支持 CPU CFS 或 PID 硬限，使用 CPU shares，并保留应用并发界限。

## 验收与范围

可追溯镜像来源、NAS 浏览器流程、拒权、重启持久性、原文件哈希/mtime 和自有验收资源回收记录见 [V01-021 交接](../../.codex/handoffs/V01-021/summary.md) 与 [验收记录](../../.codex/handoffs/V01-021/tests.md)。构建清单记录实际镜像源码 commit；后续交接文档提交不改写已构建归档。

该里程碑不等于完整 V0.1 Alpha 发布。容量、生产写入、其他平台及尚未实现的管理功能仍按 [Alpha 就绪记录](V0.1_ALPHA_READINESS.md) 保持各自门禁。
