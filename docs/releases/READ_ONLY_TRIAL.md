# AssetLibrary 首个只读试用

2026-09-07，V01-015 至 V01-020。首个集成里程碑交付 Windows x64 运行包，支持本机浏览器管理只读资源索引。完整 V0.1 Alpha、Windows Service、Explorer 和资产写入仍未发布。

## 能做什么

| 操作 | 当前结果 |
| --- | --- |
| 初始化/恢复管理员、登录/退出 | 本机受保护操作、HTTPS 会话和持久密钥；重启保留数据和有效会话 |
| 登记物理资源库 | 管理员在部署者预先配置的存储范围内登记；支持账户可读取的本地盘与 NAS UNC 路径 |
| 首次扫描 | 显式启动，显示进度，可取消；失败或取消后可重试，服务重启按持久事实恢复 |
| 浏览和搜索 | 目录浏览、名称/完整路径搜索、分页/虚拟列表、权限过滤 |
| 路径暂时不可达 | 显示不可达并保留上次索引；恢复后可以读取；不误报空库、不修改原文件 |

索引是首次扫描快照。扫描完成后不提供通用重扫、实时监听或增量更新；资产后续发生变化时，列表可能仍显示旧快照。还没有内容预览、原内容下载、上传、移动、改名、删除、查重整理或 Explorer 扩展。不要将本轮当作日常文件整理工具。

## 运行包与准备

本次实际包、来源提交、完整 SHA-256 和验证结论记录在 [交付清单](../../.codex/handoffs/V01-015/delivery.json)。解压到本地固定磁盘的独立目录，例如 `C:\AssetLibraryTrial`；状态和资产应位于包目录之外。

需要 Windows x64、PowerShell 7.5+、Python 3.12+ 和 PostgreSQL 16.15 的完整 Windows 工具目录。包已经包含自包含 .NET Host 与构建好的 Web，运行不需要 .NET SDK、Node 或 pnpm。运行账户需要目标资产的读取权限。配置的 state 必须在本地固定磁盘，不能放到 NAS。

以下操作的完整参数、错误码、证书续期、恢复和备份说明见 [Windows 部署说明](../../infra/windows/trial/README.zh-CN.md)，解压包中也附带同一说明。

## 首次使用

1. 准备 `settings.json`，明确允许登记的范围，例如下面的自有 NAS 共享。Web 当前只监听本机 HTTPS。

```json
{
  "public_origin": "https://localhost:5443",
  "storage_sources": [
    {"source_key":"nas","display_name":"我的 NAS","allowed_root":"\\\\nas\\assets"}
  ]
}
```

2. 在包目录的 PowerShell 7 中初始化，替换工具和配置路径。状态父目录必须已存在，状态目录使用全新路径。

```powershell
.\trial.ps1 -Action initialize -StatePath C:\AssetLibraryTrialState -SettingsFile C:\TrialSettings\settings.json -Python C:\Tools\Python\python.exe -PostgresBin C:\Tools\PostgreSQL16\bin
```

3. 核对本次初始化的 `tls\localhost.cer`，按包内说明手动信任到当前用户证书库，然后创建管理员。脚本隐藏口令输入，不把口令写入命令历史。

```powershell
.\trial.ps1 -Action operator -StatePath C:\AssetLibraryTrialState -OperatorAction bootstrap -AccountName admin -DisplayName 管理员
```

4. 启动并访问 `https://localhost:5443`。登录后登记库，明确启动首次扫描，再浏览或搜索。

```powershell
.\trial.ps1 -Action start -StatePath C:\AssetLibraryTrialState
.\trial.ps1 -Action status -StatePath C:\AssetLibraryTrialState
.\trial.ps1 -Action stop -StatePath C:\AssetLibraryTrialState
```

首次创建管理员、恢复或修改口令需要联网风险服务可用；既有账号登录与只读使用不依赖互联网。网络失败时同一操作身份可重试，不能跳过风险检查。首次生成证书有效期90天，续期须保留旧证书解密历史数据；新服务器证书及历史解密证书均须带至少2048位RSA私钥，本试用不接受ECDSA-only证书。不能直接覆盖 PFX 或删除密钥目录。包不自动更改证书信任、UAC、防火墙、SCM 或自动启动项。

## 已执行的验收

真实 PostgreSQL 16.15/18 条迁移、真实 HTTPS Host、实际只读 Worker 和 Chromium 浏览器组成完整闭环；覆盖登录、登记、扫描、浏览/搜索、拒权、Origin/CSRF、重启、会话撤销和管理员恢复。Windows 自包含包实际执行初始化、启动、健康、正常停止、重启与恢复。实际 SMB 合成目录完成可达→路径不可达→恢复，扫描前后文件 SHA-256 和修改时间不变。

完整命令、计数、运行上下文和限制见 [测试记录](../../.codex/handoffs/V01-015/tests.md)。Web 40 项回归覆盖桌面/窄屏、键盘和会话/扫描状态；单项综合 E2E 的内部阶段不重复计算为多项测试。

## 已知限制与状态保护

尚未进行真实 NAS 的50万资产长时使用、整机断网/突然断电、完整平台服务安装及跨版本自动升级验收。目录检查不等于内核级原子文件系统快照。部署包与状态绑定来源，更新前停机备份并遵循未来版本的迁移说明。

正常 `stop` 已验证会释放自身 TLS 临时私钥容器；强制杀进程或突然掉电时 Windows 加密提供程序可能保留容器，不能承诺零残留。停止超时后的强制回退会明确报告；不要批量删除系统私钥目录。

测试只触及自有合成目录，未登记个人资产。本轮两处合成测试目录的删除被自动批准审查拒绝，原位保留并记录在交接中；历史对齐任务也有9个空目录保留。这些是忽略目录中的磁盘残留，和 Git 工作区干净是两项独立事实。
