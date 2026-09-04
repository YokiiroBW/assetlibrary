# V01-014 交接摘要

## 完成状态

`ready_for_review`

## 完成内容

- 在 GatewayAuth 内新增 component-only 的首位本地系统管理员引导与既有本地系统管理员带外恢复核心，不新增 HTTP、CLI、Host 路由或真实部署授权 secret。
- 新增不可省略的 `IOutOfBandAuthorizationVerifier`：授权结果必须精确绑定 action、调用方 operation ID、目标账号和到期时间；拒绝、过期、不可用、未知状态、超时、异常和绑定漂移均在口令风险检查、凭据派生和数据库调用前失败关闭。
- 复用 V01-012 的 Unicode/长度策略、`ILocalSecretRiskChecker`、PBKDF2-HMAC-SHA256 凭据派生和可清理 secret material；只有 `Allowed` 才允许派生与写入。
- 新增 migration 12 和 PostgreSQL store。数据库在全局事务 advisory lock 内再次检查授权有效期及事实约束，并持久化不含 secret 的 authorization/operation 防重放结果。
- bootstrap 仅在数据库从未出现本地系统管理员时创建一个启用管理员；账号、subject、principal ID 冲突不覆盖，管理员停用后也不能重新 bootstrap。
- recovery 仅针对已经存在且仍为本地系统管理员的目标；成功时替换凭据、重新启用账号、清除失败退避、同时推进 credential/session version，并撤销全部旧 browser session。

## 关键决策

- verified authorization 只携带随机 authorization ID、绑定字段和 expiry，不携带原始 proof；原始 proof 由拥有者控制生命周期并在释放时清零，字符串表示固定为 `[redacted]`。
- 相同 authorization ID 与 operation ID、相同语义返回持久幂等结果；任一 ID 与不同 operation/action/target/expiry/display/version 组合碰撞都返回 `request_conflict`。
- 状态冲突也被记录并稳定重放，避免同一授权反复探测账号或管理员事实；未知账号、普通账号、外部 OIDC 主体和陈旧版本统一返回无账号详情的 `state_conflict`。
- 服务总 deadline 最大 5 秒；调用方取消继续传播，内部依赖超时/异常映射为不泄露正文的 `DependencyUnavailable`。日志只含固定 action、数值 outcome 和耗时。

## 修改文件

- GatewayAuth Contracts/Application/Infrastructure：授权合同、服务、日志、PostgreSQL adapter、参数与账号状态读取复用，以及现有 facade 接线。
- PostgreSQL：`0012_gateway_auth_admin_bootstrap_recovery.sql` 与 migration manifest。
- 测试：GatewayAuth .NET 合同/服务/真实 adapter、PostgreSQL 权限/并发/重启/审计、迁移版本和 Host readiness 断言。
- 文档：V0.1 Alpha readiness、V01-014 任务状态和本交接三件套。

## 模块边界、依赖方向与复用

- 依赖保持 `Infrastructure -> Application -> Contracts`；Application 不依赖 Npgsql、ASP.NET、文件系统、网络或具体 secret Provider。
- 复用 `LocalSecret`、`LocalSecretRisk`、`ILocalSecretRiskChecker`、`ILocalCredentialDeriver`、`LocalCredentialEnrollmentMaterial`、`LocalAccountState`、`PostgresAuthenticationExecutor` 和现有 GatewayAuth schema/角色。
- 抽取的 PostgreSQL 参数与账号状态 reader 只消除同一模块内重复；未跨模块访问表、内部实体或可变状态。

## 新语言、框架或重大依赖

- 无。继续使用仓库现有 C#/.NET 10、Npgsql、PostgreSQL 16 SQL 和 Python 测试工具；未修改 NuGet 锁或引入服务。

## 共享契约或数据库变化

- 新增内部 `AdministratorBootstrapRecoveryAction/Outcome/Result`、带外 proof 和验证/store 端口；没有公开 wire API、SDK 或 Host 合同变化。
- migration 12 新增 module-owned `gateway_auth.administrator_bootstrap_recovery_operation`、内部 request-match helper，以及 runtime 仅可执行的 bootstrap/recovery 两个 `SECURITY DEFINER` 函数。
- runtime 对新表和 helper 无读写/执行权限；函数固定 `search_path`、严格校验 PBKDF2 参数，并在事务锁后重新读取时钟与安全事实。

## 测试结果

- 424 项唯一自动测试通过，0 失败、0 跳过：257 .NET、67 database、54 repository、14 architecture、32 release。
- WebGateway 73/73，其中 V01-014 新增 13 项；真实 PostgreSQL 16.15 覆盖首次创建、并发唯一成功、双 ID 防重放、冲突不覆盖、恢复版本/退避/会话、重启持久化、过期授权零写入和最小权限。
- locked restore、format verify、完整 solution Release build 均通过；构建 0 warning / 0 error；repository verifier、server-release validator 和默认 Alpha audit 均退出 0。
- `v0.1-start` 允许；`--require-ready` 与 `v0.1-release` 仍按合同返回原生退出码 3。
- 实现提交：`ee8c0305bd0611d8698fcab9602964158d4d5d81`。
- 交接基线提交：由包含本文件的后续提交固定。

## 架构测试与质量门禁

- architecture 14/14，repository 54/54，release 32/32；repository verifier 扫描 206 个 C# 文件并验证 migration 12 / PostgreSQL 16.15 manifest。
- Alpha audit 继续报告 `production-authentication`、Host、TLS/secret、生产数据库组合、生产写入和各平台 release target 为 blocked；没有扩大组件证据。

## 文件安全、权限与性能影响

- 未访问真实资产/NAS，未连接生产数据库，未创建系统/生产账号，也未修改 UAC、注册表、防火墙、服务、PATH、Docker 或 Codex 配置。
- 所有数据库写测试都在任务专属 PostgreSQL 临时实例中运行；实例、listener 和 .NET build server 已停止，未留下任务进程。
- 数据库 mutation 使用固定单次事务、全局 advisory lock、主键/唯一键和目标时间索引；复杂度与 50 万资产规模无关，不读取资产文件。
- 为避免工作区扫描压力，约 2.0 GB 的任务下载工具已移出 worktree，现位于系统临时目录 `V01-014-tooling-and-tests` 与 `V01-014-pgAdmin4`；安全策略拒绝递归删除，因此需由操作者后续删除。主工作区另有一个 0 文件的空 `V01-014` 测试目录，无运行影响。

## 技术债、已知问题与风险

- 本任务故意没有真实授权 verifier/secret 生命周期；当前核心不能作为可部署的账号恢复入口，也没有进入 Host。
- 后续必须完成 TLS、持久 Data Protection key、HTTPS Cookie/Origin/CSRF、操作者身份与上游 abuse control，再进行端到端负向验证。
- migration 使用全局串行锁是低频紧急管理操作的安全取舍；若未来暴露批量管理能力，应保持其与普通账号生命周期分离。
- GitHub 托管 Linux CI 未在本任务执行；本机证据为 Windows、.NET SDK 10.0.111 和 PostgreSQL 16.15。

## 建议合并顺序

在 V01-011、V01-012、V01-013 之后合并，顺序为 14；先实现提交，再交接提交。不得随本任务把 `production-authentication`、V01-008 partial、生产写或任何 release target 改为通过。

## 下一步

下一项认证任务应实现可轮换、可审计且失败关闭的真实带外授权适配器与部署 secret 生命周期，然后在 TLS 和持久 key 就绪后，将 V01-011 至 V01-014 与 PostgreSQL runtime、认证后的只读业务 Host 组合并执行端到端负向验证。

## Codex 线程链接（可选）

仅作为导航，不是唯一交接依据。
