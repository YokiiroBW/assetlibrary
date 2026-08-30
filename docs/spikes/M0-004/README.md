# M0-004 服务端发行 Spike

## 结论

状态为 **partial**。同一份 `tests/spikes/server-packaging/src/ServerPackagingSpike.csproj` 和 `Program.cs` 已通过 .NET 10.0.111 SDK 生成 linux-x64 与 win-x64 自包含发布定义；Linux 启动、健康、优雅停止、重启、缺少配置、端口占用和只读数据路径测试通过。当前执行机没有 Windows/PowerShell，也没有 Docker daemon 权限，因此 Windows Service 周期和容器 build/run/health 只能保留可执行定义，不能宣称通过。

## 工具链与依据

- LTS 候选：.NET 10，发布 2025-11-11，当前补丁 10.0.11，支持至 2028-11-14；官方支持表最后更新 2026-08-11：<https://dotnet.microsoft.com/en-us/platform/support/policy>；版本说明：<https://github.com/dotnet/core/releases/tag/v10.0.11>。
- SDK：10.0.111；ASP.NET Core runtime pack / .NET runtime：10.0.11，安装于任务专用 `/tmp`，不修改系统运行时。
- 唯一候选 NuGet：`Microsoft.Extensions.Hosting.WindowsServices` 10.0.11，用于 Windows Service 生命周期；NuGet 页面与许可证元数据：<https://www.nuget.org/packages/Microsoft.Extensions.Hosting.WindowsServices/10.0.11>。退出策略是随 .NET 10 LTS 更新包升级或在 M0-009 评估后移除。
- .NET SDK 源码许可证为 MIT：<https://github.com/dotnet/sdk/blob/main/LICENSE.TXT>；产品分发与 runtime pack 许可说明：<https://github.com/dotnet/core/blob/main/license-information.md>。
- 版本是 M0 候选记录，不构成 M0-009 的最终技术冻结。

## 发行定义与一致性

`manifest.json` 是测试本地发行矩阵；`bootstrap.sh` 对同一项目分别执行 `dotnet publish -r linux-x64` 和 `-r win-x64`，并记录源 commit、精确大小和 SHA-256。Dockerfile 从同一 `src` multi-stage publish，Compose 将数据边界挂载到独立 named volume、以非 root 用户运行并使用同一二进制 `--health-probe`。平台差异仅在宿主命令、服务管理、路径和包装：Linux 使用 systemd，Windows 使用可逆 PowerShell/sc.exe adapter，容器使用 Docker/Compose。

配置契约 `m0-004/v1` 仅为 Spike-local：`SPIKE_DATA_PATH` 必填且可写；`SPIKE_PORT` 可选，范围 1024–65535，默认 5080；`SPIKE_BIND_HOST` 默认 127.0.0.1，容器设置为 0.0.0.0。`/healthz` 和 `/readyz` 均返回契约版本和状态；SIGTERM 触发 bounded ASP.NET Core shutdown。日志为 JSON 结构化格式，不写入资产路径或秘密。

## clean artifact 证据（issuance commit `b5d19cebafdb536a61b33917715419528e7f10e1`）

| target | bytes | SHA-256 |
|---|---:|---|
| linux-x64 `ServerPackagingSpike` | 78,256 | `a2e5c0a1d967b573721b94663d67a4e9fcca4483c8c461c54c2cf9d2a48586f3` |
| win-x64 `ServerPackagingSpike.exe` | 162,816 | `c5f45531cbe7766689110e1ca4eb8ce8ce5c4d5ed4f65a828b8559ab2ab8a125` |

## 已知环境门禁

Windows 门禁命令（需真实 Windows x64）：`ServerPackagingSpike.exe`、Windows Service install/start/stop/uninstall cycle（建议 `sc.exe create ... binPath=...`）。本机证据：`command -v powershell`、`command -v pwsh` 和 Windows 主机均不可用，未执行。

Docker 门禁命令（需可访问 daemon）：`docker build -f tests/spikes/server-packaging/Dockerfile -t m0-004-spike tests/spikes/server-packaging`，随后 `docker compose -f tests/spikes/server-packaging/docker-compose.yml up -d --build`、`docker inspect --format '{{json .State.Health}}' ...` 和 `docker compose ... down -v`。本机精确阻塞证据：Docker CLI 26.1.4 存在，但 `docker version` 报 `permission denied ... /var/run/docker.sock`；未修改 socket，未宣称容器门禁通过。

## 规模与安全边界

Spike 不触碰真实资产目录；可写数据只进入测试临时目录或容器 named volume。自包含发布约 106–107 MiB，发布阶段为 O(number of copied runtime files)，运行时健康路径为 O(1)；生产 50 万资产的索引、数据库、缓存和 Worker 性能不在本 Spike 范围内。
