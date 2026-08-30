# M0-007 测试记录

## 执行环境

Linux worktree，Python 3.12，标准库；输出目录为 `.runtime/sandbox-storage/M0-007/`。

## 执行命令

`PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s tests/spikes/performance -p 'test_*.py' -v`

`PYTHONDONTWRITEBYTECODE=1 python3 tests/spikes/performance/profile_500k.py`

`python3 scripts/validate_handoff.py`; `python3 scripts/validate_architecture_baseline.py`;
`git diff --check`

## 架构与契约测试

## 通过

4/4 unittest；500,000 条 profile；确定性摘要、100k hot directory、100GiB 逻辑值、
故障九类、取消、路径安全、幂等替换均通过。

## 失败 / 跳过

## 故障注入与恢复验证

## 性能数据

墙钟 10.375704 s；峰值 RSS 394,368 KiB；输出 194,334,025 bytes；吞吐 48,189.50
records/s；digest `c7e7e79937fe376a65b37cfd29bfb685ed343650dfe7a72d694f67955601e748`。

## 尚未覆盖

真实文件哈希、产品索引和真实权限/配额未测试（明确非本任务范围）；故障计划只做
数据驱动描述，不破坏系统状态。
