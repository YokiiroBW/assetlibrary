# M0-006 Windows 外部门禁交接摘要

## 完成状态

`partial`

Windows implementation/evidence commit：
`c14cfc1cd9217c8a1e7c610af77fd66ad4f0ab5c`；本摘要、`result.json` 与 `tests.md`
随第二个 metadata commit 提交。既有 Linux implementation
`7caf70c4a6d9af3e95018e56714ae99330314705` 的 40-test 证据保持不变。

## Windows 执行环境与边界

- Windows 11 x64 build `26100`，bundled Python `3.12.13`。
- 任务 worktree、所有小型 fixture 均在本机固定 NTFS；初始基线
  `0faa07cc2518952f78d2fb029e777974c89df74c`，分支
  `codex/m0-006-windows-evidence`。
- 证据时间 `2026-08-31T02:27:30Z`。未记录卷标、卷序列号、用户名或本机绝对路径。
- destructive fixture 只在 ignored task root
  `.runtime/sandbox-storage/M0-006/windows-probe-*`；未访问 NAS、真实资产、用户文件、
  系统级设置或全局权限。

## 已执行并通过的 Windows candidate

- `SetFileInformationByHandle(FileRenameInfo)` 的单次 no-replace 调用；
  `ReplaceIfExists=FALSE`，调用前没有 target existence pre-check。预置 target 时返回
  `ERROR_ALREADY_EXISTS (183)`，source/target 完整 SHA-256 与 bytes 均保持不变。
- 50 轮、100 个独立 contender 进程：50 success、50 collision、0 overwrite，
  每轮恰好一个 winner，loser source 保留。
- 102,404 bytes 经 `CreateFileW(FILE_FLAG_WRITE_THROUGH)` + `WriteFile` +
  `FlushFileBuffers(file)` + close，随后新进程 reopen/full SHA-256；digest：
  `375b3dc0cc7f3190dc408652de29648432c1f2ac50ad7cf6d0886070ce0ee0fd`。
- 本机 directory handle 以 `GENERIC_WRITE | FILE_FLAG_BACKUP_SEMANTICS |
  FILE_FLAG_WRITE_THROUGH` 打开后，`FlushFileBuffers(directory)` 返回 success；这里只记录
  API 执行事实，不把它提升为断电时 namespace ordering 的保证。
- 两个真实子进程分别在 `after_stage_flush`、`after_target_rename` 调用 `os._exit(77)`；
  每个边界后由两个全新进程恢复，4/4 recovery 均重新检查 stage/target、reopen/full
  SHA-256 后 `complete`，第二次恢复幂等。payload digest：
  `d1062ac277eca76bb0d41ab94c3ad3efab510c6db4a57bfd680558b37b1c3dfe`、
  `ce04b5ba2d47a9ae25e096d469f43a7d802b2073b36e7e0f334a5ba337c7442b`。
- recovery 遇到不同 hash 的既有 target 时返回 `conflict`，stage 与 target 均保留。

Windows target discovery：`6 passed / 0 failed / 1 skipped`。唯一 skip 是原有
Linux-only `fcntl`/`renameat2` module；它在 Windows 明确跳过，没有把 Linux case 伪装成
Windows 通过。详细命令与结果见 `tests.md`。

## 执行事实与官方语义保证的区分

Microsoft
[`FILE_RENAME_INFO`](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_rename_info)
明确说明 `ReplaceIfExists=FALSE` 且 target 已存在时返回错误；documented no-overwrite、
本机碰撞和并发 candidate 均已通过。

Microsoft
[`FlushFileBuffers`](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-flushfilebuffers)
与
[`CreateFile`](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilew)
支持本次 file/write-through/flush mapping。当前证据证明 API 返回成功、process crash 后
可恢复、target 可独立重开并强哈希一致；它不证明任意突然断电、控制器 cache、NTFS
recovery 下 rename 的完整原子性或 directory-entry 持久排序。没有执行强制掉电试验，
因此 formal power-loss atomicity/durability 仍为 blocker。

## 未完成门禁与所需用户输入

- 1/20/100 GiB 没有运行，也没有使用 sparse/logical file 冒充实际 bytes。
- 运行前需要用户明确给出空的、任务独占、本机固定 NTFS sandbox。必须拒绝 NAS、真实
  资产根、源码目录、非空目录与 reparse/symlink/junction；逐档验证 canonical boundary、
  free space、实际 bytes、流式 SHA-256、清理后再进入下一档。
- 同卷同时保留 source 与 staged/target 时建议最大档至少 `2 × fixture + 10%`，即
  100 GiB 档约 220 GiB 可用空间。若验证真实跨卷，则需两个用户明确指定的空固定 NTFS
  sandbox，volume identity 不同，source/target 各至少 `fixture + 10%`。
- Linux 非协作 namespace mutation 的 exclusive-lock/ACL/kernel policy 仍需 M0-009 冻结。

因此任务必须保持 `partial`，不能启动 M0-009 的完成宣称。

## 修改、架构与复用

新增 Windows-only test probe 与 unittest，Windows 上显式跳过 Linux-only module，并更新
Spike/交接证据。没有修改生产目录、公共接口、共享契约、migration、ADR、任务包、项目
状态、Vault 或 M0-009；没有新增语言、框架、依赖或二进制。

Windows test probe 复用既有 M0-006 的 physical-facts-first、strong-hash、conflict、
idempotent recovery 语义。由于现有 adapter 在 import 时绑定 Linux `fcntl`/`renameat2`，
Windows API mapping 保持独立 test-only；没有抽成第二套生产文件系统层。

## 清理与建议

所有 100 contender、2 crash child、4 recovery child 均已退出；每个
`windows-probe-*` fixture 在 test teardown 删除。最终 task runtime 递归检查为 0 个文件/
子目录；未提交原始日志、fixture、注册表导出、二进制或私密路径。

建议 M0-009 冻结 Windows no-replace capability probe、文件/namespace flush policy、
正式 power-loss fault-lab gate 与 1/20/100 GiB 执行分层。在这些 blocker 关闭前不得把本次
candidate 写成跨 Windows/NTFS 的完整持久性保证。
