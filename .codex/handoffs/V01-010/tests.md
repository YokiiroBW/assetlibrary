# V01-010 测试记录

## 执行环境

- Windows 本机隔离 worktree：`C:\YOKI\Codex\worktrees\V01-010`
- .NET SDK：10.0.111
- Python：3.13.11
- PostgreSQL server/client：16.15
- PostgreSQL：测试进程临时 cluster，`ASSETLIBRARY_TEST_POSTGRES_REQUIRED=1`，所有数据库与备份夹具均为随机测试资源
- Host 数据库身份：`assetlibrary_v01003_test_auditor`（LOGIN、NOINHERIT、无管理权限、拥有 `assetlibrary_database_auditor` membership）
- 构建配置：Release

## 执行命令

```text
dotnet restore AssetLibrary.slnx --locked-mode
dotnet format AssetLibrary.slnx --verify-no-changes --no-restore
dotnet build AssetLibrary.slnx --configuration Release --no-restore
dotnet test AssetLibrary.slnx --configuration Release --no-build --no-restore
python -I -B -m unittest discover -s tests/database -p test_*.py -q
python -I -B -m unittest discover -s tests/repository -p test_*.py -q
python -I -B -m unittest discover -s tests/architecture -p test_*.py -q
python -I -B -m unittest discover -s tests/release -p test_*.py -q
python -I -B scripts/verify_repository.py
python -I -B scripts/validate_server_release.py
python -I -B scripts/validate_v0_1_alpha.py
python -I -B tests/architecture/check_release_gates.py --target v0.1-start
python -I -B scripts/validate_v0_1_alpha.py --require-ready
python -I -B tests/architecture/check_release_gates.py --target v0.1-release
```

本机命令使用任务记录中的固定绝对工具路径；未把连接串或测试密码写入命令、报告或 Git。

## 架构与契约测试

- `verify_repository.py`：通过；handoff、14 architecture、migration manifest、SDK/Web/.NET source policy 与 Alpha 默认审计有效。
- architecture：14/14 通过。
- repository：54/54 通过。
- release：32/32 通过；其中负向 Git 对象测试会按设计输出一次 `fatal: not a tree object`，套件结论仍为 OK。
- server release definition：`status=passed`，合同仍为 `v01-008/1`，所有开放平台 gate 状态不变。
- migration manifest：9 条 migration、11 个模块、PostgreSQL 16.15，验证通过。
- Host-only 精确 payload 由既有 release process smoke 回归覆盖；数据库模式的新增字段不出现在未配置模式。

## 通过

- .NET：197/197；Packaging 30/30，完整 solution 0 failed / 0 skipped。
- database：54/54；完整真实 PostgreSQL 套件 43.333 秒，0 failed / 0 skipped。
- repository：54/54。
- architecture：14/14。
- release：32/32。
- 唯一测试合计：351 passed、0 failed、0 skipped。
- `v0.1-start`：`RELEASE_GATE_ALLOWED`。
- 默认 Alpha 审计：返回 0，`ALPHA_INTEGRATION_AUDIT_OK decision=blocked`。
- `--require-ready` 与 `v0.1-release`：输出预期 blocked；release 测试在直接子进程中验证规范退出码 3（当前执行器把顶层非零状态归一显示为 1）。

## 失败 / 跳过

- 最终受影响套件无失败、无跳过。
- 一次过程性定向命令使用 `python -I -m unittest tests.database...`，因 `tests` 不是 Python package 而在收集前退出；改用测试文件入口后同一用例通过，未掩盖测试失败。
- 较早一次 release 套件调用误选系统内不完整的 `dotnet` 安装；固定 `ASSETLIBRARY_TEST_DOTNET` 为 10.0.111 后 32/32 通过。依赖或源码没有因此修改。
- release-blocked 命令的非零状态是负向验收，不计入失败。

## 故障注入与恢复验证

- 完整 PostgreSQL 16.15 + 精确九条 ledger：启动 preflight 通过，`/readyz` 200，`database_schema_version=9`；同进程 `/healthz` 200/`ok`。
- 篡改第 9 条 ledger name：live `/readyz` 转为 503/`database_not_ready`；恢复后回到 200。
- `ACCESS EXCLUSIVE` 锁住 ledger：请求在 4–8 秒断言窗口内（配置上限 5 秒）返回 503；释放锁后回到 200。
- 强制删除测试数据库：运行中 `/readyz` 转为 503，不泄露连接事实。
- 无 auditor membership 的 runtime 登录和拥有 superuser 权限的登录：启动前退出码 69，稳定码 `database_permission_denied`，未开始监听。
- 完整 database suite 继续覆盖 pending/extra/checksum drift、并发迁移、备份/恢复、重启、权限收敛和 100,000 条规模查询证据。
- 测试结束后回读进程列表与 task runtime：无 PostgreSQL/CoreServer 子进程，空的 evidence 目录已按明确绝对路径非递归移除。

## 性能数据

- readiness 固定读取 1 条 bootstrap 和最多 1001 条 ledger；当前 9 条，时间/内存复杂度 `O(migrations)` / `O(migrations)`，与资产数无关。
- connection timeout、command timeout 与绝对取消边界均为 5 秒；pool max 4、min 0，禁用 multiplexing、error detail、enlist 与 no-reset-on-close。
- 完整数据库回归 54 项耗时 43.333 秒；单个包含启动/live/漂移/锁超时/断库/权限边界的 Host 用例约 20.5 秒。

## 尚未覆盖

- 未运行 GitHub 托管 Linux CI；只验证了工作流定义及本机 Windows PostgreSQL 16.15。
- 未连接或修改任何生产数据库，也未验证生产凭据轮换、证书、TLS、Data Protection key 或 secret store。
- 未实现认证、业务 API、数据库业务查询、迁移 apply 编排、登录角色预配或生产文件写入。
- 未关闭 `production-database-composition`、V01-008 partial、外部平台或 V0.1 release gate。
