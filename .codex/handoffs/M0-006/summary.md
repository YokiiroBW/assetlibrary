# M0-006 交接摘要

## 完成状态

`partial`

实现 commit：`10c0643a433935f2311fd0b926ad31549f0ee6b3`（包含此前实现与本轮修正）；metadata commit 在本文件提交后回填。

## 完成内容

交付 `tests/spikes/file-safety/file_safety.py` test-only 状态机/真实 Linux adapter 与
`test_file_safety.py` 验收矩阵。状态为 preflight → staged → verified →
target_committed → source_trashed → complete，并显式 cancelled/conflict/manual_attention。
journal 使用 temp+file fsync+atomic replace+directory fsync；恢复按物理 source/stage/target/
trash/hash 判断，不信 COMPLETE。

## 关键决策

- same-device 直接 `renameat2(RENAME_NOREPLACE)`；cross-device 固定 target-root
  `.m006-stage/<op>/payload`，bounded copy/hash/fsync/noreplace/reopen hash 后才移 source
  到 source-root `.m006-trash`。
- trash 使用 deterministic role path（`source-*`/`replacement-*`），metadata 先 durable
  写入，并记录 schema、operation、root_role、reason、original relative path、size/hash。
- O_CREAT|O_EXCL lock 带 operation/owner heartbeat/expiry；expired reclaim 由 engine
  先 inspect 物理候选后进行。

## 修改文件

`tests/spikes/file-safety/file_safety.py`、`tests/spikes/file-safety/test_file_safety.py`、
`.codex/tasks/M0-006.md`、`docs/spikes/M0-006/README.md`、本目录三个 handoff 文件。

## 模块边界、依赖方向与复用

模块：`file-safety-spike`；仅测试夹具/adapter，无生产入口。只读复用 M0-007
`performance.BYTES_100_GIB`，未复制通用 helper；无跨模块写入。

## 新语言、框架或重大依赖

无；Python 3.12 标准库，Linux libc/kernel `renameat2` 仅通过 ctypes capability probe。

## 共享契约或数据库变化

无生产 contract、migration、依赖锁或 ADR 变化。journal/trash schema 仅 Spike-local。

## 测试结果

31 passed, 0 failed, 0 skipped。命令：
`PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s tests/spikes/file-safety -p 'test_*.py' -v`。
另执行三项 validators、`git diff --check`（结果写入 tests.md）。

## 架构测试与质量门禁

执行 handoff、architecture baseline、repository validators；变更仅在允许路径。
`architecture_review` 与 diff 一致：无共享契约、数据库、生产模块或新运行时。

## 文件安全、权限与性能影响

所有 destructive fixture 位于 exact `.runtime/sandbox-storage/M0-006/` 或 owned
`m006-*` temp；symlink/absolute/..、保护/权限/空间/cancel、mutation、corrupt target、
collision、restore/replace/delete、lock concurrency/reclaim 均有测试。16MiB 实际 move：
16,777,216 bytes，1MiB buffer，elapsed 0.538528s，tracemalloc peak 3,164,406 bytes（测试输出直接记录）。

本轮补充：recovery lock 由 operation-bound inspect callback 授权回收；inspect 从已验证
relative path 派生所有 source/target/stage/trash 与 metadata，拒绝 journal 路径篡改、
未知 state、op_id traversal 和 metadata symlink。source/replacement metadata-only 与
physical-trash gap 均验证后恢复，所有 recovery return/exception 由 owner-aware finally
释放锁。

最终 correction 补充同卷 replacement metadata-only subprocess crash/recovery；restore 先
证明 payload 位于 configured task-local trash、逐组件无 symlink，再读取 metadata/hash 并
校验 root_role、operation_id、trash_relative_path；删除了任意递归清理 helper。

本轮最终修正固定目录 fd 上的 rename 后 fsync；取消替换保留旧 target；恢复与 trash
cleanup 均从 configured roots 派生路径；trash metadata 额外校验 role、原始相对路径和
delete reason；长拷贝/hash/trash 使用按 TTL 节流 heartbeat，第二 owner 在超过初始 TTL
后仍被活跃 owner 拒绝；恢复阶段 source/target 在 source-trash 前再次 full-hash。

Linux same-device/cross-device/fsync/noreplace 均 executed；Windows 无 executor，Windows
候选原语与 1/20/100GiB release-size 仍为外部门禁，未宣称通过。

## 技术债、已知问题与风险

- `renameat2` 与目录 fsync 能力必须在 M0-009 CI 每次 probe；不支持时 destructive commit
  fail closed。
- Windows atomic no-overwrite、fsync 与 restore mapping 尚无执行证据。
- 真实 1/20/100GiB 压测未运行；仅逻辑 100GiB boundary。

## 建议合并顺序

先审查 implementation commit，再审查 handoff metadata commit；协调器决定是否合并。

## 下一步

M0-009 冻结平台原语 probe、Windows gate、fsync policy 与 release-size/performance CI。

## Codex 线程链接（可选）

仅作为导航，不是唯一交接依据。
