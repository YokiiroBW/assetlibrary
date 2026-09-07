# V01-016 交接摘要

## 状态

ready_for_review，2026-09-07。分支 codex/v01-016-secure-browser-authentication，基线77540cb。实现605d80e，延迟DI接线a7566e4，源码与测试09e090e，最终测试准备去重复用f5e0eba。V01-015继续负责完整试用包和真实PG/浏览器验收，不由本任务宣布整个里程碑或发布完成。

## 完成内容

- GatewayAuthenticationComposition.Create组合既有LocalAuthenticationService、BrowserSessionService、PwnedPasswordsSecretRiskChecker、AdministratorBootstrapRecoveryService及PostgresAuthenticationStore，公开GatewayAuthenticationRuntime。
- 256-bit随机部署授权key经传入的持久Data Protection provider加密，本机私密目录、owner/DACL、非reparse/非网络存储预检；独占锁与临时文件原子发布。初始化不覆盖已有key，轮换不偷偷初始化缺失key。
- 65-byte HMAC-SHA256 proof绑定部署、key ID、authorization ID、动作、operation、账号和UTC有效期，最长15分钟；每次读当前key，轮换后拒绝旧proof。原数据库审计和消费规则继续防重放。
- 自动恢复在真实授权与风险门禁之后调用SQL14版本准备，再复用原CAS恢复函数；停用管理员和同operation原expected-version重放得以保留。无owner手写SQL或伪造管理员actor。
- /assetlink/v1/auth/login、/session、/logout实现冻结的snake_case合同。单个__Host-AssetLibrary-Session携DP保护的session/CSRF/期限；Secure、HttpOnly、SameSite=Strict、Path=/，无Domain，JSON不返回原始session。
- /assetlink/v1请求统一检查HTTPS/Host/Origin/Fetch Metadata，除登录外每次验数据库会话，POST额外固定时核对CSRF。CurrentAdministratorPolicy是唯一管理员政策；控制错误复用AssetLink envelope。
- 登录20次/分钟、并发2、排队0，先于解析/口令派生；请求截止5秒，登录JSON上限8KiB、operator stdin上限16KiB。503保留Cookie，失效会话401清理Cookie；注销失败不返回204。
- 本机operator支持initialize-key、rotate-key、bootstrap、recover；密码只经有界stdin，无匿名HTTP初始化/恢复。已有账号离线登录/浏览不调用HIBP；新口令仍在风险源不可用时闭锁。

## 精确Host接线

根Host注册受保护IDataProtectionProvider、GatewayAuth最小权限NpgsqlDataSource和runtime工厂，然后调用TrialAuthentication.Configure(builder.Services, publicOrigin)。容器解析时才构造，不提前BuildServiceProvider；原四参数Configure仍可用。

TrialAuthentication.Use(app)必须位于端点执行之前，TrialAuthentication.Map(app)映射三个认证入口。管理控制调用TrialAuthentication.TryGetCurrentAdministrator(context,out identity)；该身份仅来自本请求数据库验证，不能从Web角色字段制造。

本机调用TrialAdministratorOperator.ExecuteAsync(action,runtime,inputStream,cancellationToken)，读取ExitCode/Json。bootstrap stdin字段：authorization_id、operation_id、account_name、display_name、expires_at、password；recover去掉display_name。重试保留相同ID和期限。配置AuthorizationKeyFile的父目录须由根部署工具创建为私密目录。

## 架构与复用

GatewayAuth仍唯一拥有身份、口令、会话、管理员状态机和授权。Domain不变；Application仅依赖端口，Infrastructure拥有Npgsql、文件、HMAC和DP。已有诊断类继续承担耗时记录，原复杂度/耦合门禁未抑制或放宽。没有新依赖、语言、框架、数据库、角色或跨模块表访问；没有手改生成SDK。

没有修改SQL、manifest、其他模块或根Host默认行为。SQL14由V01-017独占。新增公开facade/operator合同已由协调器批准，HTTP实现消费contracts/assetlink/read-only-trial-v1.md。既有健康检查模式不会自行开放认证入口。

## 验证

Release构建0警告/0错误，format verify通过。WebGateway runner81/81、Packaging runner46/46；其中2个既有PG条件方法未配置连接而return，明确不作真实数据库证据。排除这2项后125个.NET实际执行，加manifest21、architecture14、AssetLink21，去重181项通过；新增24项全部执行。详见tests.md。

Windows真实TLS发现Schannel不支持ephemeral私钥。测试改用UserKeySet，不含PersistKeySet/Exportable，并确定Dispose；协调器批准根Host同样处理。12项TLS批次前后当前用户Crypto文件3→3，新增/减少0。没有安装证书、修改系统信任、注册表、SCM或防火墙。

## 安全、性能和剩余边界

只操作自身授权state或隔离测试文件，不读写原资产、NAS或资产权限。测试叶目录清理完成，临时V01-016父目录子项0；ignored构建和TRX缓存保留。key/proof/Cookie/request固定有界；全局登录限流不创建无限账号/IP分区；数据库按主体、摘要、operation索引，与50万资产数无关。

V01-017已报告PG16.15的READ ONLY prepare、disabled管理员、首次CAS、同op重放、过期/未知/冲突/错误runtime拒权通过；完整真实Postgres适配器+HTTPS+Web仍由V01-015验收。

根Host负责DP显式加密、TLS验证与证书轮换。更换TLS私钥证书时保留旧DP解密证书或明确重包key，不可静默丢弃旧PFX。Windows异常进程终止可能留下当前用户私钥容器，故障检查和自有容器回收归V01-015；正常退出证据不替代崩溃清理保证。离线首次配置需要未来可信离线风险源，不可跳过现有门禁。

建议顺序：协调器合同/ADR → V01-017 SQL14 → 本任务 → 根Host/扫描/Web组合与E2E。完整Alpha、生产资产写、SCM/systemd/Docker和Explorer门禁保持原状。
