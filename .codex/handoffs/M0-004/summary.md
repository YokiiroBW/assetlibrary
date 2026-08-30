# M0-004 交接摘要（clean replacement）

## 完成状态

`partial`

## 实现提交

发行输入最后提交：`06dc8cf0a3e0f4df76b56a2e7b8537e21153885d`；clean branch 包含多个连续源码/测试修订提交，最后另有 handoff metadata 提交。

## 完成内容

同一 ASP.NET Core Spike 源码定义 Linux、Windows x64 和 Docker/Compose；监听地址与 health probe 解耦（容器监听 `0.0.0.0`，probe 默认 loopback）；提供 Windows Service PowerShell/sc.exe adapter 与 systemd unit。SDK/runtime 候选固定为 SDK 10.0.111、ASP.NET Core/.NET runtime 10.0.11。生成物、缓存、日志和 hash 均位于 `.runtime/sandbox-storage/M0-004`，未进入 Git。

## 修改文件

`.codex/tasks/M0-004.md`；`tests/spikes/server-packaging/.dockerignore`、`Dockerfile`、`docker-compose.yml`、`manifest.json`、`bootstrap.sh`、`src/Program.cs`、`src/ServerPackagingSpike.csproj`、`test_spike.py`、`scripts/windows-service.ps1`、`systemd/assetlibrary-m0-004-spike.service`；`docs/spikes/M0-004/README.md`；本目录三个 handoff 文件。

## 架构与依赖

模块为 `server-packaging-spike`，无跨模块访问；复用 ASP.NET Core hosting 生命周期。主语言/框架为既定 C#/.NET 10 + ASP.NET Core 候选；唯一新增候选依赖为官方 `Microsoft.Extensions.Hosting.WindowsServices` 10.0.11，用于 Windows Service 生命周期。未变更共享契约、数据库或生产实现。

## 测试结果

空 runtime 目录后执行 bootstrap；restore、Linux/Windows RID publish、6/6 Python unittest、Docker Compose 静态 config 和 `git diff --check` 通过。artifact source commit 为 clean implementation commit；完整每文件 size/SHA256 和 aggregate digest 已在 runtime artifact 清单中记录。

## 外部门禁与风险

Windows x64 EXE/Service install-start-health-stop-uninstall 未执行：无 Windows/PowerShell。Docker build/run/health 未执行：Docker CLI 26.1.4 存在但 daemon 报 `permission denied ... /var/run/docker.sock`。因此不能宣称 Windows/Docker 门禁通过，必须保持 partial。

## 建议

协调器只合并 clean branch 的连续源码/测试提交及最后的 handoff metadata；在真实 Windows 和 Docker daemon 环境补做外部门禁后，M0-009 再评估是否冻结服务端候选。旧 `codex/m0-004-server-packaging` 分支含历史错误 publish 产物，不应合并。
