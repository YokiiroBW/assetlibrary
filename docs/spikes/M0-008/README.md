# M0-008 Provider 隔离进程与资源限制 Spike

状态：`partial`（2026-08-31）。

本 Spike 冻结了一个可供 M0-009 评审的 Provider manifest/RPC 候选，并在
test-only Python supervisor/fixture 中执行了进程边界、长度前缀 framing、绝对
deadline、supervisor 强制 process-tree cancel、崩溃恢复、输出洪泛、资源上限、后代清理、重启退避、安全模式
和 L0 降级验证。fixture 只使用系统临时目录，未访问真实资产、凭据或外部网络。

## 已执行证据

命令：

```text
PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover \
  -s tests/spikes/provider-sandbox -p 'test_*.py' -v
```

结果：25 tests，全部通过。

代表性观测（单次执行的耗时会因主机负载变化）：

| 场景 | 机制 | 观测结果 |
|---|---|---|
| happy | 4-byte big-endian length + JSON | `ok`, correlation=`req-1`, `original_write=false` |
| hang / supervisor cancel | absolute deadline + process-group TERM/KILL | `timeout`, worker reaped；当前没有发送/确认 RPC `cancel` envelope |
| crash | worker `os._exit(23)` | `crashed`, return code `23` |
| malformed / oversize / stdout flood | bounded frame reader rejects before payload allocation | `protocol_error` |
| stderr flood | bounded drain (8 KiB retained) | deadline still fired; no pipe deadlock |
| no_read + 900 KiB unknown optional padding | pre-encoded frame + writer thread + same absolute deadline | write backpressure timeout bounded; writer released and process tree reaped |
| descendant | worker spawns one bounded `sleep`, same process group；另测 parent 先退出 | timeout/EOF 后 pid probe 显示 child 不存活；parent-exit case 连续 20/20 次确认捕获 PGID 已排空 |
| memory | Linux `RLIMIT_AS=64 MiB` | worker returns `memory_limit` |
| CPU | Linux `RLIMIT_CPU=1 s` | return code `-24` (`SIGXCPU`) |
| descriptors | Linux `RLIMIT_NOFILE=32` | worker returns `descriptor_limit`, 29 descriptors opened |
| processes | Linux `RLIMIT_NPROC=8` | worker returns `process_limit`, 0 extra child in this host baseline |
| restart | 3 failures, exponential delay 10/20/40 ms, circuit open | deterministic unit proof |

supervisor 在 dispatch 前检查 request message type/version/id、operation、opaque
input token、deadline 和 request frame 大小；响应 frame 按 manifest 与 request
上限的较小值读取，并在接受前检查 correlation/version/type/status、只读标记、
artifact 数量、token/size/hash。`/etc/...` 等 host path 在 spawn 前拒绝；结果只允许 metadata、suggestions
和 supervisor-owned artifact token。结果不会生成或执行核心物理操作计划。

进程清理先记录 worker 的 session/process group，向整个组升级发送 TERM/KILL，
再在有界 200ms 窗口内重复观察 `/proc` 的 PGID 成员并直接补发 KILL，确认组为空后
才返回。此前仅在 `wait()` 超时分支做一次 group kill，parent 先退出时可能在 child
被重新托管前返回；该竞态由 parent-exit fixture（同时记录 child PID/PGID）和排空断言覆盖。
若排空窗口耗尽，所有经过清理的终止结果 `Outcome.detail` 都会包含 `group_drain_timeout`，不会把清理失败伪装成普通结果。

## 执行过的隔离探针

探针命令不是静态声明，而是实际执行：

```text
unshare -n -- /bin/true
# returncode=1
# unshare: unshare failed: Operation not permitted

bwrap --unshare-net --ro-bind / / /bin/true
# returncode=1
# bwrap: loopback: Failed RTM_NEWADDR: Operation not permitted

systemd-run --user --scope --quiet /bin/true
# returncode=0（user scope 可启动；未证明 resource accounting）
```

本机 `/sys/fs/cgroup` 根不可写；systemd user scope probe 返回 0，但没有在该 scope
中施加资源限制或读取 child accounting 的进一步执行证据。上述 namespace/bwrap
失败意味着 hard network denial、read-only input exposure、writable-temp confinement
和 child-tree cgroup accounting 均不能在本 Spike 中宣称通过；rlimit 不能替代这些
边界。没有 Windows 主机或 PowerShell，
Windows 证据为未执行。

## 平台候选映射（待 M0-009 选择与执行）

| 平台 | 候选机制 | 本 Spike 证据 | M0-009 门禁 |
|---|---|---|---|
| Linux | cgroup v2（优先 systemd delegated user unit）；namespace、seccomp 和经批准 sandbox launcher；rlimit 作为补充 | rlimit CPU/AS/NPROC/NOFILE 已执行；systemd user scope 返回 0 但未证明 resource accounting；cgroup root 不可写；unshare/bwrap 网络探针失败 | 在真实发行宿主执行 hard network/filesystem/descendant accounting，记录 overhead 与回收时限 |
| Windows | Job Object active process/memory/CPU limits、kill-on-close；Restricted Token 或 AppContainer，并显式 capability | 未执行（无 Windows executor） | Windows 11 x64 实机证明 Job Object 后代回收、文件/网络 capability 和 safe mode |

manifest 的 `network=true` 只可表示“请求 supervisor-approved profile”，没有
profile 就拒绝；默认 false。`original_write` 永远是 `false`，`input_tokens_only`
永远是 `true`。safe mode 只允许 `official_signed`，side-loaded 只能在显式
test library policy 下运行，不能自行扩大权限。签名实现不在本 Spike 范围内。

## M0-009 必须冻结的决策

1. Linux 正式发行选择 delegated cgroup/systemd 还是受批准的 sandbox launcher，
   并为 network/filesystem/seccomp 给出真实执行证据；不得把本目录的 manifest
   校验当成 OS enforcement。
2. Windows Job Object + restricted-token/AppContainer 组合及其 capability 列表。
3. production Worker 使用的 RPC frame、message/artifact limits、deadline 和
   cancellation acknowledgement 语义；本 Spike contract 仍是 candidate。
4. Provider restart budget、circuit-breaker 状态、日志脱敏字段以及 50 万资产
   下的调度/缓存 overhead。
5. 是否在 RPC 边界实现 `cancel` envelope 的发送与 acknowledgement（本 Spike 仅
   证明 supervisor 强制终止）；Provider 失败统一降级 L0；缺少 Provider 时基础浏览、身份和 external-open
   metadata 必须继续可用。

## 依赖、性能和技术债

实现只用 Python 3 标准库，Python 仅为测试 supervisor/opaque fixture；未新增
生产 runtime、package 或许可证负担。正常 fixture 启动约 40–70 ms；受限 CPU
约 1.1 s 后被内核终止；这些是 Spike 观测，不是产品 SLO。后续需要在目标发行
环境测量 worker startup、IPC、cgroup/sandbox setup 和回收 P95，并确认大批量
请求不会逐文件创建进程。

未证明的 OS 隔离能力和 Windows 执行器是 `partial` 的明确 blockers。
