# M0-006 Windows 测试记录

## 执行环境

- Windows 11 x64 build `26100`
- bundled Python `3.12.13`
- fixed local NTFS task worktree/sandbox（绝对路径未记录）
- base commit `0faa07cc2518952f78d2fb029e777974c89df74c`
- evidence timestamp `2026-08-31T02:27:30Z`

## 目标测试

PowerShell：

```powershell
$env:PYTHONDONTWRITEBYTECODE = '1'
<bundled-python-3.12> -m unittest discover -s tests/spikes/file-safety -p 'test_*.py' -v
```

最终结果：`6 passed / 0 failed / 1 skipped`，elapsed `3.929s`。

唯一 skip：`test_file_safety` 是既有 Linux-only `fcntl` +
`renameat2(RENAME_NOREPLACE)` adapter；Windows 明确跳过。其既有 Linux 证据仍为
`40 passed / 0 failed / 0 skipped`，本轮没有在 Windows 重跑或伪装这些平台 case。

## Windows candidate 结果

| 证据 | 结果 |
| --- | --- |
| fixed local NTFS + sandbox boundary | pass |
| single `SetFileInformationByHandle(FileRenameInfo)` no-replace collision | pass；Win32 `183` |
| two-process contention | pass；50 rounds / 100 contenders / 50 success / 50 collision / 0 overwrite |
| file write-through + `FlushFileBuffers(file)` | pass；102,404 bytes |
| new-process reopen/full SHA-256 | pass；`375b3dc0cc7f3190dc408652de29648432c1f2ac50ad7cf6d0886070ce0ee0fd` |
| `FlushFileBuffers(directory)` executed call | returned success；formal rename power-loss ordering not inferred |
| subprocess crash after stage flush | exit 77；two new recovery processes complete/idempotent |
| subprocess crash after physical target rename | exit 77；two new recovery processes complete/idempotent |
| different-hash recovery collision | conflict；stage + target preserved |

Crash fixture digest：

- `after_stage_flush`：`d1062ac277eca76bb0d41ab94c3ad3efab510c6db4a57bfd680558b37b1c3dfe`
- `after_target_rename`：`ce04b5ba2d47a9ae25e096d469f43a7d802b2073b36e7e0f334a5ba337c7442b`

## Repository / architecture 门禁

```powershell
<bundled-python-3.12> scripts/validate_handoff.py
<bundled-python-3.12> scripts/validate_architecture_baseline.py
<bundled-python-3.12> scripts/verify_repository.py
git diff --check
```

- handoff validation：pass
- architecture baseline：pass
- repository verification：pass
- `git diff --check`：pass
- changed-path audit：仅 M0-006 允许目录
- contract/migration/production/public API changes：none

## 未执行与 blocker

- 1/20/100 GiB：not run；缺少用户明确指定的空 fixed-NTFS sandbox 与对应容量确认。
- sudden power loss/controller-cache/NTFS replay：not run；process crash 不能替代 power loss。
- directory-handle flush 返回成功是 executed fact，不构成公开 formal durability guarantee。
- Linux 非协作 namespace mutation policy：仍由 M0-009 冻结。

## 清理证明

每个 fixture root 由 `tearDown` 在已验证的 task sandbox 内删除；所有 contender/crash/
recovery subprocess 均已 wait/退出。最终 `.runtime/sandbox-storage/M0-006/` 递归检查为
`0` 个文件或子目录；无 `__pycache__`、大文件、原始日志或存活 probe child。
