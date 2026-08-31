# M0-008 Windows 外部门禁交接摘要

## 完成状态

`partial`

Windows evidence/implementation commit：
`68fe9aea9e43e828d073f6ae193405b775fd787f`。本摘要、`result.json` 与
`tests.md` 作为第二阶段 handoff metadata 单独提交并引用该 commit。

## 本次完成内容

- 在真实 Windows 11 x64 `10.0.26100` 上执行 test-only Job Object probe，证明
  active-process count、64 MiB per-process committed-memory、500 ms per-process
  user-time limit、`JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`、默认后代 Job 继承和后代
  回收。所有 root process 都以 suspended 状态创建，加入匿名 Job 后才恢复运行。
- active-process limit 配置为 1；第二个 assign 被 Win32 error `1816` 拒绝；关闭 Job
  后首个成员已终止。
- memory worker 在分配 57,671,680 bytes 后得到 `MemoryError`；Job peak 为
  66,461,696 bytes，未超过 67,108,864-byte cap。
- CPU busy worker 被系统以 `0xC0000044` (`STATUS_QUOTA_EXCEEDED`) 终止，未产生
  30 秒完成报告，Job 最终 active count 为 0。本轮 wall termination 为 4,140 ms，
  说明 Job 的周期性 user-time 检查不能替代 Provider request deadline。
- kill-on-close 前观测到 bundled runtime/root/descendant 共 4 个 active Job members；
  记录的直接后代 `IsProcessInJob=true`；关闭唯一 Job handle 后 root 与后代均在
  5 秒界内 signaled。
- 通过 `CreateRestrictedToken(DISABLE_MAX_PRIVILEGE)` 和 `CreateProcessAsUserW`
  启动实际 restricted process：`IsTokenRestricted=true`，privilege total `24 -> 1`、
  enabled `4 -> 1`。为让系统运行时可执行，restricting SID list 保留当前 token 的
  enabled SID 集。
- Restricted Token 进程仍可读写当前用户 ACL、Interactive group ACL 与同用户 sibling
  temp，并可 `ping.exe 127.0.0.1`。因此它只证明降权，不证明文件或网络 sandbox；
  `filesystem_confinement_proven=false`、`network_denial_proven=false` 保持真实。

## 未完成门禁与 blockers

- Windows AppContainer/Win32 app isolation 或等价 capability/ACL/network profile 未
  执行；Restricted Token 与 Job Object 的组合不能单独满足 deny-by-default 文件/网络
  隔离。
- 本次没有新增 Linux host 证据。原 handoff 中 delegated cgroup/accounting、namespace、
  seccomp、只读 input 与唯一可写 temp 的 hard confinement blockers 保持不变。
- 当前 supervisor 仍只做强制 process-tree cancellation；Provider RPC `cancel` envelope
  的发送与 acknowledgement 没有执行证据。
- production worker startup/IPC/sandbox overhead 和 50 万资产下的批量调度仍未验证。

因此不能把 M0-008 改为 `completed`，也不能把该探针描述为完整安全沙箱。

## 修改文件

- `tests/spikes/provider-sandbox/isolation_probe.py`
- `tests/spikes/provider-sandbox/supervisor.py`
- `tests/spikes/provider-sandbox/test_isolation.py`
- `tests/spikes/provider-sandbox/test_supervisor.py`
- `tests/spikes/provider-sandbox/windows_isolation_probe.py`
- `tests/spikes/provider-sandbox/windows_worker_fixture.py`
- `docs/spikes/M0-008/README.md`
- `.codex/handoffs/M0-008/summary.md`
- `.codex/handoffs/M0-008/result.json`
- `.codex/handoffs/M0-008/tests.md`

没有修改 `contracts/providers/**`、生产目录、公共接口、ADR、任务包、任务注册表、
项目状态或 M0-009。

## 架构、依赖与复用

模块仍为 `provider-sandbox-spike`。新增代码只位于 test-only Spike；复用既有
manifest/RPC contract 测试和 Python 标准库，不导入 production supervisor/provider，
不复制核心文件操作、权限或 AssetLink 逻辑。没有新增语言、框架、第三方依赖、
数据库迁移或公共契约变化。

Windows API 语义与参考链接记录在 `docs/spikes/M0-008/README.md`；本 Spike 只提供
候选证据，M0-009 才能冻结正式实现。

## 测试与清理

- Windows target suite：30 discovered，12 passed，0 failed，18 platform-skipped。
  12 项实际执行包括 7 项 Provider contract/framing 与 5 项 Windows isolation；18 项
  为 Linux namespace/POSIX supervisor 用例，不用 Windows 假结果替代。
- `isolation_probe.py` 返回 0，且 Job Object 四项与 Restricted Token capability probe
  均得到可观察结果。
- `validate_handoff.py`、`validate_architecture_baseline.py`、`verify_repository.py`、
  `git diff --check` 均通过，详见 `tests.md`。
- suite 后 surviving Windows worker/process、`m0-008-provider-*` temp、Python cache、
  tracked binary/log/registry export 均为 0。

## 文件安全、权限与性能影响

所有文件写入只发生在 task-owned 系统临时目录，ACL 只修改这些临时目录；没有管理员
操作、全局安装、系统注册表、外网、真实资产或凭据访问。active-process=1、memory=64
MiB、CPU fixture≤30 秒、Job close/wait≤5 秒，所有压力严格有界。CPU limit 的周期检查
延迟是已记录风险，而不是被隐藏成 deadline 保证。

## 建议合并顺序与下一步

先审查 evidence/implementation commit，再审查 handoff metadata commit。M0-009 可把
Job Object process/memory/user-time/kill-on-close 作为 Windows 候选，但必须继续取得
AppContainer/等价文件与网络 enforcement、Linux hard confinement 和 RPC cancel
acknowledgement 证据后，才能判定 M0-008 完成。
