# V01-011 测试记录

## 执行环境

- Windows 独立 worktree：`C:\YOKI\Codex\worktrees\V01-011`
- .NET SDK：10.0.111；Release 配置
- Python：3.12.14，禁用 bytecode 缓存
- PostgreSQL server/client：16.15
- PostgreSQL：测试进程创建的一次性 cluster；角色、数据库和备份夹具均为随机测试资源
- runtime 登录：`assetlibrary_v01003_test_runtime`（LOGIN、NOINHERIT、无管理权限，仅拥有固定 runtime membership）

## 执行命令

```text
dotnet restore AssetLibrary.slnx --locked-mode
dotnet format AssetLibrary.slnx --verify-no-changes --no-restore
dotnet build AssetLibrary.slnx --configuration Release --no-restore
dotnet test AssetLibrary.slnx --configuration Release --no-build --no-restore
python -B database/migrations/production/migration_tool.py validate
python -B -m unittest discover -s tests/database -p test_*.py -v
python -I -B -m unittest discover -s tests/repository -p test_*.py -v
python -I -B -m unittest discover -s tests/architecture -p test_*.py -v
python -I -B -m unittest discover -s tests/release -p test_*.py -v
python -I -B scripts/verify_repository.py
python -I -B scripts/validate_server_release.py
python -I -B scripts/validate_v0_1_alpha.py
python -B tests/architecture/check_release_gates.py --target v0.1-start
python -I -B scripts/validate_v0_1_alpha.py --require-ready
python -B tests/architecture/check_release_gates.py --target v0.1-release
```

本机命令使用任务记录中的固定绝对工具路径；连接串和合成测试 secret 只通过子进程环境传递，没有写入命令、报告或 Git。

## 架构与契约测试

- architecture baseline：通过，扫描 94 个输入；GatewayAuth 没有 Domain 反向依赖、跨模块内部引用或平台依赖。
- .NET source policy：通过，扫描 179 个 C# 文件；无敏感认证值日志或重复实现。
- migration manifest：10 条连续 migration、11 个模块、PostgreSQL 16.15，checksum/owner/文件集精确一致。
- runtime 对 `local_account_credential`、`browser_session` 和 `authenticated_principal` 的直接 SELECT/INSERT/UPDATE 均拒绝；只允许执行 migration 10 明确授权的函数。
- CoreServer readiness 对版本 10 ready，对版本 9 pending、版本 11 extra 和 checksum drift 继续 not-ready。

## 通过

- .NET：210/210；其中 WebGateway 26/26、Packaging 30/30；完整 solution 0 failed / 0 skipped，构建 0 warning / 0 error。
- database：59/59；完整真实 PostgreSQL 套件耗时 52.316 秒。最终扩展后的 300 秒退避封顶用例另做定向复跑，1/1 通过、耗时 4.303 秒；不重复计入唯一总数。
- repository：54/54；architecture：14/14；release：32/32。
- 唯一自动测试合计：369 passed、0 failed、0 skipped。
- `v0.1-start`：`RELEASE_GATE_ALLOWED`。
- 默认 Alpha 审计：退出 0，`ALPHA_INTEGRATION_AUDIT_OK decision=blocked`。
- `--require-ready` 与 `v0.1-release`：均输出预期 blocked，退出码 3。

## 失败 / 跳过

- 最终受影响套件无失败、无跳过。
- 一次过程性全 solution format 在只还原 WebGateway 项目时报告其他项目缺包，同时准确报告新 PostgreSQL facade 超过 CA1506 耦合阈值；随后把凭据与会话适配器拆分、执行完整 locked restore，最终 format/analyzer、build 和 tests 全部通过。
- 完整 locked restore 遇到 NuGet TLS 连接被远端重置并自动重试，命令最终退出 0、全部项目还原成功；没有更改源或锁文件绕过验证。
- release-blocked 命令的退出码 3 是负向验收，不计入失败。

## 故障注入与恢复验证

- 未知账号与错误凭据返回相同拒绝，未知账号执行 600,000 次同算法虚拟派生；禁用或退避账号不签发会话。
- 8 个并发失败记录请求仅 1 个更新成功、7 个观察到现有窗口；重复失败增长至 11 次时下次允许时间保持在 298–301 秒断言窗口内。
- 本地成功登录在同一事务创建会话并清零失败状态；陈旧 credential version 或 principal session version 不创建/不认证会话。
- 数据库只保存 session/CSRF SHA-256 摘要；原始 32-byte token 未出现在存储回读中。
- 错误 CSRF 不能认证变更请求或注销；正确 CSRF 可注销，注销后同 token 立即拒绝。
- 第 21 个会话创建后只保留最新 20 个；最旧会话不可认证。
- PostgreSQL 重启后有效会话仍可认证；空闲/绝对期限到达后失败关闭；主体禁用后本地材料和所有会话不可用。
- 真实 C# `PostgresAuthenticationStore` 完成本地登录、读认证、CSRF 变更认证、注销和注销后拒绝的 Npgsql 往返。
- 完整 database suite 继续覆盖并发迁移、checksum/ledger drift、备份恢复、TaskHealth、10 万目录查询、Host readiness 与权限收敛。
- 最终回读为 0 个命令行关联 V01-011 的 PostgreSQL 进程和 0 个关联监听端口；临时数据库由测试逐一强制回收，空的 `.runtime/sandbox-storage/V01-011` 已按精确路径移除。

## 性能数据

- PBKDF2 固定基线 600,000 次 HMAC-SHA256，盐至少 16 bytes、派生值 32 bytes；本机真实 verifier 单测约 0.26 秒（与并行套件共同执行，仅作回归信号）。
- session/CSRF 各 32 bytes；空闲寿命 30 分钟、绝对寿命 12 小时、每主体最多 20 会话。
- PostgreSQL connection/statement/command 与应用操作的截止时间均为 5 秒；账号、session digest 和主体活跃会话均有有界索引路径。
- 完整数据库回归 59 项耗时 52.316 秒；认证定向五项在修改前一轮耗时 10.867 秒。

## 尚未覆盖

- 未运行 GitHub 托管 Linux CI；只在本机验证工作流定义和 Windows PostgreSQL 16.15。
- 未连接或修改生产数据库，没有创建生产账号，也没有测试真实部署 secret、证书、TLS、持久密钥或账号恢复。
- 未实现 HTTP 登录、Cookie/Origin adapter、外部速率限制、OIDC 协议或认证后的业务 Host 组合。
- 未关闭 `production-authentication`、`production-database-composition`、业务 API、生产写入、V01-008 partial 或任何外部平台 release gate。
