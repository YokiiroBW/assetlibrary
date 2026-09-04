# V01-008 测试记录

## 执行环境与边界

- Worktree：`C:\YOKI\Codex\worktrees\V01-008`；实现与安全修复 commit `81eb838136b9826c9ed6e0ac9e1ae9fdb42cfd49`。
- 当前 runner：Windows 11 x64、非 elevated Codex 会话；没有注册/停止服务、写 HKLM、启用 Windows 功能、修改 UAC/PATH/账户或连接真实资产目录。
- .NET SDK `10.0.111`、ASP.NET Runtime `10.0.11`、Python `3.12.13`、Node `24.20.0`、pnpm `11.19.0`、Temurin JDK `21.0.12+8`、PostgreSQL `16.15`。
- GNU Bash 由 Git for Windows 的固定 runtime 提供，仅用于 `bash -n` 静态语法检查。
- 所有 runtime、NuGet、release、数据库与 state 输出只位于任务 `.runtime/sandbox-storage/V01-008/**`。

## 安全补丁后的执行命令

```text
python -I -B -m unittest discover -s tests/release -p test_*.py -v
python -I -B -m unittest discover -s tests/repository -p test_*.py -v
python -I -B -m unittest discover -s tests/architecture -p test_*.py -v
python -I -B scripts/verify_repository.py

Python AST parse: scripts/build_server_release.py, scripts/validate_server_release.py,
  tests/release/test_server_release.py, tests/repository/test_server_packaging_foundation.py
PowerShell AST parse: infra/windows-server/service-evidence.ps1
<Git-for-Windows>/usr/bin/sh.exe -n infra/linux-server/systemd-evidence.sh
git diff --check

pwsh -NoProfile -File infra/windows-server/service-evidence.ps1 -Action preflight \
  -ExpectedComputerName <current-computer> -ExpectedSourceRevision <revision> \
  -ExpectedArtifactTreeSha256 <win-tree-sha256> \
  -ExpectedRuntimeEvidenceBindingSha256 <win-binding-sha256>

python -I -B scripts/build_server_release.py \
  --dotnet C:\YOKI\Codex\worktrees\V01-004\.runtime\sandbox-storage\V01-004\dotnet\dotnet.exe
python -I -B scripts/validate_server_release.py --require-artifacts

python -I -B tests/architecture/check_release_gates.py --target v0.1-start
python -I -B tests/architecture/check_release_gates.py --target windows-server-release
python -I -B tests/architecture/check_release_gates.py --target docker-release
python -I -B tests/architecture/check_release_gates.py --target linux-server-release
python -I -B tests/architecture/check_release_gates.py --target production-file-writes
python -I -B tests/architecture/check_release_gates.py --target v0.1-release
```

最终 artifact 进程 smoke 使用
`.runtime/sandbox-storage/V01-008/release-build/artifacts/win-x64/AssetLibrary.CoreServer.Host.exe`，
随机 loopback 端口和任务沙盒内独立 state 目录；启动后读取精确 `/healthz`、`/readyz`，再执行非提权
`--health-probe`，最后只终止该测试子进程并检查 listener/state 残留。

## 安全补丁后的结果

- Release Python：19/19 passed。
- Repository：48/48 passed；architecture：14/14 passed；`verify_repository.py` 全链通过。
- Python AST、PowerShell AST、GNU Bash `-n` 与 `git diff --check` 通过。
- Windows 当前宿主只读 preflight：`elevated=false`、`computer_matches=true`；因未布置 VM evidence artifact，boundary/artifact 为 false；service/HKLM/secure staging/state/process/listener 残留全部 false/0，未进行写动作。
- 两次独立 cold publish 均从 `81eb838...` 的 Git source snapshot 构建；679 文件清单一致。
- 独立 `validate_server_release.py --require-artifacts` 返回 `errors=[]`，复核 manifest、locks、archives、source/issuance/runtime binding 全部一致。
- 最终 win-x64 EXE：`health={status:ok, contract:v01-008/1}`；`ready={status:ready, contract:v01-008/1, scope:host_only, business_api_ready:false, production_file_writes_enabled:false}`；独立 probe exit 0；停止后 listener 可重绑、state residue count 0。
- Gate：`v0.1-start=0`；其余五个未满足目标均按设计 `exit=3`，没有被误记为通过。

## 发行证据

- source revision：`81eb838136b9826c9ed6e0ac9e1ae9fdb42cfd49`
- issuance input tree SHA-256：`e53b1d0c33e593e87b075c39439190d06df05bd68d4b2b8c663ec4d8e1bf7bcd`
- artifact aggregate SHA-256：`c5ed8786479e07a81803f5ab57bbe238365bcc3e2a41c9350f7bb846801c9e3f`
- release-lock aggregate SHA-256：`73a5aa88f3be7d06c7704ebb7cf2bfdb4116973f0b59dd8a270e84de1100e442`
- Linux archive：48,219,608 bytes，SHA-256 `0e88179a4e5ac107e6719c6d9e255f13f64482a1f123e31842c96ac243c4b414`
- Windows archive：49,127,994 bytes，SHA-256 `d117bf500ec869686049e4936fa585597ba3b7a0f0ce50f7e1875a24e388c5da`
- runtime aggregate：Linux `15767229c96f60213b3e644ab54aa4d11b2239c2d4babccd926b88f41cc35084`；Windows `0994990bdfde5e00f845a178305ad763eff0f00e21faf5175a21307326dd952a`
- runtime binding：Linux `d0eab2932902aa454b03e3de6779a4a15e0f54d74105fe3185f999d796d2340b`；Windows `d2b216383051f7e706a1cdd7295dbaec0f5665d5d84a460613531989bb6937f4`

## 累计自动测试

唯一计数为 360 passed、0 failed、0 skipped：

| 套件 | 数量 | 本安全补丁后状态 |
| --- | ---: | --- |
| .NET solution | 185 | 先前完整绿测；补丁未改 C#、项目或 lock 输入 |
| Database | 53 | 先前完整绿测（含 PostgreSQL 16.15 integration 14） |
| Repository | 48 | 已重跑，通过 |
| Architecture | 14 | 已重跑，通过 |
| SDK Python | 14 | 先前完整绿测；输入未变 |
| AssetLink contract/spike | 21 | 先前完整绿测；输入未变 |
| Chromium Web | 6 | 先前完整绿测；输入未变 |
| Release Python | 19 | 已重跑，通过 |

另有不重复计数的 Packaging 定向 18/18、TypeScript SDK 5/5、Kotlin/JVM build、Web build/audit 与完整 .NET Release build（0 warning/0 error）均在同一分支先前通过。按验证约定，安全补丁未影响其源码、配置、依赖或 fixture，因此未重复执行高成本套件。

## 覆盖的负向与故障路径

- 未跟踪/修改的 `Directory.Build.targets`、祖先 targets、外部 MSBuild override、policy 自排除、选择提交与 live tree 不同、无效 revision 后 snapshot 清理均失败关闭。
- 所有发行入口拒绝非 isolated Python；`scripts/**`、props/targets、infra/host/tests/locks 均绑定到 issuance tree。
- Validator 拒绝 traversal、反斜杠、冒号、控制字符、尾随点/空格、Windows 设备别名、错误 archive 名、symlink/reparse、重复/额外 member、长度/hash/source/RID/binding 漂移。
- Archive 在安全句柄中复制并核验到匿名 temp snapshot 后解析，避免路径 check/use 与 live archive 换包。
- Windows/Linux 编排器的静态回归拒绝管理员/root 直接运行 staged artifact helper；安全副本、身份、HTTP-only health 和 owner-guarded cleanup 均有断言。
- Host 的配置、端口占用、只读/不可用 state、健康失败、重启、停止和 listener/state 零残留路径已覆盖。

## 开发期异常与最终状态

- 一次 repository 测试因先前生成的 `scripts/__pycache__` 触发缓存禁入门禁；只删除该精确生成目录后重跑 48/48，通过。最终缓存计数为 0。
- 最终验证链 0 failed、0 skipped；真实平台缺环境按 `blocked_missing_environment` 记录，不作为测试 skip。

## 尚未覆盖

- `YOKICESHI` 快照 VM 中真实 LocalService install/start/HTTP health/stop/uninstall，以及 service/HKLM/secure staging/state/process/listener 零残留闭环。
- 真实 Docker daemon 上 build/up/health/restart、non-root/read-only/no-new-privileges、down `--volumes` 与全部资源零残留。
- 批准 Linux root/systemd runner 上 root-owned staging、专用非 root identity、unit hardening、stop/uninstall 与零残留。
- V01-009 的认证、PostgreSQL runtime composition、业务 API、TLS/secret、迁移/升级/回滚、Alpha integration，以及 M0-006 生产写证据。
