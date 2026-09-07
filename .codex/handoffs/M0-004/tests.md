# M0-004 Windows 外部门禁测试记录

## 执行环境

2026-08-31（Asia/Shanghai）；Windows 11 企业版 LTSC 10.0.26100 build 26100 x64；PowerShell 7.6.4 x64；任务捆绑 Python 3.12.13；Git 2.53.0.windows.3。系统无 .NET SDK，任务忽略目录使用 SDK 10.0.111 与 runtime 10.0.11。所有可写数据、SDK、NuGet cache、publish、manifest 与原始输出均在 `.runtime/sandbox-storage/M0-004` 或系统临时目录。

## 自动验证

| 命令/检查 | 结果 |
|---|---|
| `pwsh -NoProfile -File tests/spikes/server-packaging/bootstrap.ps1` | 通过；固定 SDK restore，linux-x64/win-x64 self-contained publish，source commit 与完整 manifest 生成 |
| `PYTHONDONTWRITEBYTECODE=1 <bundled-python> -m unittest discover -s tests/spikes/server-packaging -p 'test_*.py' -v` | 9 passed，0 failed，0 skipped；测试先从干净提交发布行为产物，再执行三个独立 PowerShell cold bootstrap，完整 `files.sha256` 完全相同 |
| `pwsh -File tests/spikes/server-packaging/scripts/windows-service.ps1 -Action preflight` | 通过；EXE 存在，service/owner registration/data 不存在；只读检查 |
| `pwsh -File tests/spikes/server-packaging/scripts/windows-service.ps1 -Action verify-absent` | 通过；service=false、registry=false、data=false |
| PowerShell parser（`bootstrap.ps1`、`windows-service.ps1`）与 Python compile | 通过；compile residue 仅在被 Git 忽略的 `__pycache__`，最终无跟踪变更 |
| `python scripts/validate_handoff.py` | 通过 |
| `python scripts/validate_architecture_baseline.py` | 通过 |
| `python scripts/verify_repository.py` | 通过 |
| `git diff --check` | 通过 |

9 项 Spike 测试覆盖：single-source manifest；产物 `AssemblyInformationalVersion` 绑定 issuance commit；win-x64 环境变量启动；`/healthz`、`/readyz` 与内置 probe；Ctrl+Break cancellation/0 exit；同端口 restart；occupied port；缺少配置；非法 port/bind host；不可用 data target；Windows Service command-line config；三个独立 cold bootstrap 的完整 artifact provenance；Service adapter direct EXE/owner guard/残留定义。

## Provenance 回归与修正

独立审查复跑在旧实现下得到 aggregate `5249f5eb846cbfe578e2d74f74b5867fa3ecb5b57e6e23a4572f5ef795f70fce`、win apphost `85ce9946dcda17f4f443d87f9817acd6155a05ede2675fae0a91dcbf1bde2ab8`，与首次 handoff 的 `0a7b...` / `76d8...` 不一致，而旧测试仍通过。诊断读取产物 ProductVersion 为 `0.1.0-spike+5481ed7...`：.NET SDK 嵌入了当前 metadata `HEAD`，但旧 `source-commit.txt` 仍写前一 issuance commit `2b814b4...`。这证明旧测试只覆盖同一 `HEAD` 内连续运行，provenance 定义不闭合。

correction implementation 显式传入 `SourceRevisionId`、CI/deterministic/PathMap 属性，使用 RID 独立 `obj/bin`、`UseSharedCompilation=false`、`--disable-build-servers`，并在每轮前后关闭 build servers。测试的三个 bootstrap 各自启动独立 PowerShell、清空受边界保护的 `obj/bin/artifact`，同时比较完整 manifest；另由 `--build-info` 检查产物内 informational version。提交本 handoff metadata 使仓库 `HEAD` 再次前进后，复跑仍得到相同 issuance commit、informational version 与 hashes。

## Artifact provenance

- correction implementation、runtime `source-commit.txt` 与产物 informational version：`25594025fbdb54903f311271147f86e1b43cc1f0`，精确一致；`repository-head.txt` 在 metadata commit 后不同于该值，产物仍固定到 issuance commit。
- 完整 `files.sha256` aggregate：`7df4c0d8c839da1df443b3738fd0cba23209d09dfd20fbd888a5ef0cd8422906`。
- linux-x64 apphost：78,256 bytes，SHA-256 `a2e5c0a1d967b573721b94663d67a4e9fcca4483c8c461c54c2cf9d2a48586f3`。
- win-x64 apphost：162,816 bytes，SHA-256 `55f4cbed1b4502ebd87485d51148655a0af5aa8ce223295567e685ef5a0ae51e`。
- linux-x64：335 files / 109,701,025 total bytes；win-x64：338 files / 110,356,264 total bytes。
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

## 2026-09-05 两端历史对齐

ALIGN-001 将 NAS `6fd8fb1` 的保护性修复与本地 Windows 证据整合。本交接原有 commit、平台观测和测试计数保留为对应历史快照；新实现及验证见 `.codex/handoffs/ALIGN-001/` 与 `.codex/handoffs/ALIGN-004/`。原 partial 状态、缺失平台证据和所有发布阻断不变。
