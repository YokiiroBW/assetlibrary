# M0-004 测试记录（clean replacement）

## 执行环境

Linux x86-64，Python 3.12；从空 `.runtime/sandbox-storage/M0-004` bootstrap；SDK 10.0.111，ASP.NET Core/.NET runtime pack 10.0.11；Docker CLI 26.1.4，无 daemon 权限；无 Windows/PowerShell。

## 命令与结果

- `M0_004_DOTNET_DIR=/tmp/m0-004-dotnet-10.0.111 bash tests/spikes/server-packaging/bootstrap.sh` — 通过；clean provenance、restore、linux-x64/win-x64 publish。
- `M0_004_DOTNET_DIR=/tmp/m0-004-dotnet-10.0.111 PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s tests/spikes/server-packaging -p 'test_*.py' -v` — 6 passed。
- `docker compose -f tests/spikes/server-packaging/docker-compose.yml config` — 通过。
- `git diff --check` — 通过。

## Artifact matrix

发行输入最后提交：`b5d19cebafdb536a61b33917715419528e7f10e1`；协调器在关闭 MSBuild/C# build servers 后连续两次 cold publish 的完整 `files.sha256` 一致；aggregate digest：`c0f434d0556eadac727ce1601afece3b9e907ef3be7a8b2769e6eb141a5a7ff2`。以下 hash 是该 Linux 构建环境的本机观测，不是跨主机 bit-for-bit 保证；身份由 commit 与完整清单共同证明。

| target | exact bytes | SHA-256 |
|---|---:|---|
| linux-x64 apphost | 78,256 | `a2e5c0a1d967b573721b94663d67a4e9fcca4483c8c461c54c2cf9d2a48586f3` |
| win-x64 apphost | 162,816 | `25587fad799168cd11efeeed3e00c4508e6abcfe78269174f49831c85961f2d9` |

完整目录 manifest 为 runtime 内 `file-sizes.txt` + `files.sha256`；测试重新计算 aggregate digest，不信任 source-commit 文件本身，并连续两次清理 obj/bin/artifact 后比较完整清单。

## 已覆盖

启动、`/healthz`、SIGTERM bounded shutdown、restart health、occupied port（有界超时）、缺少配置、invalid port/bind host、只读 data path、Docker env 下 bind/probe 定义、manifest/provenance、日志不含 repository ContentRoot、Windows/systemd 定义对称性。

## Skipped / external gates

本机 uid 1000，read-only path 未 skip；root 环境会显式 skip。Windows：无 host，未执行 service cycle。Docker：`docker version` 精确失败为 `permission denied ... /var/run/docker.sock`，未执行 build/run/health。

## 2026-09-02 correction

新增独立 `test_windows_service_contract.py`，只读脚本文本，不导入或触发 `test_spike.py`；9/9 通过。新增覆盖括号化 Test-Path 预检、wrapper 原文与 marker 双重比较、服务账户统一 readback、created-this-run 清理、服务仍存在时保留文件、Data 空目录限制、service 不存在时残留三态，以及 stop/delete 有界等待与最终 readback。未执行 Windows/PowerShell、dotnet、Docker 或重型 publish。

## 2026-09-02 direct-executable correction

当前源码契约检查覆盖 direct EXE binPath/no `.cmd`、CLI 映射与 probe JSON 校验、系统临时 staging、SID ACL、完整 LocalService、mutex、GUID/Description marker 所有权、PID/listener health、Stopwatch bounded wait、未知路径无递归删除。测试仅为独立静态测试；未重新 publish/build，历史 artifact/hash 不代表当前源码。
