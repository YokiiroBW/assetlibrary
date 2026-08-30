# M0-008 测试记录

## 执行环境

Linux x86-64，Python 3.12，uid 1000。所有运行目录为系统临时目录；未访问真实资产、NAS 路径、凭据或外部网络。

## 执行命令

`PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s tests/spikes/provider-sandbox -p 'test_*.py' -v`

`git diff --check`

`ps -u "$(id -un)" -o pid,ppid,pgid,stat,cmd | grep -E 'worker_fixture|time.sleep\\(30\\)' | grep -v grep || true`

## 架构与契约测试

17 个 unittest 全部通过：manifest schema parse、RPC envelope catalog、required correlation、unknown optional preservation、API negotiation fail-closed、original-write/network policy、bounded frame encode/decode、safe mode。

## 通过

- happy response、request correlation 和 L0-safe result；
- deadline、explicit cancel、TERM/KILL escalation 与 process-group reap；
- crash、malformed、oversized frame、stdout/stderr flood；
- descendant PID probe；
- RLIMIT_AS、RLIMIT_CPU、RLIMIT_NPROC、RLIMIT_NOFILE；
- restart exponential backoff/circuit breaker；
- safe-mode official/signed vs side-loaded，以及 Provider 缺失时 base L0 browse / external-open metadata 可用；
- namespace probe 已实际执行，结果维持未证明。

## 失败 / 跳过

功能测试无失败、无跳过。能力探针结果不是测试失败，而是明确 blocker：`unshare -n -- /bin/true` returncode 1（Operation not permitted）；`bwrap --unshare-net --ro-bind / / /bin/true` returncode 1（Failed RTM_NEWADDR: Operation not permitted）；`/sys/fs/cgroup` writable 为 false；Windows executor 不可用/未执行。

## 故障注入与恢复验证

Fixture 最大为单个 2 MiB flood、单 child、64 个 bounded process attempts，不构造 fork bomb；frame header 在分配 payload 前检查；stdout/stderr 按上限排空。测试结束的 process scan 无存活 `worker_fixture` 或 `time.sleep(30)`。

## 性能数据

单次观测：正常 worker 约 40–70ms；CPU limit 约 1.1s；stderr flood 仍按 deadline 结束。数据仅用于 Spike 方向，生产 P95/SLO 需 M0-009 在目标发行环境重新测量。

## 尚未覆盖

真实 Linux delegated cgroup/systemd user unit、namespace+seccomp/sandbox launcher 组合的 network/filesystem enforcement，Windows Job Object/restricted token/AppContainer，生产 Worker 集成以及 50 万资产的批量调度压测。
