# M0-008 Provider 隔离进程与资源限制 Spike

状态：`partial`（2026-08-31）。Windows Job Object 资源/进程树证据已补齐；
Windows 文件与网络 confinement、Linux hard confinement 和 RPC cancel acknowledgement
仍未通过。

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
边界。

## Windows 11 x64 已执行证据

执行环境为 Windows 11 Enterprise LTSC x64 `10.0.26100`、PowerShell `7.6.4`、
Codex bundled Python `3.12.13` 和 Git `2.53.0.windows.3`。探针只调用 Python
标准库与 Windows 系统 API；所有 worker 均以 suspended 状态创建、先加入带
`JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` 的匿名 Job，再恢复运行。运行目录使用
`m0-008-provider-windows-*` 系统临时目录并在每轮后删除；未访问资产、凭据或外网。

执行命令：

```powershell
$env:PYTHONDONTWRITEBYTECODE = '1'
python.exe -B tests/spikes/provider-sandbox/isolation_probe.py
python.exe -B -m unittest discover -s tests/spikes/provider-sandbox -p 'test_*.py' -v
```

Windows 目标套件发现 30 项：12 passed、0 failed、18 platform-skipped。实际执行的
12 项为 7 项 Provider contract/framing 测试和 5 项 Windows isolation 测试；18 项
Linux namespace/POSIX supervisor 测试只在此 Windows host 跳过，原 Linux 证据未被
本次运行替代或重写。

最终 evidence run 的脱敏观测：

| Windows 场景 | 实机机制 | 结果 |
|---|---|---|
| active process count | `JOB_OBJECT_LIMIT_ACTIVE_PROCESS=1` | 首个成员计数为 1；第二次 `AssignProcessToJobObject` 被拒绝，Win32 error `1816`；close 后首个进程已终止 |
| process memory | `JOB_OBJECT_LIMIT_PROCESS_MEMORY=64 MiB` | worker 在分配 57,671,680 bytes 后得到 `MemoryError`；job peak 66,461,696 bytes，未超过 67,108,864-byte cap |
| process CPU time | `JOB_OBJECT_LIMIT_PROCESS_TIME=500 ms` | busy worker 未生成 30 秒完成报告，退出状态 `0xC0000044` (`STATUS_QUOTA_EXCEEDED`)；Job 最终 active count 为 0；周期性检查导致本轮 wall termination 为 4,140 ms，因此该原语不能替代请求 deadline |
| kill-on-close / descendants | `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` + inherited Job membership | close 前观测 4 个 active members；直接后代 `IsProcessInJob=true`；close 后 root 与记录的后代 handle 均在 5 秒界内 signaled |
| Restricted Token | `CreateRestrictedToken(DISABLE_MAX_PRIVILEGE)` + restricting SID list + `CreateProcessAsUserW` | `IsTokenRestricted=true`；token privilege count `24 -> 1`、enabled `4 -> 1`；worker 正常退出 |
| file/network capability under that token | task-temp ACL + `ping.exe 127.0.0.1` | 当前用户 ACL、Interactive group ACL、同用户 sibling temp 均可读写；loopback 可达；因此 `filesystem_confinement_proven=false`、`network_denial_proven=false` |

Restricted Token 本轮只证明降权进程可启动及其实际能力，不证明沙箱。探针为保证系统
运行时可启动，将当前 token 的 enabled SID 集纳入 restricting SID list；这没有建立
“只读输入 + 唯一可写 temp”文件边界，也没有拒绝网络。正式 Provider 仍需执行并验证
AppContainer/Win32 app isolation 或等价的 capability/ACL/network profile；不能用
manifest 的 `network=false`、Restricted Token 或 Job Object 替代。

结论与微软官方语义一致：Job member 默认把子进程带入同一 Job，active-process、
per-process user time、per-process committed memory 与 kill-on-close 均是 Job limits；
Restricted Token 的 SID 列表参与第二次 access check，而 `DISABLE_MAX_PRIVILEGE` 只负责
删除绝大多数 privileges。参考：

- [Job Objects](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects)
- [AssignProcessToJobObject](https://learn.microsoft.com/en-us/windows/win32/api/jobapi2/nf-jobapi2-assignprocesstojobobject)
- [JOBOBJECT_BASIC_LIMIT_INFORMATION](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-jobobject_basic_limit_information)
- [Restricted Tokens](https://learn.microsoft.com/en-us/windows/win32/secauthz/restricted-tokens)
- [CreateRestrictedToken](https://learn.microsoft.com/en-us/windows/win32/api/securitybaseapi/nf-securitybaseapi-createrestrictedtoken)
- [CreateProcessAsUserW](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-createprocessasuserw)
- [AppContainer isolation](https://learn.microsoft.com/en-us/windows/win32/secauthz/appcontainer-isolation)

## 平台候选映射（待 M0-009 选择与执行）

| 平台 | 候选机制 | 本 Spike 证据 | M0-009 门禁 |
|---|---|---|---|
| Linux | cgroup v2（优先 systemd delegated user unit）；namespace、seccomp 和经批准 sandbox launcher；rlimit 作为补充 | rlimit CPU/AS/NPROC/NOFILE 已执行；systemd user scope 返回 0 但未证明 resource accounting；cgroup root 不可写；unshare/bwrap 网络探针失败 | 在真实发行宿主执行 hard network/filesystem/descendant accounting，记录 overhead 与回收时限 |
| Windows | Job Object active process/memory/CPU-time limits、kill-on-close；Restricted Token 或 AppContainer，并显式 capability | Job Object 四项 executed/pass；Restricted Token 降权与实际 file/loopback 能力 executed；Restricted Token 未拒绝同用户文件或网络 | 执行 AppContainer/等价文件与网络 capability profile；测量 production worker overhead，并保留 Job CPU periodic-enforcement 风险 |

manifest 的 `network=true` 只可表示“请求 supervisor-approved profile”，没有
profile 就拒绝；默认 false。`original_write` 永远是 `false`，`input_tokens_only`
永远是 `true`。safe mode 只允许 `official_signed`，side-loaded 只能在显式
test library policy 下运行，不能自行扩大权限。签名实现不在本 Spike 范围内。

## M0-009 必须冻结的决策

1. Linux 正式发行选择 delegated cgroup/systemd 还是受批准的 sandbox launcher，
   并为 network/filesystem/seccomp 给出真实执行证据；不得把本目录的 manifest
   校验当成 OS enforcement。
2. Windows Job Object + AppContainer/Win32 app isolation 或等价 profile 的组合及其
   capability 列表；Job Object 可冻结为资源/进程树候选，但 Restricted Token 单独使用
   无法满足文件/网络 deny-by-default。
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

Windows Job Object 不再是 blocker；Windows 文件/网络 sandbox、Linux hard
confinement、RPC cancel acknowledgement 与 production overhead 仍是 `partial` 的明确
blockers。
