# V01-011 交接摘要

## 完成状态

`ready_for_review`

## 完成内容

- 在 GatewayAuth 内实现本地账号规范化、PBKDF2-HMAC-SHA256 固定时校验、未知账号同成本虚拟派生和统一拒绝结果。
- 将失败次数与下次允许时间持久化；原子退避从 1 秒指数增长、300 秒封顶，不产生永久锁死。
- 用独立 256-bit CSPRNG 值签发 session 与 CSRF token，数据库只保存 SHA-256 摘要；支持读会话、带 CSRF 的变更会话、立即注销、30 分钟空闲/12 小时绝对寿命和每主体 20 会话上限。
- 会话同时绑定主体 session version 和本地 credential version；主体禁用、改密版本变化、过期或撤销都会失败关闭。
- 将“已验证主身份”与“会话签发”拆开，本地账号与后续 OIDC 适配器复用同一 session issuer；本任务没有实现或假装实现 OIDC 协议。
- 新增 append-only 生产 migration 10 与最小权限函数，并把真实 C# 存储往返接入既有 PostgreSQL 16.15 CI 作业。

## 关键决策

- 本地登录名固定为 3–64 个小写 ASCII 字符，可用字母、数字、点、下划线和连字符；显示名继续来自 `authenticated_principal`。
- 本地凭据派生使用每账号盐、PBKDF2-HMAC-SHA256、至少 600,000 次迭代和 32-byte 派生值；新口令长度政策为 15–128 个 Unicode scalar，不要求字符组合。
- 本任务不提供账号预配或改密入口，因此常见/泄露口令筛查不能被绕过；后续 owner-only 入口必须先实现该门禁。
- runtime 只获得五个固定 `SECURITY DEFINER` 函数的执行权，不能直接读取凭据或会话表，也不能切换到 owner。
- 所有 PostgreSQL 操作均在显式事务内使用固定 runtime role、5 秒 statement/command 截止时间；会话验证会原子刷新空闲期限。
- 当前 HTTP-only Host 不得映射登录端点。未来入口必须先具备 HTTPS、`__Host-`/Secure/HttpOnly/SameSite Cookie、Origin/CSRF 校验、持久密钥和上游滥用控制。

## 修改文件

- 认证合同、领域和应用服务：`services/core-server/Modules/GatewayAuth/{Contracts,Domain,Application}/**`
- PostgreSQL 适配器：`services/core-server/Modules/GatewayAuth/Infrastructure/Postgres*Authentication*`、`PostgresBrowserSessionStore.cs`、`PostgresLocalCredentialStore.cs`
- 数据库：`database/migrations/production/0010_gateway_auth_local_sessions.sql`、`manifest.json`
- 测试与 CI：`tests/database/**`、`tests/dotnet/AssetLibrary.WebGateway.Tests/**`、`tests/dotnet/AssetLibrary.Packaging.Tests/DatabaseReadinessTests.cs`、`.github/workflows/handoff-quality.yml`
- 任务与说明：`.codex/tasks/V01-011.md`、`docs/releases/V0.1_ALPHA_READINESS.md`、`.codex/handoffs/V01-011/**`

## 模块边界、依赖方向与复用

- 依赖保持 `Infrastructure -> Application -> Domain`，合同只承载稳定值对象；Domain/Application 不依赖数据库、ASP.NET、文件系统、网络或操作系统。
- 凭据端口与会话端口分别由小型 PostgreSQL 适配器实现，公共 facade 只负责组合，避免 God adapter 和反向依赖。
- 复用现有 `AuthenticatedSubject`、`authenticated_principal`、GatewayAuth owner/runtime、迁移/备份机制和锁定的 Npgsql 10.0.3。
- 未访问或写入 LibraryStorage、AssetIdentity 等其他模块内部表；没有新增共享可变状态、服务、语言、框架或数据库。

## 新语言、框架或重大依赖

无。仅使用仓库已有 C#/.NET、Python 测试工具、Npgsql 10.0.3、PostgreSQL 16.15 和 .NET 加密标准库。

## 共享契约或数据库变化

- 新增 GatewayAuth 内部本地认证与不透明浏览器会话端口，不修改 `contracts/**` 或生成 SDK。
- 新增 migration 10：`local_account_credential`、`browser_session`、主体 `session_version`、索引和五个最小权限函数。
- manifest/latest readiness 从精确版本 9 推进到 10；pending、extra 和 checksum drift 继续失败关闭。
- migration 1–9、`roles.sql`、`bootstrap.sql` 与固定角色图未修改。

## 测试结果

- 369 项唯一自动测试通过，0 失败、0 跳过：210 .NET、59 database、54 repository、14 architecture、32 release。
- PostgreSQL server/client 均为 16.15；完整数据库套件在一次性本机 cluster 中执行，强制 `ASSETLIBRARY_TEST_POSTGRES_REQUIRED=1`。
- 真实 C#→Npgsql→PostgreSQL 链路完成本地登录、session/CSRF 校验和注销；错误 CSRF、撤销后访问、旧凭据版本与禁用主体均拒绝。
- 并发错误尝试只有一个原子更新成功；指数退避在 300 秒封顶；会话在数据库重启后仍有效，并按空闲/绝对期限失败关闭。
- `v0.1-start` 继续允许；Alpha 默认审计有效但 decision 为 blocked；`--require-ready` 与 `v0.1-release` 均按规范返回 3。
- 实现提交：`5d8d47c36f061fcd03c704ed5cdb36950d6ce723`。

## 架构测试与质量门禁

- locked restore、format/analyzer、Release build（0 warning / 0 error）、完整 solution 全部通过。
- repository verifier、architecture baseline、.NET sensitive-log/source policy、migration manifest、Alpha audit 与 release suites 通过。
- CI 只扩展既有 PostgreSQL job，没有新增或弱化 tier；Linux 托管 CI 尚未在本线程远程执行，不将工作流定义冒充远端证据。
- `production-authentication`、HTTP 业务 API、TLS/secret、生产文件写和外部平台 release 状态均未被修改或关闭。

## 文件安全、权限与性能影响

- 没有读写真实资产/NAS，也没有修改系统账号、UAC、注册表、防火墙、服务、PATH、Codex 配置或生产数据库。
- 测试凭据为随机临时数据库中的合成值；报告和 Git 不包含明文生产 secret、连接串或原始 token。
- runtime 不能直接读取表；会话/CSRF 原始值只在调用方内存中短暂存在，可释放缓冲区会清零，数据库仅保存摘要。
- 登录/会话查找走唯一账号名或 32-byte 摘要索引，单次为 `O(log n)`；每主体清理被 20 条上限约束，与 50 万资产规模无关。
- 最终检查未发现 V01-011 PostgreSQL 子进程或监听端口；数据库、备份和 cluster 夹具均已回收。
- `.runtime/sandbox-storage/V01-011` 已按校验后的绝对路径非递归移除；仅保留 Git 忽略的 `.runtime/dotnet-home` 与 `.runtime/nuget` 构建缓存，其中不含测试连接串或凭据。

## 技术债、已知问题与风险

- 尚无生产账号预配、改密/恢复与常见/泄露口令筛查入口；不得通过手写 SQL 作为产品流程替代。
- 尚无 HTTPS 登录 endpoint、Cookie/Origin adapter、持久 Data Protection key、证书和部署 secret 生命周期。
- 未知账号虽执行同成本派生，但外部入口仍必须配置全局/来源级请求与并发限制，防止 CPU 滥用。
- OIDC 只有会话签发边界，尚未实现 discovery、state/nonce、PKCE、callback 或 provider 配置。

## 建议合并顺序

在 V01-010 之后合并，本任务顺序为 10；先合并实现提交，再合并本交接提交。不要随本任务修改 Alpha capability 或 release target 状态。

## 下一步

由后续认证 Host 任务实现受控账号预配/恢复、HTTPS Cookie/Origin 和滥用控制，再与 V01-010 数据库 readiness、V01-006 权限过滤只读用例组合。端到端安全负向测试完成前，生产认证、业务 API、TLS 与生产写入继续保持 blocked。

## Codex 线程链接（可选）

仅作为导航，不是唯一交接依据。
