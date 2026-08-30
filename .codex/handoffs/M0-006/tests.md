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

40 passed / 0 failed / 0 skipped。包含 cross-device durable/physical crash boundaries
（含 metadata-only source/replacement、physical trash gap）、same-device subprocess commit
与 replacement gap、lock O_EXCL concurrency/expiry、16MiB multi-chunk
bounded move、trash restore/delete、symlink/path tamper、unknown-state 和 operation-id
拒绝，以及 configured-trash containment/metadata consistency。

## 失败 / 跳过

无测试失败或跳过。Windows executor 缺失和 1/20/100GiB release-size 是明确外部 blockers，
不是测试 skip。

## 故障注入与恢复验证

每个 crash child returncode=77；每点由两个新 supervisor recovery 进程执行并最终 complete，
source absent，deterministic source trash+metadata valid，stage/lock absent。target hash
full reopen 校验；冲突/损坏路径保留 source 并返回 conflict/manual。

新增回归：cancelled replace 保留旧 target 且 recovery 不移动；journal stage 被篡改为
外部路径时 cleanup 不删除外部文件；recovery source 在 target commit 后被修改时不入
trash；source physical-trash 完成但 journal 更新前 target 被修改时进入 conflict；慢拷贝在
超过初始 lease TTL 后由第二 owner 尝试 recovery 仍被 unexpired lease 拒绝。

本轮还覆盖 pinned 祖先目录换链、replace 预检出现/identity 变化、trash 后 target 变化、
recovery 同步变化、非法 replacement metadata 零物理写入，以及旧 owner 对新 generation
的 heartbeat/release 交错保护；同 owner 第二实例在 expiry 前所有 claim/heartbeat/release/
recover 均失败且锁字节不变，expiry 后经 inspect/reconcile 才轮换 generation/token。

`_rename_noreplace` 的 pinned dirfd post-syscall containment 回归证明祖先交换后的
external relocated tree 不产生 payload，源文件 bytes 恢复、configured target 不存在；
这是 fail-closed reverse noreplace rollback 证据，不宣称 `renameat2` 具备原子 beneath-root
条件。Linux 非协作 namespace mutation policy 仍 blocked_by M0-009。

## 性能数据

最大实际 fixture 16,777,216 bytes；chunk 1,048,576 bytes；独立测量 elapsed 0.585626s，
`tracemalloc` peak 3,181,357 bytes（测试输出直接记录）。逻辑 `BYTES_100_GIB=107374182400`，未物化大文件。

## 尚未覆盖

Windows atomic/flush 执行证据、真实 1/20/100GiB release-size gate、M0-009 CI freeze（含
Linux 非协作 namespace mutation policy）。
