# M0-004 Windows 外部门禁测试记录

## 执行环境

2026-08-31（Asia/Shanghai）；Windows 11 企业版 LTSC 10.0.26100 build 26100 x64；PowerShell 7.6.4 x64；任务捆绑 Python 3.12.13；Git 2.53.0.windows.3。系统无 .NET SDK，任务忽略目录使用 SDK 10.0.111 与 runtime 10.0.11。所有可写数据、SDK、NuGet cache、publish、manifest 与原始输出均在 `.runtime/sandbox-storage/M0-004` 或系统临时目录。

## 自动验证

| 命令/检查 | 结果 |
|---|---|
| `pwsh -NoProfile -File tests/spikes/server-packaging/bootstrap.ps1` | 通过；固定 SDK restore，linux-x64/win-x64 self-contained publish，source commit 与完整 manifest 生成 |
| `PYTHONDONTWRITEBYTECODE=1 <bundled-python> -m unittest discover -s tests/spikes/server-packaging -p 'test_*.py' -v` | 8 passed，0 failed，0 skipped；测试内部连续两次 cold publish 的 `files.sha256` 完全相同 |
| `pwsh -File tests/spikes/server-packaging/scripts/windows-service.ps1 -Action preflight` | 通过；EXE 存在，service/owner registration/data 不存在；只读检查 |
| `pwsh -File tests/spikes/server-packaging/scripts/windows-service.ps1 -Action verify-absent` | 通过；service=false、registry=false、data=false |
| PowerShell parser（`bootstrap.ps1`、`windows-service.ps1`）与 Python compile | 通过；compile residue 仅在被 Git 忽略的 `__pycache__`，最终无跟踪变更 |
| `python scripts/validate_handoff.py` | 通过 |
| `python scripts/validate_architecture_baseline.py` | 通过 |
| `python scripts/verify_repository.py` | 通过 |
| `git diff --check` | 通过 |

8 项 Spike 测试覆盖：single-source manifest；win-x64 环境变量启动；`/healthz`、`/readyz` 与内置 probe；Ctrl+Break cancellation/0 exit；同端口 restart；occupied port；缺少配置；非法 port/bind host；不可用 data target；Windows Service command-line config；完整 artifact provenance；Service adapter direct EXE/owner guard/残留定义。

## Artifact provenance

- 实现提交与 runtime `source-commit.txt`：`2b814b4d466a716be3c0552e285cf4bbbf2bad6e`，精确相等。
- 完整 `files.sha256` aggregate：`0a7b216c3379e4a938546aaff0165d4e671bbffe18f473195269a17c7cbc2fd9`。
- linux-x64 apphost：78,256 bytes，SHA-256 `a2e5c0a1d967b573721b94663d67a4e9fcca4483c8c461c54c2cf9d2a48586f3`。
- win-x64 apphost：162,816 bytes，SHA-256 `76d876243a6c488c8058283bb052c93fa5bc522ec68bd15ef701346c5506b6a3`。
- linux-x64：335 files / 109,700,001 total bytes；win-x64：338 files / 110,355,240 total bytes。
- 旧 Linux 主机发布的 win-x64 apphost SHA-256 为 `25587fad799168cd11efeeed3e00c4508e6abcfe78269174f49831c85961f2d9`；跨主机 hash 不同，未宣称跨主机 bit-for-bit 可重现。

## 明确未执行

Windows Service 的以下管理员操作未获用户明确批准，因此全部未执行：

```text
pwsh -File tests/spikes/server-packaging/scripts/windows-service.ps1 -Action install
pwsh -File tests/spikes/server-packaging/scripts/windows-service.ps1 -Action start
pwsh -File tests/spikes/server-packaging/scripts/windows-service.ps1 -Action health
pwsh -File tests/spikes/server-packaging/scripts/windows-service.ps1 -Action stop
pwsh -File tests/spikes/server-packaging/scripts/windows-service.ps1 -Action uninstall
```

如果后续获批，必须按该顺序执行，并在任何失败后优先执行 owner-guarded uninstall，最后再次运行 `verify-absent`。本轮只读检查确认未曾创建目标 service、专属 HKLM key 或 service-data。

Docker 精确阻塞证据：`Get-Command docker` 为 absent；Docker Desktop 标准 CLI 路径 absent；`Get-Service com.docker.service` absent；`Test-Path '\\.\pipe\docker_engine'` 为 false。因此没有执行 Docker build、Compose up/health、non-root/read-only 或 `down -v`，也没有自行安装 Docker。

## 清理与边界

测试结束后无 `ServerPackagingSpike` 进程或测试监听；无 service、专属 HKLM service key、service-data、容器、volume 或 Docker 改动。构建输出、缓存、hash manifest 与原始日志全部留在 Git 忽略 runtime，提交内容仅包含文本源码、测试、脱敏证据和 handoff。
