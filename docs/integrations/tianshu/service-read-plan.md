# TS062：只读服务接入的最小实现包

2026-09-14；方案待协调裁决。产品基线 `555f337271c3b8690d96a4a5a72d796d1c0a7536`。本轮不改实现，服务身份能力仍 unavailable。

## 已有事实与缺口

`Host/Trial/Authentication/TrialAuthenticationMiddleware.cs` 只接受浏览器 ticket，POST 先要求 Origin/CSRF；不能把 Bearer 请求直接送入便称服务接入。`Adapters/AssetLink/AssetLinkReadRequestParser.cs` 从认证 principal 的唯一 sub/NameIdentifier 取主体，绝不从 body 取授权主体。

调用链为 `ReadOnlyAssetLinkProtocol -> ReadOnlyBrowseService -> IAuthorizedReadModelQuery -> PostgresAuthorizedReadModelQuery`。现有五个只读操作为 `libraries.list`、`libraries.get`、`entries.get`、`entries.browse`、`assets.search`。不另建平台查询器或数据库连接。

`database/migrations/production/0009_gateway_auth_read_api.sql` 的 authenticated_principal 已有 subject_key、disabled_at 和管理员标记；0007 的 library_permission 主键为 library_id/principal_id，支持 read_only。0021 的查询继续基于当前主体和权限投影过滤。`PostgresReadExecutor` 每请求建只读 RepeatableRead 事务并切换最小数据库角色；不是把登录时权限缓存到 token。撤权事务提交后**新查询事务**不得再读该库；已在途快照可能完成，不能许诺瞬时召回已发送字节。

0007 明确库权限 runtime 只有 SELECT；`LibraryStorage/Application` 目前没有赋权/撤权用例。0009 runtime 也不能任意写 principal。因此仅增加 GatewayAuth Application/AssetLink 不足以交付可管理服务身份。需要下述 CCP，不能在网关跨模块 INSERT 或使用管理员数据库连接作为捷径。

## 推荐身份与最小权限

AssetLibrary 本地受信操作者通过新增受控管理命令创建独立 service principal（非管理员，不创建 local login）。服务 token 是一次显示的高熵随机 opaque secret，存储只保留摘要、credential id、principal id、创建/到期/撤销时间。复用 AuthenticationSecretDigest、时间/取消模式与现有 PostgreSQL；不复用 browser_session 表、用户密码、平台 origin、对话 audience，也不引入 JWT 自包含权限快照或第二数据库。

每个平台资产接入配置绑定一个独立 principal 和明确库清单。LibraryStorage 所有者的受控端口仅赋 read_only/撤销；库清单是现有 library_permission 的记录，不能再维护一份 token ACL。不同用户或隐私边界使用不同 principal/凭据；平台没有实际用户认证与绑定时不开放 UI，禁止以共享服务身份聚合全部个人库。

每个请求在线验证 token 未到期/撤销、principal 未禁用且非管理员，再进入现有只读用例，SQL 再核验库权限。认证存储不可用即 503，不使用旧成功快照继续发请求。旋转创建新 credential，显式撤销旧 credential；禁用主体停用所有凭据，重启不能复活撤销。管理过程需最少审计：操作者、目标主体/库、动作、时间、关联 ID，不记 secret。

仅五个 read operation 可到达此认证分支；不得给服务 principal 赋 browser identity ticket、管理/扫描/预览/上传/文件操作权限。授权库无权限与不存在均按现有 404，list/search 过滤，避免存在性泄露。

## 请求与消费者边界

推荐保留 `/assetlink/v1/control` 及五个既有 body/result，由独立服务认证分支处理单个 Authorization Bearer。必须拒绝混合 Cookie/Bearer、多个 Authorization、Origin/Sec-Fetch 浏览器请求；旧浏览器路径保留原信任检查，不全局移除 CSRF。服务分支先强制 HTTPS、配置的 authority 和路径，再验证 token，之后显式 allowlist read operation。该分支要在浏览器 trust 拒绝之前分流，且不得被写操作调度器复用。具体合同见 CCP。

分页复用默认50/最大100、cursor 最大8192、查询100ms–10s（默认5s），body 64KiB。现有 Host 普通请求整体5s，不能宣传客户端 timeout_ms=10000 等于端到端10s。建议服务分支也整体5s，消费者单次总预算5s并保留取消。保护游标绑定 scope/filter/sort，不是主体授权票据，也没有跨页一致快照保证；切账号、库、路径、搜索或排序必须清 cursor，按 entry_id 去重。每页重新授权，拒绝后不得保留可继续访问的旧页。首版不给游标增加主体字段，避免无必要迁移/SDK改动。

消费者限制响应体建议1MiB（候选消费预算，不冒认 Core 已限制），流式计数，超限中止并呈现明确不可用，不截断后宣称成功。服务端已有行数上限，路径等长字段可能超过此预算，双方联合测试需验证并批准预算。无文件内容/缩略图下载；名称和路径不得冒认为内容哈希。

错误保留 HTTP 状态及 code；合法且已解析的控制请求关联 request_id。当前认证/运输/解析前失败可能是 `unknown`，消费者保留自己的本地调用 ID并记录上游 ID，不伪造回显。401清凭据状态与资产快照，404清目标快照并重新发现，400不自动重试；503/504/网络断开标不可用。首版离线只显示连接状态/最后观测时间，不展示旧资产正文或列表，从而不把旧快照当新授权。

重连只在用户重试或有限退避（建议最多2次，累计15s）后重新认证并从第一页发现库；取消、账号/范围改变递增本地请求代次，丢弃迟到结果。401、404、无效 cursor 不循环重试，不退回管理员/浏览器 Session。释放流、连接、取消源及旧页缓存。

## 后续源码范围（本轮均未授权）

路径相对于 AssetLibrary；新文件名为提案，可由各单一负责人在审批时固定。

| 所有者 | 最小文件/入口 | 原因 |
| --- | --- | --- |
| GatewayAuth | `Modules/GatewayAuth/Contracts/ServiceReadAuthenticationContracts.cs`、`Application/ServiceReadAuthenticationService.cs`、`Application/ServiceReadAuthenticationPorts.cs`、`Infrastructure/PostgresServiceReadCredentialStore.cs`（均位于 services/core-server） | 签发/验证/撤销、摘要存储；复用现有主体和权限查询 |
| LibraryStorage | `services/core-server/Modules/LibraryStorage/Application/ServiceLibraryReadGrantService.cs` 与 `Contracts/ServiceLibraryReadGrantContracts.cs`、`Infrastructure/PostgresServiceLibraryReadGrantStore.cs` | 单一所有者管理既有库权限，禁止 GatewayAuth 写表 |
| Host | `services/core-server/Host/Trial/Authentication/TrialAuthenticationMiddleware.cs`；新增 `ServiceReadAuthenticationHandler.cs` 与受控本地管理命令；现有 composition/CLI 的具体挂载文件在授权时按调用者确认 | HTTPS只读分流、依赖注入、操作者入口；不能只授权 Application 后暗改 Host |
| AssetLink | `contracts/assetlink/read-only-trial-v1.md`（认证附录），必要的请求认证测试 | 不变控制消息 schema/body，生成 SDK 不应变化；新管理命令不是跨产品 HTTP |
| 迁移负责人 | `database/migrations/production/` 新增 GatewayAuth 凭据迁移、LibraryStorage 权限管理函数迁移及 manifest | 新 credential 持久状态与模块所有权函数；编号由集成人分配，不覆写0007/0009/0021 |
| 验证负责人 | `tests/dotnet/AssetLibrary.WebGateway.Tests/` 新服务认证/HTTP/撤权测试；`tests/database/`；`tests/integration/tianshu/` 联合驱动 | 有效负例、重启与清理；必要 host fixture 变更另列范围 |

Host调用者已定位：`services/core-server/Host/Trial/TrialHostFactory.cs` 注册只读服务，`Trial/Authentication/TrialAuthentication.cs` 装配认证，`Trial/TrialWebEndpoints.cs` 将 control 交给 `Trial/TrialManagementGateway.cs`（其中还有扫描和管理操作）。服务请求必须在 `TrialWebEndpoints.cs` 显式分流到只读协议，不依赖管理网关“恰好拒绝”。本地操作者入口为 `Hosting/CoreServerHost.cs -> Trial/TrialHost.cs -> Trial/Authentication/TrialAdministratorOperator.cs`；后续新增 `Trial/Authentication/ServiceReadOperator.cs`，通过 TrialHost 分派，保留旧 bootstrap/recover。配置入口 `Trial/TrialConfiguration.cs` 及其实际解析文件需随默认关闭开关一并授权。这些均属于 Host 唯一负责人的审批范围，不能由 Application 工作者顺手修改。

ReadOnlyBrowseService、IAuthorizedReadModelQuery、查询 DTO、SQL只读投影、SDK 的业务逻辑预计无需改动。无锁/新依赖、第二数据库或文件迁移。**本轮零迁移；推荐后续实现需要新增迁移**，不能写“整个接入无需迁移”。管理命令跨模块组合依赖公开用例，不跨表事务；部分失败保留无权限主体，重试幂等按主体/库键，成功回执前重新核验授权，失败不泄漏 token。操作者认证复用现有受保护本地证明模式，但其 action 枚举/签名载荷目前仅 bootstrap/recovery，新增服务管理 action 必须作为 GatewayAuth 内部合同扩展获批，不把旧恢复证明用于新动作。

## Platform a94d345 的公开约束

只读 `projects/tianshu-platform` 固定提交 `a94d345` 的 `docs/module-integration.md`、`docs/platform/runtime.md`，没有读写其数据库或未提交代码。前者要求同源聚合、合同/实例/双方验收、取消和过期响应保护，目前资产页不可用；后者只公开 origins/resolve 与 model-config/snapshot，dialogue 授权域不是资产库授权，来源引用不能赋库权限。

平台后续需另获范围：服务端 AssetLink 客户端、专用凭据存储引用、用户到接入绑定、同源授权聚合与页面消费。不在浏览器持有服务 token，不猜 I01–I18 URL，不把 a94d345 来源同步能力当资产适配已完成。正式跨产品合同从协调根 contracts 发布；本目录仅内部 CCP，未经 schema/实例/双方验收不得冻结。

## 双方验收清单（未来实现门禁，非本轮通过）

真实平台通过 HTTPS 连接自有临时 Core/PG 与合成两库：独立凭据只见A、另一凭据只见B；五操作/中文/100+余数分页；跨库/路径/排序旧 cursor；伪主体/混合认证/缺失/到期/撤销token；库撤权后下一页和get拒绝、list/search无泄露；禁用主体/轮换/重启不复活；授权库源离线与网络离线分别呈现；错误关联（含 unknown）；64KiB/1MiB边界、查询/网络超时、取消/迟到、断网重连。源离线的索引结果即使可读也须显示 availability，不能声称原件可用。

保存各场景请求类型、状态和脱敏关联证据；记录真实实现/替身边界，最后核验原件 hash/mtime、临时数据库和六角色、Host/PG/监听/私密凭据清理。规模只验证有界分页，50万查询计划/P95单独门禁。不得触碰真实 NAS，B查重/分类、C整理/删除/发布仍未实现。
