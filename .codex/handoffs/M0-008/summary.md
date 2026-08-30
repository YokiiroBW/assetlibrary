# M0-008 交接摘要

## 完成状态

`partial`

## 完成内容

- 在 `contracts/providers/**` 交付 manifest 1.0 与 RPC 1.0 candidate，覆盖 discover、handshake、request、result、error、cancel、health。
- 固定 4-byte big-endian bounded framing、correlation、opaque input token、artifact token、deadline、资源上限、默认拒网和 `original_write=false`。
- 在 test-only Python fixture 中执行 supervisor/worker 的超时、supervisor 强制 process-tree cancel、崩溃、malformed/oversize frame、stdout/stderr flood、子进程清理、CPU/内存/进程/descriptor limit、restart/backoff/circuit breaker、安全模式和 L0 降级；父进程先退出场景连续 20 次确认 child 继承捕获 PGID 且返回前已排空。
- 所有 fixture 只使用系统临时目录；suite 后没有存活的 worker 或 child，未产生 tracked 二进制或 Python cache。

## 关键决策

`partial` 是准确结论。rlimit 与 process-group cleanup 在 Linux 主机上有执行证据，但 `unshare -n` 和 `bwrap --unshare-net` 均因 `Operation not permitted` 失败，cgroup v2 根不可写，故 network/filesystem namespace、read-only input、writable-temp 和 cgroup child accounting 未证明。没有 Windows executor，Job Object/restricted-token/AppContainer 只记录为候选映射。

## 修改文件

implementation commits `86d95094b019c9e9c000840207e26e58dd5232c5`, `75da89129cca86bdbaf8f052ab7c7ed32cfa100c`, `852ebb1f7b4d1f692a1f87086648a6ebfbb909de`, `9cac6ac84fa88849357a92ebbfb24de5cee01b69`, `24b4f3af933ee9a01d6929cdb6057911ea9c1f3e`, and final correction `5251e8306555f24154337237c94525356dfca293` 包含：

- `contracts/providers/README.md`
- `contracts/providers/provider-manifest.schema.json`
- `contracts/providers/provider-rpc.schema.json`
- `docs/spikes/M0-008/README.md`
- `tests/spikes/provider-sandbox/` 下 11 个契约、fixture、supervisor、worker、probe 文件

## 模块边界、依赖方向与复用

模块为 `provider-sandbox-spike`，所有权为 M0-008。只依赖 versioned Provider contract；没有引用 production services/providers/apps/infra，也没有跨模块写表。复用既有 Provider manifest 文件并在其边界上演进；没有复制核心物理操作、权限或 AssetLink 业务逻辑。

## 新语言、框架或重大依赖

没有 production language/framework/runtime/dependency。Python 3 standard library 仅用于 test-only supervisor、worker fixture 和 probes，符合工具预算。

## 共享契约或数据库变化

仅变更 `contracts/providers/**`（本任务唯一契约所有者）；无数据库迁移、无其他共享契约变化。Provider candidate 尚未成为 production API，须由 M0-009 冻结。RPC schema validation 复用了只读 AssetLink `SchemaStore`，未新增依赖或复制 validator。

## 测试结果

`PYTHONDONTWRITEBYTECODE=1 PYTHONPATH=tests/spikes/provider-sandbox python3 -m unittest test_supervisor.ProviderSupervisorTests.test_parent_exit_does_not_leave_descendant_alive -q` 连续 20/20 通过；每轮 child PGID 等于 supervisor 捕获 PGID，`_last_group_drained=True`，且 child probe 为 dead。

`PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s tests/spikes/provider-sandbox -p 'test_*.py' -v`：24 passed，0 failed，0 skipped。

## 架构测试与质量门禁

复用只读 AssetLink `SchemaStore` 对 manifest 和七类 RPC envelope 做 schema parse/reference/positive/negative validation；未知字段保留、bounded framing、请求/响应接受边界与全部故障 fixture 已通过。任务范围验证及全局 handoff/架构门禁由协调线程在合并时复跑。

## 文件安全、权限与性能影响

Provider boundary 不接受任意 host path；输入为 supervisor token，输出为 metadata/suggestions/supervisor artifact。默认拒网、永久禁止 original write；所有 I/O 有 deadline/size cap，stderr drain 避免管道死锁。正常 fixture 启动观测约 40–70ms；CPU fault 约 1.1s 被内核终止；不是产品 SLO。50 万资产场景仍需批量调度与缓存设计，不能逐资产无界创建进程。

## 技术债、已知问题与风险

- Linux hard network/filesystem confinement、cgroup delegation/accounting 和 seccomp 未执行通过，不能声称安全沙箱。
- Windows Job Object + restricted token/AppContainer 没有实机证据。
- 当前 `cancel()` 是 supervisor 强制 process-tree cancel，没有发送/确认 RPC `cancel` envelope；RPC cancel acknowledgement、restart budget、日志字段和目标平台 overhead 需要 M0-009 冻结/实测。
- 进程组清理的根因是 parent 先退出时旧实现只做一次 kill、未在返回前确认 PGID 已空；当前实现以捕获 PGID 为边界，重复观察并直接 KILL 成员，窗口耗尽会显式报告 `group_drain_timeout`。

## 建议合并顺序

先审查并合并 implementation commit，再审查本 handoff metadata commit；仅在 M0-009 补齐平台 enforcement 证据后进入生产 Worker 设计。

## 下一步

M0-009 冻结 Linux/Windows enforcement 机制、RPC/limits/cancel 语义、Provider restart policy 与 50 万资产调度 overhead，并把失败隔离统一映射为 L0。
