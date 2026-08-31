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
| Windows 11 x64 fixed NTFS no-replace candidate | executed/pass for documented no-replace and observed contention; formal crash/power-loss atomicity remains blocked |
| Windows file flush + independent reopen/hash | executed/pass (`FILE_FLAG_WRITE_THROUGH`, `FlushFileBuffers(file)`) |
| Windows directory-handle flush call | executed/returned success; rename power-loss ordering is not established by this observation |

M0-009 应在 CI 冻结 Linux `renameat2` capability probe、directory-fsync policy、Windows
no-replace capability probe 与 flush policy，并保留跨设备 1/20/100 GiB release-size gate
为未执行的外部门禁。

## Windows 11 x64 外部门禁证据（2026-08-31）

执行环境为 Windows 11 x64 build `26100`、bundled Python `3.12.13`、本机固定 NTFS；
证据时间 `2026-08-31T02:27:30Z`。探针只在 task-owned
`.runtime/sandbox-storage/M0-006/windows-probe-*` 目录创建小型 fixture，测试结束后删除；
没有读取 NAS、真实资产或用户文件。实现为 `windows_file_safety_probe.py`，仅供测试，
不导入 Linux `fcntl`/`renameat2` adapter，也不形成生产接口。

### 执行事实

- payload commit 只调用一次
  `SetFileInformationByHandle(FileRenameInfo)`，结构体的 `ReplaceIfExists=FALSE`；调用前
  不检查 target。预先存在 target 时返回 Win32 `ERROR_ALREADY_EXISTS (183)`，source 与
  target bytes/SHA-256 均保持不变。
- 50 轮、100 个独立 contender 进程竞争同一 target：50 success、50 collision、0 overwrite；
  每轮恰好一个 source 成为 target，失败 contender 的 source 保留。
- 102,404-byte fixture 使用 `CreateFileW(FILE_FLAG_WRITE_THROUGH)`、`WriteFile`、
  `FlushFileBuffers(file)`、close；新进程重新打开并全量 SHA-256。结果为
  `375b3dc0cc7f3190dc408652de29648432c1f2ac50ad7cf6d0886070ce0ee0fd`。
- 目录使用 `CreateFileW(GENERIC_WRITE, FILE_FLAG_BACKUP_SEMANTICS |
  FILE_FLAG_WRITE_THROUGH)` 打开，本机 `FlushFileBuffers(directory)` 返回 success。该返回值
  仅记录为执行事实，不能单独证明 rename directory entry 的断电持久顺序。
- 两个真实 crash child 分别在 `after_stage_flush` 与 `after_target_rename` 使用
  `os._exit(77)` 终止；每个边界随后由两个全新 Python 进程恢复。4/4 recovery 均通过
  物理 stage/target 检查、target reopen/full SHA-256 后得到 `complete`，第二次恢复幂等。
  两个 payload SHA-256 分别为
  `d1062ac277eca76bb0d41ab94c3ad3efab510c6db4a57bfd680558b37b1c3dfe`、
  `ce04b5ba2d47a9ae25e096d469f43a7d802b2073b36e7e0f334a5ba337c7442b`。
- recovery 遇到已存在且不同 hash 的 target 时返回 conflict，stage 与 target 均保留，
  不执行 check-then-rename 或覆盖 fallback。

目标测试命令（Windows PowerShell 环境变量等价于 Linux 命令前缀）：

```powershell
$env:PYTHONDONTWRITEBYTECODE = '1'
<bundled-python-3.12> -m unittest discover -s tests/spikes/file-safety -p 'test_*.py' -v
```

结果：Windows candidate `6 passed / 0 failed`；既有 Linux-only module 在 Windows 明确
`1 skipped`，不会把未执行的 Linux `renameat2`/cross-device case 伪装成 Windows 通过。

### 官方语义与仍未证明的边界

- Microsoft 的
  [`FILE_RENAME_INFO`](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_rename_info)
  文档明确说明 `ReplaceIfExists=FALSE` 且 target 已存在时操作返回错误；因此 documented
  no-overwrite 与本机碰撞/并发行为已执行通过。
- [`FlushFileBuffers`](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-flushfilebuffers)
  文档说明它把指定 file handle 的 buffered data 写入设备；
  [`CreateFile`](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilew)
  文档说明 write-through 与 metadata flush 的候选映射。本次只证明 API 返回成功、进程
  crash 后可重新打开并得到强哈希一致内容。
- 上述公开文档没有在本 Spike 中建立“`SetFileInformationByHandle` rename 在任意突然断电、
  控制器缓存与 NTFS recovery 情况下均具备完整原子性/持久排序”的可引用保证；本次也没有
  做强制掉电实验。因此 formal power-loss atomicity、directory-entry durable ordering 和
  硬件 cache 行为继续是 blocker，不能由 50 轮 contention 或 directory flush success 推断。

### 未运行的大文件门禁

未运行 1/20/100 GiB，也未创建稀疏文件来冒充实际 bytes。运行前仍需用户明确指定一个
空的、任务独占、本机固定 NTFS sandbox；必须拒绝 NAS、真实资产根、项目源码目录、
reparse/symlink/junction 与非空目录，并在每档前验证 canonical boundary 和空间、逐档执行、
强哈希后清理。若同一卷同时保留 source 与 staged/target，建议按最大档至少预留
`2 × fixture + 10%`（100 GiB 档约 220 GiB）；若要验证真实跨卷路径，则需两个用户明确
指定的空固定 NTFS sandbox，分别预留 `fixture + 10%`，且 volume identity 必须不同。
