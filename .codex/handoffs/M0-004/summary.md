# M0-004 Windows 外部门禁交接摘要

## 完成状态

`partial`

修正后的实现/证据提交：`25594025fbdb54903f311271147f86e1b43cc1f0`。本文件与其余 handoff 元数据在随后独立提交中引用该实现提交；更早的 Windows hash 交接已被本次 correction 取代。

## 本轮完成内容

- 在真实 Windows 11 x64、Windows 本机 NTFS 独立 worktree 上安装任务本地 .NET SDK 10.0.111，没有修改全局 SDK、PATH、包缓存或系统配置。
- 新增 Windows 对称 bootstrap；同一项目冷发布 linux-x64 与 win-x64，三个独立 PowerShell bootstrap 的完整逐文件 manifest 一致，metadata `HEAD` 前进后复跑仍一致。
- 修正 provenance 漂移：旧 bootstrap 记录最后修改发行输入的 commit，却允许 .NET SDK 把当前仓库 `HEAD` 写入产物 informational version，导致 handoff metadata commit 改变二进制而 `source-commit.txt` 不变。现在显式固定 `SourceRevisionId`，记录 repository HEAD，隔离 RID 中间目录，禁用/关闭构建服务器，并由 `--build-info` 自动核对产物内版本。
- win-x64 EXE 已验证环境变量和命令行两种配置入口、`/healthz`、`/readyz`、内置 health probe、Ctrl+Break 优雅停止、同端口重启、端口碰撞、缺少/非法配置和不可用数据目标。
- 修正 Windows Service adapter：SCM 现在直接启动 EXE，不再经 `.cmd` wrapper；固定 `LocalService`、服务名和任务私有 data path，并增加 owner marker + ImagePath 双重所有权校验、失败回滚及 uninstall 后 service/专属注册项/data 三项残留检查。
- Windows Service 只执行了无写入的 `preflight` 与 `verify-absent`。用户尚未明确批准 SCM/HKLM 管理员操作，因此 install/start/health/stop/uninstall 没有执行。
- Docker CLI、Docker Desktop 标准 CLI、`com.docker.service` 和 `\\.\pipe\docker_engine` 均不存在；未安装 Docker，也未执行 build/Compose/容器边界测试。

## Windows 环境与 Artifact

- Windows 11 企业版 LTSC 10.0.26100 build 26100，x64。
- PowerShell 7.6.4 x64；Python 3.12.13；Git 2.53.0.windows.3。
- 任务本地 SDK 10.0.111；Microsoft.AspNetCore.App、Microsoft.NETCore.App、Microsoft.WindowsDesktop.App 10.0.11。
- 修正后的 issuance/source commit：`25594025fbdb54903f311271147f86e1b43cc1f0`。
- 完整 `files.sha256` aggregate：`7df4c0d8c839da1df443b3738fd0cba23209d09dfd20fbd888a5ef0cd8422906`。

| target | files | total bytes | apphost bytes | apphost SHA-256 |
|---|---:|---:|---:|---|
| linux-x64 | 335 | 109,701,025 | 78,256 | `a2e5c0a1d967b573721b94663d67a4e9fcca4483c8c461c54c2cf9d2a48586f3` |
| win-x64 | 338 | 110,356,264 | 162,816 | `55f4cbed1b4502ebd87485d51148655a0af5aa8ce223295567e685ef5a0ae51e` |

首次 Windows handoff 的 `0a7b...` / `76d8...` 与独立复跑的 `5249...` / `85ce...` 暴露了 metadata `HEAD` 漂移，现均撤回。此前 Linux 主机从旧 issuance commit 发布的 win-x64 apphost SHA-256 是 `25587fad799168cd11efeeed3e00c4508e6abcfe78269174f49831c85961f2d9`，与本机 Windows 发布不同。该差异不被写成失败或跨主机 bit-for-bit 保证；身份由明确 issuance commit、产物内 informational version、相同 project/manifest、完整逐文件 hash 和等价行为共同证明。

## 架构、复用与影响

模块仍为 `server-packaging-spike`。复用现有 ASP.NET Core hosting 生命周期、Spike-local `m0-004/v1` 行为和官方 `Microsoft.Extensions.Hosting.WindowsServices` 10.0.11；没有复制领域逻辑，没有更改生产目录、共享契约、数据库、ADR、任务包、项目状态或 M0-009。Windows command-line configuration 只是 Service host adapter 的配置来源，未引入公开产品 API。所有生成物、缓存和原始证据位于忽略 runtime；没有提交二进制、日志、注册表导出或本机绝对路径。

## 未完成门禁与风险

1. Windows Service install/start/health/stop/uninstall 仍需用户明确批准当前机器的 SCM/HKLM 管理员操作；静态定义、普通 EXE 与 preflight 不能代替真实服务生命周期。
2. Docker 不存在，build、Compose health、non-root/read-only boundary 和 `down -v` 清理均无真实证据。
3. 状态必须继续为 `partial`；M0-009 不得据此冻结 Windows Service 或 Docker 发行候选。

## 清理状态与建议

`AssetLibrary-M0-004-Spike` service、对应 HKLM service key 和任务 service-data 均不存在；5080/5091/5092/5093/5094/5095 测试监听与 `ServerPackagingSpike` 进程均已结束。Docker 没有被安装或修改。建议 Linux 主协调器先审查实现/证据提交，再审查 handoff 元数据提交；若用户稍后授权，应在同一分支追加真实 SCM 周期证据并再次更新 handoff，最后仍必须执行 uninstall 与三项残留验证。
