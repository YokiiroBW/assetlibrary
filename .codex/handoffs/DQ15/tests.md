# DQ15 验证

- `python -I -B scripts/verify_repository.py`：通过，包括现有交接、架构、Alpha、迁移与架构用例入口。
- `python -I -B -m unittest discover -s tests/repository -p test_*.py -v`：95 项，94 通过，1 失败。失败为 `test_production_modules_define_ports_without_physical_adapters` 缺少预期的 Infrastructure 目录，与本次工作流变更无关。
- 在未修改的原始 d7b43ec 检出单独执行 `test_transfer_operation_foundation.py`：同一项同一断言复现，另外 4 项通过。未扩展修复历史目录问题。
- `git diff --check`：通过。未执行远端 GitHub Actions，未测量节省时长。

运行日志：本检出 `.runtime/dq15-verification.log`、`.runtime/dq15-repository.log`；本轮完成状态不宣称全仓全绿。
