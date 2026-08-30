# M0-006 测试记录

## 执行环境

Linux x86-64，Python 3.12 stdlib；NAS `st_dev=147`，`/tmp` `st_dev=2050`。

## 执行命令

`PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s tests/spikes/file-safety -p 'test_*.py' -v`

`python3 scripts/validate_handoff.py`

`python3 scripts/validate_architecture_baseline.py`

`python3 scripts/verify_repository.py`

`git diff --check`

## 架构与契约测试

handoff validator、architecture baseline validator、repository validator、diff check：pass。
无 contract/migration 变更。

## 通过

21 passed / 0 failed / 0 skipped。包含 cross-device durable/physical crash boundaries
（含 metadata-only source/replacement、physical trash gap）、1 个 same-device subprocess
commit gap、replacement recovery、lock O_EXCL concurrency/expiry、16MiB multi-chunk
bounded move、trash restore/delete、symlink/path tamper、unknown-state 和 operation-id
拒绝。

## 失败 / 跳过

无测试失败或跳过。Windows executor 缺失和 1/20/100GiB release-size 是明确外部 blockers，
不是测试 skip。

## 故障注入与恢复验证

每个 crash child returncode=77；每点由两个新 supervisor recovery 进程执行并最终 complete，
source absent，deterministic source trash+metadata valid，stage/lock absent。target hash
full reopen 校验；冲突/损坏路径保留 source 并返回 conflict/manual。

## 性能数据

最大实际 fixture 16,777,216 bytes；chunk 1,048,576 bytes；独立测量 elapsed 0.310058s，
`tracemalloc` peak 3,171,064 bytes。逻辑 `BYTES_100_GIB=107374182400`，未物化大文件。

## 尚未覆盖

Windows atomic/flush 执行证据、真实 1/20/100GiB release-size gate、M0-009 CI freeze。
