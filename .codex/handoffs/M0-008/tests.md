# M0-008 测试记录

## 执行环境

Linux x86-64，Python 3.12，uid 1000。所有运行目录为系统临时目录；未访问真实资产、NAS 路径、凭据或外部网络。

## 执行命令

`PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s tests/spikes/provider-sandbox -p 'test_*.py' -v`

`for n in $(seq 1 20); do PYTHONDONTWRITEBYTECODE=1 PYTHONPATH=tests/spikes/provider-sandbox python3 -m unittest test_supervisor.ProviderSupervisorTests.test_parent_exit_does_not_leave_descendant_alive -q || exit 1; done`

`git diff --check`

`git diff --name-only 842b9e0d86a659154bc7e9315c8537687a9fb1ad...HEAD` 与 handoff `result.json.changed_files` 的任务级 exact set comparison：预期 19 个文件，结果相等；当前主线已合并原版 M0-008，故 `git diff --name-only main...HEAD` 仅包含本次 correction 的 7 个文件。

`ps -u "$(id -un)" -o pid,ppid,pgid,stat,cmd | grep -E 'worker_fixture|time.sleep\\(30\\)' | grep -v grep || true`

## 架构与契约测试

25 个 unittest 全部通过：复用只读 AssetLink `SchemaStore` 对 manifest 和七类 RPC envelope 做 schema parse/reference/positive/negative validation；required correlation、unknown optional preservation、API negotiation fail-closed、original-write/network policy、bounded frame encode/decode、request write backpressure/deadline、request/response acceptance、safe mode，以及 group-drain failure 的 Outcome 可观察性。

## 通过

- happy response、request correlation 和 L0-safe result；
- deadline、supervisor 强制 process-tree cancel、TERM/KILL escalation 与 process-group reap；RPC `cancel` envelope 未发送/确认；
- frame 在 spawn 前编码，writer thread 与 response reader 共用 `started + manifest deadline`；`no_read` + 合法 900 KiB unknown-optional padding 写回压测试通过，writer 已解除且无残留；
- crash、malformed、oversized frame、stdout/stderr flood；
- descendant PID probe；
- parent-exit descendant cleanup 连续 20/20 轮；fixture 记录 child PGID，测试确认等于 supervisor 捕获 PGID，`_last_group_drained=True`，并在返回前完成有界 PGID 排空；
- RLIMIT_AS、RLIMIT_CPU、RLIMIT_NPROC、RLIMIT_NOFILE；
- restart exponential backoff/circuit breaker；
- safe-mode official/signed vs side-loaded，以及 Provider 缺失时 base L0 browse / external-open metadata 可用；
- namespace 与 systemd user-scope probe 已实际执行；结果按返回值自洽记录，隔离能力仍未全部证明。

## 失败 / 跳过

功能测试无失败、无跳过。能力探针结果不是测试失败，而是明确 blocker：`unshare -n -- /bin/true` returncode 1（Operation not permitted）；`bwrap --unshare-net --ro-bind / / /bin/true` returncode 1（Failed RTM_NEWADDR: Operation not permitted）；`systemd-run --user --scope --quiet /bin/true` returncode 0（仅证明 user scope 可启动，未证明 resource accounting）；`/sys/fs/cgroup` writable 为 false；Windows executor 不可用/未执行。

本次 bounded correction 的回归根因是 parent 先退出时旧实现只在一次性 group kill 后返回，未确认 child 已从捕获 PGID 消失。修复后 supervisor 在 TERM/KILL 后以 `/proc` PGID 成员为观测，最多 200ms 重复直接 KILL 并确认空组；排空超时会在 timeout、crashed、protocol_error、write failure 和 response cleanup 等清理终止路径进入可观察的 `group_drain_timeout` detail，新增 monkeypatch 负例验证该边界。20/20 parent-exit 轮均未触发该超时且无 live child。

## 故障注入与恢复验证

Fixture 最大为单个 2 MiB flood、单 child、64 个 bounded process attempts，不构造 fork bomb；frame header 在分配 payload 前检查；stdout/stderr 按上限排空。测试结束的 process scan 无存活 `worker_fixture` 或 `time.sleep(30)`。

## 性能数据

单次观测：正常 worker 约 40–70ms；CPU limit 约 1.1s；stderr flood 仍按 deadline 结束。数据仅用于 Spike 方向，生产 P95/SLO 需 M0-009 在目标发行环境重新测量。

## 尚未覆盖

真实 Linux delegated cgroup/systemd user unit 的 resource/accounting、namespace+seccomp/sandbox launcher 组合的 network/filesystem enforcement，Windows Job Object/restricted token/AppContainer，RPC cancel acknowledgement，生产 Worker 集成以及 50 万资产的批量调度压测。
