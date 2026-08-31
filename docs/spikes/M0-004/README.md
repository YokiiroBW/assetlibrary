# M0-004 服务端发行 Spike

## 结论

状态仍为 **partial**。既有 Linux 行为证据保持有效；2026-08-31 已在真实 Windows 11 x64 上由同一份 `tests/spikes/server-packaging/src/ServerPackagingSpike.csproj` 和 `Program.cs` 完成任务本地 SDK 安装、linux-x64/win-x64 冷发布、win-x64 EXE 启动、health/readiness、Ctrl+Break 优雅停止、重启和负例验证。Windows Service adapter 已修正为 SCM 直接启动 EXE，并通过只读 preflight/残留检查，但 install/start/health/stop/uninstall 需要用户明确批准管理员操作，本轮未执行。Docker CLI、服务和 engine pipe 均不存在，容器门禁也未执行。因此 Windows Service 与 Docker 仍是 M0-009 blocker，不得将本任务改为 completed。

## 工具链与依据

- Windows 执行机：Windows 11 企业版 LTSC，版本 10.0.26100、build 26100、x64；PowerShell 7.6.4 x64；Python 3.12.13；Git 2.53.0.windows.3。
- 系统只有 .NET 6.0.36 runtime，没有全局 SDK。本任务仅在忽略目录 `.runtime/sandbox-storage/M0-004/dotnet-10.0.111` 安装 SDK 10.0.111；ASP.NET Core、.NET 与 Windows Desktop runtime 均为 10.0.11。没有修改 PATH、系统运行时或全局包缓存。
- 2026-08-31 复核的 LTS 候选仍为 .NET 10：发布 2025-11-11，当前补丁 10.0.11，Active LTS 至 2028-11-14；官方支持表最后更新 2026-08-11：<https://dotnet.microsoft.com/en-us/platform/support/policy>；10.0.11 release note 明确列出 SDK 10.0.111：<https://github.com/dotnet/core/blob/main/release-notes/10.0/10.0.11/10.0.11.md>。
- 唯一候选 NuGet 仍为 `Microsoft.Extensions.Hosting.WindowsServices` 10.0.11。Windows Service adapter 依据 .NET Windows Service 指南由 SCM 直接启动候选 EXE，不再通过无法与 SCM 握手的 `.cmd` 子进程：<https://learn.microsoft.com/dotnet/core/extensions/windows-service>、<https://learn.microsoft.com/windows-server/administration/windows-commands/sc-create>。
- .NET SDK 源码许可证为 MIT：<https://github.com/dotnet/sdk/blob/main/LICENSE.TXT>；产品分发与 runtime pack 许可说明：<https://github.com/dotnet/core/blob/main/license-information.md>。版本和适配器均只是 M0 候选，不构成 M0-009 技术冻结。

## Windows 发行与行为证据

`bootstrap.ps1` 是 Windows 对称入口：只接受干净 worktree，在任务忽略目录安装/使用固定 SDK 和 NuGet cache，安全清理经过绝对路径边界校验的任务 `obj/bin/artifact`，再由同一项目发布 linux-x64 与 win-x64。`test_spike.py` 在 Windows 使用 `CREATE_NEW_PROCESS_GROUP` 与 Ctrl+Break 触发 ASP.NET Core cancellation；两次冷发布的完整 `files.sha256` 完全一致。

win-x64 EXE 的 Windows 本机验证覆盖：

- 环境变量启动、`/healthz`、`/readyz` 与 `--health-probe`；
- Ctrl+Break 后进程在 5 秒内以 0 退出，随后同端口重启恢复健康；
- 第二进程占用同一端口时有界失败且不影响首进程；
- 缺少配置、非法端口、非法 bind host 与不可用数据目标均返回配置/路径错误；
- 仅为 Windows Service adapter 增加的命令行配置覆盖可启动同一 host，未分叉业务行为或公开契约；
- 日志未暴露仓库路径，所有可写数据和原始证据均在任务 runtime 或系统临时目录。

### Windows 冷发布观测

以下值来自 Windows x64 上连续冷发布后留在忽略 runtime 的完整 manifest；实现提交由 handoff 记录，`source-commit.txt` 与该提交一致。

| target | files | total bytes | apphost bytes |
|---|---:|---:|---:|
| linux-x64 | 335 | 109,700,001 | 78,256 |
| win-x64 | 338 | 110,355,240 | 162,816 |

精确 apphost SHA-256、完整 `files.sha256` aggregate 与 source commit 必须在实现提交形成后生成，因此记录在随后提交的 handoff，而不是把 pre-commit hash 冒充为最终 provenance。此前 Linux 主机发布的 win-x64 apphost 与本机 Windows 冷发布 hash 不同；这不冒充跨主机 bit-for-bit 可重现。发行身份由同一 source commit、同一 project/manifest、完整逐文件 hash 和等价行为共同证明，本机连续冷发布可重现性单独证明。

## Windows Service adapter 与外部门禁

`scripts/windows-service.ps1` 使用固定服务名 `AssetLibrary-M0-004-Spike`、`LocalService`、任务私有 data path 和直连 EXE `ImagePath`。安装会拒绝既有同名服务或既有 data path；后续 start/stop/uninstall 在修改前同时校验固定 owner marker 与完整 `ImagePath`，拒绝碰触其他注册项；卸载后验证 service、专属 HKLM service key 与 data path 均消失。只有 install/start/stop/uninstall 会要求提升权限，`preflight`、`health` 和 `verify-absent` 可只读执行。

本轮实际只运行了 `preflight` 与 `verify-absent`：EXE 存在，服务/专属注册项/data path 均不存在。由于尚未获得用户对 SCM/HKLM 操作的明确批准，没有执行 install/start/health/stop/uninstall；不得把静态 adapter 或普通 EXE 行为写成 Windows Service 已通过。

## Docker blocker

Windows 本机精确检查结果均为 false：PATH 中 Docker CLI、Docker Desktop 标准 CLI 路径、`com.docker.service`、`\\.\pipe\docker_engine`。因此没有执行 `docker build`、Compose up/health、non-root/read-only 验证或 `down -v`；没有安装 Docker、触发 UAC 或更改系统服务。既有 Dockerfile/Compose 静态定义保留，但真实容器证据仍是 blocker。

## 规模、安全与架构边界

Spike 不触碰真实资产、NAS 路径、生产服务、公共接口、共享契约、ADR、任务注册表、项目状态或 M0-009。生成物、NuGet cache、日志、逐文件 manifest 和原始命令输出不进入 Git。健康路径为 O(1)，发布为 O(runtime files)；50 万资产索引、数据库、缓存和 Worker 性能不在本 Spike 范围。无 Docker 时无法证明容器 non-root/read-only 边界；无批准的 SCM 周期时无法证明 Windows Service lifecycle，二者继续列为显式风险。
