# M0-006 文件安全 Spike

这是 test-only Python 3.12 标准库实现，不是生产文件操作库。物理文件是权威，journal
只用于解释检查点；恢复每次重新读取 source/stage/target/trash 并全量 SHA-256 验证。

## 已执行证据

- Linux same-device：`renameat2(RENAME_NOREPLACE)` 直接 source→target，目标碰撞不会覆盖；
  commit gap 通过真实 `os._exit(77)` 子进程、两个新 recovery 进程验证。
- Linux cross-device：NAS `st_dev=147` → `/tmp` `st_dev=2050`，target-root
  `.m006-stage/<operation>/payload` bounded copy、文件/目录 fsync、noreplace commit、
  reopen/full hash、source-root `.m006-trash`；source trash 与 replacement trash 均有
  deterministic role path 和先行 durable metadata。
- Durable/physical crash hooks：`after_preflight_journal`、`after_stage_physical`、
  `after_staged_journal`、`after_verified_journal`、`after_target_physical_commit`、
  `after_target_committed_journal`、`after_source_physical_trash`、
  `after_source_trashed_journal`、`after_complete_journal`，每点均要求首次子进程
  returncode 77，随后两次新进程 recovery 最终 `complete`、source absent、trash/meta
  valid、stage/lock absent。
- source-trash 的 metadata-only、`before_source_physical_trash` 与 post-rename gap 均有
  恢复矩阵；pinned dirfd 在 syscall 前/后做 containment probe，越界时使用 pinned reverse
  noreplace rollback，fail closed。`renameat2` 本身没有原子 beneath-root 条件；非协作
  namespace mutation 的 exclusive-lock/ACL/kernel policy 仍由 M0-009 冻结。
- 16 MiB 实际 bounded move：16,777,216 bytes，1 MiB buffer，实测 elapsed 0.585626s，
  `tracemalloc` peak 3,181,357 bytes；未物化 1/20/100 GiB，仅导入 M0-007 的
  `BYTES_100_GIB == 100 * 1024**3` 做 64-bit 逻辑断言。

## 平台矩阵与 M0-009 门禁

| 平台/保证 | 结果 |
| --- | --- |
| Linux x86-64 same-device noreplace | executed/pass |
| Linux x86-64 cross-device stage/recovery | executed/pass |
| Linux file fsync + directory fsync | executed/pass (`os.fsync`, `O_DIRECTORY`) |
| Windows atomic no-overwrite/fsync mapping | external gate; no Windows executor |

M0-009 应在 CI 冻结 Linux `renameat2` capability probe、directory-fsync policy、Windows
`MoveFileEx`/底层 no-overwrite 等价性证据，并保留跨设备 1/20/100 GiB release-size gate
为未执行的外部门禁。
