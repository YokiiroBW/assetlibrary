# M0-008 Windows 外部门禁测试记录

## 执行环境

- 时间：2026-08-31（Asia/Shanghai）。
- Windows 11 Enterprise LTSC x64，version/build `10.0.26100`。
- PowerShell `7.6.4`；Codex bundled Python `3.12.13`；Git
  `2.53.0.windows.3`。
- 分支 `codex/m0-008-windows-evidence`；evidence commit
  `68fe9aea9e43e828d073f6ae193405b775fd787f`。
- 所有 runtime fixture 位于 task-owned `m0-008-provider-windows-*` 系统临时目录；
  未访问真实资产、NAS 库、凭据或外部网络。

## 执行命令与结果

以下 `python.exe` 均指 Codex bundled Python 3.12.13，handoff 不记录用户私有绝对路径。

| 命令 | 结果 |
|---|---|
| `$env:PYTHONDONTWRITEBYTECODE='1'; python.exe -B tests/spikes/provider-sandbox/isolation_probe.py` | pass，exit 0；四项 Job Object probe passed；Restricted Token capability probe executed |
| `$env:PYTHONDONTWRITEBYTECODE='1'; python.exe -B -m unittest discover -s tests/spikes/provider-sandbox -p 'test_*.py' -v` | 30 discovered；12 passed；0 failed；18 platform-skipped |
| `python.exe -B scripts/validate_handoff.py` | pass |
| `python.exe -B scripts/validate_architecture_baseline.py` | pass |
| `python.exe -B scripts/verify_repository.py` | pass |
| `git diff --check` | pass |

## Windows Job Object 观测

| 能力 | 结果 |
|---|---|
| active process limit | cap=1；首次 active=1；第二个 assign denied；Win32 error=1816；close 后首进程 dead |
| process memory | cap=67,108,864 bytes；worker allocated=57,671,680 bytes 后 `MemoryError`；peak=66,461,696 bytes；worker exited |
| process CPU user time | configured=500 ms；completion report absent；exit=`0xC0000044`；active-after=0；wall termination=4,140 ms；Job-accounted CPU=4,125 ms |
| kill-on-close / descendants | active-before=4；recorded descendant in Job=true；root dead=true；descendant dead=true |

CPU 数值同时证明限制触发和周期检查的非精确性；不把 500 ms 配置误写成 500 ms wall
deadline。request deadline 仍必须由 supervisor 单独实现。

## Restricted Token 文件/网络能力

- `CreateRestrictedToken(DISABLE_MAX_PRIVILEGE)` + `CreateProcessAsUserW`：executed。
- `IsTokenRestricted=true`；privilege total `24 -> 1`，enabled `4 -> 1`。
- current-user ACL read/write=true；Interactive-group ACL read/write=true；same-user sibling
  temp read/write=true。
- `ping.exe 127.0.0.1`=true；没有访问外部网络。
- `filesystem_confinement_proven=false`；`network_denial_proven=false`。

结论：Restricted Token 降权执行候选已验证，但文件/网络 sandbox 未验证。AppContainer/
等价 capability profile 继续 blocker。

## 平台跳过与原有证据边界

Windows suite 的 18 项 skip 是 2 项 Linux isolation mapping 与 16 项 POSIX supervisor
测试。既有主线已保存 Linux 25/25 证据；本次不在 Windows 上伪造 RLIMIT、`/proc`
process-group、namespace 或 cgroup 结果。没有新 Linux hard-confinement 证据，相关 blocker
保持不变。

Provider RPC `cancel` acknowledgement 也没有新增执行证据；现有强制 Job/process-tree
termination 不能替代协议 acknowledgement。

## 清理与范围验证

- surviving `windows_worker_fixture.py` / `restricted-worker.cmd` process：0。
- task-owned `m0-008-provider-*` temp residue：0。
- `tests/spikes/provider-sandbox/**` 下 `__pycache__` / `.pyc`：0。
- tracked `.exe`、`.dll`、`.pdb`、`.log`、registry export 或 raw evidence：0。
- 最终 diff 只包含 `tests/spikes/provider-sandbox/**`、`docs/spikes/M0-008/**` 和
  `.codex/handoffs/M0-008/**`；公共 contracts 与生产目录均未修改。
