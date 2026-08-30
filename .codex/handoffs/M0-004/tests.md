# M0-004 测试记录（clean replacement）

## 执行环境

Linux x86-64，Python 3.12；从空 `.runtime/sandbox-storage/M0-004` bootstrap；SDK 10.0.111，ASP.NET Core/.NET runtime pack 10.0.11；Docker CLI 26.1.4，无 daemon 权限；无 Windows/PowerShell。

## 命令与结果

- `M0_004_DOTNET_DIR=/tmp/m0-004-dotnet-10.0.111 bash tests/spikes/server-packaging/bootstrap.sh` — 通过；clean provenance、restore、linux-x64/win-x64 publish。
- `M0_004_DOTNET_DIR=/tmp/m0-004-dotnet-10.0.111 PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s tests/spikes/server-packaging -p 'test_*.py' -v` — 6 passed。
- `docker compose -f tests/spikes/server-packaging/docker-compose.yml config` — 通过。
- `git diff --check` — 通过。

## Artifact matrix

source commit：`37f5fd821b5d79a3d7b88bb510d8ab717deff183`；aggregate digest：`cedd099f2a57db13b16d7950dd1ebb56d9fff030e54cf4d89a07b88f531515ac`。

| target | exact bytes | SHA-256 |
|---|---:|---|
| linux-x64 apphost | 78,256 | `a2e5c0a1d967b573721b94663d67a4e9fcca4483c8c461c54c2cf9d2a48586f3` |
| win-x64 apphost | 162,816 | `655085241e43ff6c182796dc4b5ffc669ced39045bf72b826178a8400a2fdcff` |

完整目录 manifest 为 runtime 内 `file-sizes.txt` + `files.sha256`；测试重新计算 aggregate digest，不信任 source-commit 文件本身。

## 已覆盖

启动、`/healthz`、SIGTERM bounded shutdown、restart health、occupied port（有界超时）、缺少配置、invalid port/bind host、只读 data path、Docker env 下 bind/probe 定义、manifest/provenance、日志不含 repository ContentRoot、Windows/systemd 定义对称性。

## Skipped / external gates

本机 uid 1000，read-only path 未 skip；root 环境会显式 skip。Windows：无 host，未执行 service cycle。Docker：`docker version` 精确失败为 `permission denied ... /var/run/docker.sock`，未执行 build/run/health。
