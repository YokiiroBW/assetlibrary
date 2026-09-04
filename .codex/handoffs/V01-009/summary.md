# V01-009 交接摘要

## 两个独立结论

- **V01-009 门禁实现状态：`ready_for_review`。** `v01-009/1` 政策、失败关闭验证器、CI 接入、文档和负向测试均已完成。
- **V0.1 Alpha release decision：`blocked`。** 完成本任务只证明发布判定机制可靠，不表示 Alpha 已可发布；V01-008 仍为 `partial`，所有开放 M0 gate 均保持原状。

## 分支与提交

- 分支：`codex/v01-009-alpha-release-integration`
- Worktree：`C:\YOKI\Codex\worktrees\V01-009`
- 任务定义 commit：`2bd4988`
- 实现 commit：`4300c2d4eceb6104d551264c3f249c95c9f9d68e`
- 交接 commit：本交接三件套提交后的分支 tip；不在其自身内容中伪造自引用哈希。

## 已交付

- 新增 `eng/v0.1-alpha-readiness.json`，以 `v01-009/1` 固定 8 个任务输入、20 项必需能力和 6 个 release target。
- 新增标准库验证器 `scripts/validate_v0_1_alpha.py`：默认只读审计；`--require-ready` 在有效但未就绪时返回 3；审计损坏返回 2。
- 验证器交叉读取 task registry、handoff result、V01-008 host policy，并通过有界隔离子进程复用 `check_release_gates.py`，没有复制第二套 gate 允许算法。
- evidence 与可选 output 均拒绝越界、UNC、盘符、父级跳转、控制字符、symlink/reparse、Windows 设备别名、ADS、尾随点/空格；输出仅能原子写入带 owner marker 的 V01-009 沙箱真后代。
- fast-merge 与 `verify_repository.py` 接入默认审计；人工 scheduled release 改用聚合 `--require-ready`，且无吞错或 `continue-on-error`。
- 新增 readiness 文档、13 项 release 验证器测试和 5 项 repository/CI 合同测试。
- 未修改业务实现、共享 wire contract、数据库迁移、根级依赖、版本号或 `tests/architecture/m0-gates.json`。

## 当前能力阻断清单

| 能力 | 状态 / 来源 | owner | 后续任务 |
| --- | --- | --- | --- |
| `read-only-permission-gateway-web` | `component_only` / V01-006 | `web-gateway-owner` | `V01-AUTH-RUNTIME-INTEGRATION` |
| `transfer-operation-sandbox` | `deferred_fail_closed` / V01-007 + M0-006-G2 | `transfer-operation-owner` | `V01-WRITE-DURABILITY-GATE` |
| `single-core-native-packaging` | `component_only` / V01-008 partial | `server-packaging-owner` | `V01-PLATFORM-RELEASE-GATES` |
| `production-authentication` | 缺实现 / V01-006、V01-008 交接 | `gateway-auth-owner` | `V01-AUTH-RUNTIME` |
| `production-database-composition` | 缺实现 / V01-003、V01-008 交接 | `database-migration-owner` | `V01-DATABASE-RUNTIME` |
| `host-business-api` | 缺实现 / host policy 明确为 false | `web-gateway-owner` | `V01-READ-HOST-INTEGRATION` |
| `tls-and-secret-management` | 缺实现 / V01-008 与安全基线 | `server-packaging-owner` | `V01-TLS-SECRETS` |
| `metadata-tags-ratings-colors` | 缺实现 / 需求基线 | `metadata-sidecar-owner` | `V01-METADATA-CORE` |
| `exact-dedup` | 缺实现 / 需求基线 | `search-dedup-owner` | `V01-DEDUP-CORE` |
| `software-backup-restore` | 缺实现 / 备份基线与 V01-003 | `backup-update-owner` | `V01-BACKUP-RESTORE` |
| `production-file-operations` | `deferred_fail_closed` / V01-007 + M0-006-G2 | `operation-trash-owner` | `V01-WRITE-DURABILITY-GATE` |
| `scale-and-fault-release-evidence` | 缺环境 / V01-007 + M0-006-G1 | `test-performance-owner` | `V01-LARGE-FILE-GATE` |
| `windows-service-runtime-evidence` | 缺环境 / V01-008 + M0-004-G1 | `server-packaging-owner` | `V01-WINDOWS-SERVICE-GATE` |
| `linux-systemd-runtime-evidence` | 缺环境 / V01-008 + M0-006-G3 | `server-packaging-owner` | `V01-LINUX-NAMESPACE-GATE` |
| `docker-runtime-evidence` | 缺环境 / V01-008 + M0-004-G2 | `server-packaging-owner` | `V01-DOCKER-GATE` |

另有独立任务输入阻断：V01-008 的 registry 与 handoff 状态均必须继续为 `partial` / `adjudication_only`，不能由本任务提升。

## Release target 判定

| target | 当前结果 | 开放 gate |
| --- | --- | --- |
| `v0.1-release` | blocked | `M0-004-G2`、`M0-006-G1`、`M0-006-G2` |
| `windows-server-release` | blocked | `M0-004-G1` |
| `linux-server-release` | blocked | `M0-006-G3` |
| `docker-release` | blocked | `M0-004-G2` |
| `large-file-release` | blocked | `M0-006-G1` |
| `production-file-writes` | blocked | `M0-006-G2` |

`v0.1-start` 仍返回 0，只授权既定范围内继续开发；它不覆盖上述 release target。

## 验证摘要

- 唯一成功计数：365 passed、0 failed、1 skipped（外部 PostgreSQL integration 未配置）。
- .NET solution：185；repository：54；release Python：32；database 本机可执行：39；architecture：14；SDK Python：14；AssetLink：21；Chromium：6。
- 额外不重复计数：TypeScript SDK 5/5、Kotlin/JVM `build` + `sdkTest`、Web format/lint/typecheck/build、NuGet 与 pnpm vulnerability audit、全部依赖/许可证/体积/源码门禁通过。
- `--require-ready=3`、`v0.1-release=3` 均经精确断言；`v0.1-start=0`。
- 首次 release suite 因未先构建 host 而失败，按真实 CI 顺序 restore/build 后完整 32/32 通过；首次 NuGet query 与一次 pnpm audit 遇到临时网络错误，重试后通过。

## 未执行环境与安全影响

- PostgreSQL integration 因未配置 `ASSETLIBRARY_TEST_POSTGRES_BIN` 跳过；CI 中的 PostgreSQL 16.15 专用 job 仍是权威环境。
- `profile_500k.py` 依赖 Unix-only `resource`，Windows 本机不能执行；其 scheduled runner 固定为 Ubuntu。生成器 suite 的 symlink 用例也因当前非提权 Windows token 缺少创建 symlink 权限而不能完成，另外 5 项通过。
- 未运行 Windows SCM、Docker daemon 或 Linux systemd/root 周期；未创建服务、容器、unit、HKLM、监听、防火墙规则或真实资产/NAS 写入。
- 构建与报告只写入 Git 忽略的 build 目录和 `.runtime/sandbox-storage/V01-009/**`。为复现 CI，Corepack 在用户缓存下载了锁定的 pnpm 11.19.0；Gradle 9.3.1 仅下载到任务沙箱；没有持久修改 PATH、JAVA_HOME、UAC 或账户。

## 建议合并与后续

- 建议合并顺序：`8`。实现可以合入 `main`，但主协调线程应保持项目状态为“V0.1 release blocked”。
- 后续按 readiness 文档顺序拆分生产认证/数据库/只读 host、TLS/secrets、元数据/查重/备份、生产写耐久、大文件、Windows/Linux/Docker 平台门禁任务。
- 只有全部必需能力为 `passed`、V01-008 等输入正式完成、六个 release target 均允许并经独立审查后，才可把 Alpha 称为 release candidate。
