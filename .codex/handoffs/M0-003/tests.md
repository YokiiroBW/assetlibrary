# M0-003 测试记录

## 执行环境

Linux，Python 3 标准库，AssetLink worktree。

## 执行命令

`PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s tests/spikes/assetlink -p 'test_*.py' -v`

## 架构与契约测试

## 通过

5/5 tests passed。所有 schemas/fixtures 可由 Python `json` 解析，Draft 2020-12 声明和 v1 `$id` 唯一性通过。

## 失败 / 跳过

## 故障注入与恢复验证

固定夹具验证 cursor replay、cursor-expired rebuild 语义、幂等字段、重复分块边界和失败 hash/negative offset。

## 性能数据

## 尚未覆盖

## 完整命令清单

`PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s tests/spikes/assetlink -p 'test_*.py' -v` — 14 passed。
`python3 scripts/verify_repository.py` — passed。
`python3 scripts/validate_handoff.py` — passed。
`python3 scripts/validate_architecture_baseline.py` — passed。
`git diff --check` — passed。JSON/ref/dialect/$id checks are test_01 and test_14;
allowed-path and cache-residue checks passed; no generated cache remains.

真实 HTTP/2、浏览器运行时和跨进程断电注入待后续实现 Spike；本任务明确不实现网络或存储业务。
