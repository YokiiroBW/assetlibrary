# V01-013 交接摘要

## 完成状态

`ready_for_review`

## 完成内容

- 在 GatewayAuth Infrastructure 内实现 `PwnedPasswordsSecretRiskChecker`，复用 V01-012 的 `ILocalSecretRiskChecker` 与失败关闭生命周期，不新增 Host、路由、账号入口或数据库写入能力。
- 只向固定 `https://api.pwnedpasswords.com/range/{PREFIX}` 发起 GET；`PREFIX` 是完整口令严格 UTF-8/SHA-1 的前 5 个大写十六进制字符，请求固定发送 `Add-Padding: true` 和 `AssetLibrary-GatewayAuth/0.1`，不发送 API key、正文、明文、完整摘要或 35 字符 suffix。
- 只接受 HTTP 200；以有界流读取响应，严格要求 CRLF、35 位大写十六进制 suffix、非负十进制计数、800–1200 行，并拒绝非法 UTF-8、空响应、重复 suffix、声明/实际长度不一致和任何超限数据。
- 命中正计数返回 `Compromised`；零计数 padding 被忽略；合法未命中返回 `Allowed`。非 200、重定向、网络/读取异常、超时或响应不合法统一返回 `Unavailable`；调用方主动取消继续向上传播。
- 新增有界内存 prefix-range LRU cache：默认 64、绝对上限 256 个 prefix，默认有效期 15 分钟、绝对上限 1 小时；使用单调时间判定到期，失败响应不缓存，过期允许结果不会在断网时继续放行。
- SHA-1 仅用于 HIBP k-anonymity 协议兼容，现有 PBKDF2-HMAC-SHA256 凭据策略未改变。口令 UTF-8 字节、完整摘要和查询 suffix 的可清理缓冲在使用后清零。

## 精确网络与资源边界

- origin、scheme 和路径基址不可配置；生产 transport 禁止自动重定向、Cookie 和自动解压。
- 总请求与连接超时均不超过 2 秒；`HttpClient` 自身无限 timeout 仅用于避免双重计时，实际调用由适配器自己的 linked cancellation deadline 约束。
- 默认响应上限 128 KiB，绝对可配置上限 256 KiB；响应 header 上限 16 KiB；每个服务端最多 4 个连接；缓存和解析都与账号数、资产数无关。
- 适配器不记录应用日志、异常正文、指标或持久文件。未来 Host 组合时仍须显式确认出站 HTTP 诊断不会把 range path/prefix 写入日志或高基数指标。

## 架构、复用与兼容

- 依赖方向保持 `Infrastructure -> Application -> Contracts`；具体网络 Provider 没有进入 Domain/Application。
- 复用 `LocalSecret`、`LocalSecretRisk`、`ILocalSecretRiskChecker` 和 `LocalAccountLifecycleService`；没有复制凭据派生、账号写入、权限复验或审计逻辑。
- 仅使用 .NET 10 BCL 的 HTTP、加密、流、并发和 `TimeProvider` 能力；未新增语言、框架、NuGet 包、共享 wire contract、数据库、migration 或角色。
- 未修改 `services/core-server/Host/**`、`apps/**`、`contracts/**`、SDK、solution、依赖锁或任何其他模块内部实现。

## 测试结果

- 365 项唯一自动测试通过，0 失败、0 跳过：244 .NET、21 个 connection-free migration manifest、54 repository、14 architecture、32 release。
- WebGateway 60/60，其中 V01-013 新增 24 项，覆盖 ASCII/Unicode 固定向量、请求隐私、padding、正/零/未命中、所有非 200、重定向、超时、取消、断连、截断、大小和行数边界、非法编码/格式/重复、缓存命中/到期/LRU/并发，以及生命周期允许和拒绝路径。
- locked restore、format verify、完整 solution Release build 均通过；构建为 0 warning / 0 error；repository verifier 和 server release definition validator 退出 0。
- `v0.1-start` 允许；默认 Alpha 审计退出 0 且 decision 保持 blocked；`--require-ready` 与 `v0.1-release` 均按合同返回原生退出码 3。
- 必测 Provider 路径全部使用受控 `HttpMessageHandler` 和内存流；未向 HIBP 或任何真实互联网服务执行 live 查询。
- 核心实现提交：`6078c39f3d76acb790a74b57891adc95ac25c88b`；允许路径生命周期补强提交：`ac85f59162c8d781d26b2747962f2ce16684a971`。
- 交接基线提交：`8bf3a76c1b739a4af11dad225741b39e4b5665ba`。

## 文件安全、权限与运行残留

- 未访问真实资产/NAS，未创建生产账号，也未修改系统账号、UAC、注册表、防火墙、系统服务、PATH、Docker、Codex 配置或生产数据库。
- 测试只使用合成口令、受控内存响应和仓库忽略的构建输出；没有 HIBP live 网络流量、API key、连接串、token 或真实凭据。
- release 进程测试验证了 graceful restart 和零 listener 残留；未启动数据库或留下任务专用服务进程。

## 技术债、已知问题与风险

- Provider 是外部可用性依赖；合法缓存到期后若 HIBP 不可达，账号口令写入会按设计失败关闭，而不会使用 stale allow。
- 本任务没有并入 Host；首位管理员 bootstrap、无人可登录时的带外恢复、部署授权 secret、TLS、持久 Data Protection key、Secure Cookie、Origin/CSRF 和上游滥用控制仍未实现。
- 后续 Host 组合必须为真实出站 HTTP 栈验证 URL/path 遥测脱敏、代理与证书失败、部署网络策略和流量限制；本任务不把受控 transport 测试冒充 live 可达性或端到端生产证据。
- GitHub 托管 Linux CI 未在本任务执行；本机验证环境为 Windows 和 .NET SDK 10.0.111。

## 建议合并顺序

在 V01-012 之后合并，顺序为 13；先合并两个实现/测试提交，再合并本交接提交。不得随本任务把 `production-authentication`、V01-008 partial 或任何 release target 改为通过。

## 下一步

下一项认证工作应实现受控首位管理员 bootstrap 与带外恢复，并保持外部入口关闭；之后再完成 TLS/secret、Cookie/Origin、出站诊断脱敏和上游滥用控制，最后把认证、账号生命周期、风险 Provider 与 PostgreSQL 组合进 Host。完成端到端负向验证前，生产认证、业务 API、生产数据库组合和生产写入继续 blocked。

## Codex 线程链接（可选）

仅作为导航，不是唯一交接依据。
