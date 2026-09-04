# V01-012 交接摘要

## 完成状态

`ready_for_review`

## 完成内容

- 在 GatewayAuth 内实现只接受已认证系统管理员的本地账号预配、精确读取、凭据轮换/恢复、停用与重新启用应用服务。
- 新口令写入前强制经过风险检查端口：只有 `Allowed` 可继续；`Common`/`Compromised` 统一拒绝，`Unavailable`/超时/异常统一失败关闭，所有非允许路径均为零凭据写入。
- 复用 V01-011 的 PBKDF2-HMAC-SHA256 策略，以 600,000 次迭代、每次独立 16-byte CSPRNG 盐和 32-byte 派生值生成凭据；登录和预配共用一套敏感哈希材料校验与内存清零实现。
- 凭据轮换/恢复会增加 credential version 与 principal session version、清除失败退避、撤销目标主体全部会话；恢复可重新启用账号。停用/启用也增加 session version，旧会话不会恢复。
- 新增 append-only migration 11、无 secret 的幂等审计表和四个有界生命周期函数；数据库在每次操作内按 actor principal ID、当前 session version、管理员事实与启用状态再次复验调用者。
- 用全局事务 advisory lock 串行化管理员可用性变更；两个管理员并发停用最多一个成功，最后一个可用管理员保持启用。

## 风险检查状态合同

- `Allowed`：派生新凭据并进入 PostgreSQL 原子写入。
- `Common`、`Compromised`：返回 `SecretRejected`，不派生、不调用 store。
- `Unavailable`、未知状态：返回 `DependencyUnavailable`，不派生、不调用 store。
- 风险源超时或异常：5 秒截止后返回 `DependencyUnavailable`，不调用 store；调用方主动取消仍向上传播。
- 日志只记录固定动作、稳定结果编号和耗时，不记录账号口令、风险查询正文、盐、摘要、token、连接串或异常正文。

## 数据库对象与权限

- 新表：`gateway_auth.local_account_lifecycle_operation`，保存 operation ID、动作、已验证 actor、目标账号、非 secret 请求语义、稳定结果、版本和时间。
- 新函数：`read_local_account_for_administrator`、`provision_local_account`、`replace_local_account_credential`、`set_local_account_enabled`。
- 内部授权 helper：`local_account_actor_is_authorized`；runtime 无执行权。
- `assetlibrary_gateway_auth_runtime` 不能直接读取或写入 lifecycle、principal、credential、session 表，只能执行 migration 11 明确授权的四个函数。
- migration 11 SHA-256：`2a4e6356f4d0d80cb03117ad7aeafd9618dc182909a4a6d62631c188a62fa06c`；manifest 精确推进到 11，migration 1–10、`roles.sql`、`bootstrap.sql` 和 26-role 固定图未修改。

## 幂等、版本与恢复结果

- 同 operation ID、同一非 secret 语义返回首次结果且不重复增版本；同 ID 不同语义返回 `OperationConflict`。
- 账号名或 `local:<account>` subject 冲突返回 `AccountConflict`，不会覆盖显示名、管理员事实或凭据。
- 轮换把测试账号从 credential/session `1/1` 推进到 `2/2`，清零失败计数和 retry 时间并撤销旧 session。
- 启停依次推进 session version；禁用后的凭据恢复以新摘要把账号重新启用并推进到 credential/session `3/6`。
- PostgreSQL 快速重启后，恢复后的启用状态、credential version、session version 和新摘要仍保持；旧 session 仍不可用。
- 审计表不存在名称含 `secret`、`salt`、`digest`、`token` 或 `csrf` 的列，runtime 也不能读取该表。

## 架构、复用与兼容

- 依赖方向保持 `Infrastructure -> Application -> Domain/Contracts`；Domain 不依赖 Application、数据库、ASP.NET、文件系统、网络或操作系统。
- 复用 `AuthenticatedIdentity`、`LocalAccountName`、`LocalSecret`、`LocalSecretHashingPolicy`、现有 PostgreSQL executor、GatewayAuth owner/runtime 和 Npgsql 10.0.3。
- 敏感哈希材料在既有认证与新生命周期之间复用，没有复制校验/清零逻辑；生命周期 PostgreSQL adapter 保持小型并由现有 store facade 组合。
- 没有修改公开 wire contract、生成 SDK、Host、其他模块 schema、统一错误码、语言预算、包或依赖锁。
- 账号和 operation 查询走唯一键/索引，单次为 `O(log n)`；会话撤销只触及目标主体的有界会话，与 50 万资产规模无关。

## 测试结果

- 382 项唯一自动测试通过，0 失败、0 跳过：220 .NET、62 database、54 repository、14 architecture、32 release。
- .NET SDK 10.0.111；Release 构建 0 warning / 0 error；WebGateway 36/36，Packaging 30/30。
- PostgreSQL server/client 均为 16.15；最终完整数据库套件耗时 59.421 秒，强制使用任务专用一次性 cluster。
- 真实 C# → Npgsql → PostgreSQL 链路完成管理员会话认证、账号预配与读取；数据库负向证据覆盖普通账号、伪造 actor、陈旧 session version、禁用管理员和 runtime 直接访问拒绝。
- 最后管理员并发、operation 重放/冲突、账号冲突不覆盖、退避清理、会话撤销、恢复重启持久化和无 secret 审计均通过。
- `v0.1-start` 允许；默认 Alpha 审计退出 0 且 decision 为 blocked；`--require-ready` 与 `v0.1-release` 均按合同返回 3。
- 实现提交：`849a8ab8d7e5d05629ec401db4dc77c142f27e56`；最终恢复/重启测试提交：`250b49a38fc02d1d708eafe16775b0a1ad9e5282`。
- 交接基线提交：在本交接三件套首次完整提交后记录。

## 文件安全与运行残留

- 未读取或写入真实资产/NAS，未修改系统账号、UAC、注册表、防火墙、服务、PATH、Docker、Codex 配置或生产数据库。
- 数据库测试只使用 `.runtime/sandbox-storage/V01-012` 内的随机临时 cluster、合成凭据与备份。
- 最终检查为 0 个命令行关联 V01-012 的 PostgreSQL 进程；测试数据库、备份、cluster、Python cache 和空任务 runtime 均已回收。
- 仅保留 Git 忽略的 `.runtime/dotnet-home` 与 `.runtime/nuget` 构建缓存，其中不含测试连接串或凭据。

## 技术债、已知问题与风险

- 当前没有生产常见/泄露口令风险源实现，因此不存在可启用的生产预配流程；不得用手写 SQL 或绕过 Application service 替代。
- 首位管理员 bootstrap、无人可登录时的带外恢复、部署授权 secret 尚未实现；没有已认证管理员时生命周期函数按设计失败关闭。
- 当前 Host 仍为 HTTP-only 且没有登录或账号管理端点；HTTPS、Secure/HttpOnly/SameSite Cookie、Origin/CSRF、持久密钥和上游滥用控制未完成前不得映射入口。
- 数据库函数的 actor ID/session version 必须来自 V01-011 会话边界；后续 adapter 不得接收客户端自报身份并直接传入数据库。
- 未运行 GitHub 托管 Linux CI；本线程只验证了本机 Windows 与真实 PostgreSQL 16.15。

## 建议合并顺序

在 V01-011 之后合并，本任务顺序为 11；先合并两个实现/测试提交，再合并本交接提交。不要随本任务改变 Alpha capability、V01-008 partial 或任何 release target 状态。

## 下一步

后续认证 Host 任务应先实现受控的首位管理员 bootstrap/带外恢复和真实口令风险源，再补齐 TLS、持久密钥、部署 secret、Secure Cookie、Origin/CSRF 与上游滥用控制，最后才把认证及账号管理映射到 Host。端到端负向验证完成前，生产认证、业务 API、生产数据库组合和生产写入继续保持 blocked。

## Codex 线程链接（可选）

仅作为导航，不是唯一交接依据。
