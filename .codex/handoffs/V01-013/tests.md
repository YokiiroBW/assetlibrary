# V01-013 测试记录

## 执行环境

- Windows 独立 worktree：`C:\YOKI\Codex\worktrees\V01-013`
- 分支：`codex/v01-013-secret-risk-provider`
- .NET SDK：10.0.111；Release 配置
- Python：3.12.14，使用 `-I -B` 或 `-B` 禁用 bytecode 缓存
- Provider：仅受控 `HttpMessageHandler`/内存流；未执行 live HIBP 查询

## 执行命令

```text
dotnet restore AssetLibrary.slnx --locked-mode
dotnet format AssetLibrary.slnx --verify-no-changes --no-restore
dotnet build AssetLibrary.slnx --configuration Release --no-restore
dotnet test tests/dotnet/AssetLibrary.WebGateway.Tests/AssetLibrary.WebGateway.Tests.csproj --configuration Release --no-build
dotnet test AssetLibrary.slnx --configuration Release --no-build --no-restore
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

本机命令使用任务记录中的固定绝对 .NET/Python 路径；没有向命令行、文件、日志或 Git 写入真实 secret、token、连接串或资产路径。

## 架构与契约测试

- architecture baseline：通过，扫描 107 个输入；GatewayAuth 的网络实现只位于 Infrastructure，Domain/Application 无 HTTP、文件系统、数据库或操作系统反向依赖。
- .NET source policy：通过，扫描 197 个 C# 文件；未发现敏感日志或重复受管代码块。
- migration manifest 保持 11 条 migration、11 个模块、PostgreSQL 16.15，数据库文件、角色和 checksum 均未修改；connection-free manifest 21/21。
- Host、公开契约、生成 SDK、依赖锁和 release ledger 未改变；Alpha audit 仍能精确区分审计成功与发布阻断。

## 通过

- .NET：244/244；WebGateway 60/60，其中 V01-013 新增 24 项；完整 solution 0 failed / 0 skipped，Release 构建 0 warning / 0 error。
- connection-free migration manifest：21/21；repository：54/54；architecture：14/14；release：32/32。
- 唯一自动测试合计：365 passed、0 failed、0 skipped。
- locked restore、format、repository verifier、server release definition validator、默认 Alpha integration audit 均退出 0。
- `v0.1-start`：`RELEASE_GATE_ALLOWED`。
- `--require-ready` 与 `v0.1-release`：均输出预期 blocked，原生退出码 3；这两项是负向验收，不计为失败。

## 协议、隐私与失败注入

- 固定 ASCII 向量 `password` 与 Unicode 向量验证严格 UTF-8、40 字符大写 SHA-1 拆分为 5 字符 prefix 和 35 字符 suffix。
- request snapshot 只见固定 HTTPS range URI 的 5 字符 prefix、`Add-Padding: true` 和固定 User-Agent；没有正文、明文、完整摘要、suffix 或 API key。
- HTTP 正数命中、零计数 padding 和合法未命中分别验证 `Compromised`、忽略和 `Allowed`。
- 400、403、404、429、500、503、302、传输异常和读取异常全部稳定映射为 `Unavailable`，且重定向只发生一次请求。
- 空响应、声明超限、流式超限、长度不一致、799/1201 行、非法 UTF-8、LF-only、缺失终止 CRLF、小写/短 suffix、非法分隔符、空/负/带符号/空白/溢出 count 与相同或冲突重复 suffix 全部失败关闭。
- Provider deadline 取消 transport 并返回 `Unavailable`；调用方主动取消传播 `TaskCanceledException`，没有被伪装为依赖不可用。

## 缓存与生命周期验证

- 相同 prefix 的新鲜 range 只联网一次；同 prefix 的另一口令复用完整公共 range，而不是复用首个口令的允许/拒绝结果。
- 失败不缓存；到期后必须重新联网；即使 UTC 墙钟倒退，单调时间到期仍生效，远端失败时不会回退到过期允许结果。
- LRU 容量淘汰和 24 个并发 prefix churn 均保持结果一致、容量有界；disposed checker 拒绝继续调用。
- 使用真实 Provider adapter 接入 V01-012 lifecycle：`Allowed` 恰好调用一次派生和 store；`Compromised` 与 `Unavailable` 均为零派生、零 store 写入。

## 性能与资源上限

- 单次网络请求总超时和 connect timeout 最大 2 秒；响应默认 128 KiB、绝对最大 256 KiB，行数固定在 800–1200；header 最大 16 KiB，每服务端最多 4 个连接。
- cache 默认 64、最大 256 个 prefix；默认 TTL 15 分钟、最大 1 小时；读取/解析为 `O(response bytes)`，cache 操作受固定容量约束。
- 复杂度与账号总数及 50 万资产规模无关；测试未把整个真实文件、资产或无界网络内容读入内存。

## 清理与残留

- 未启动 PostgreSQL、Docker、Windows 服务或真实 Provider 连接。
- release process tests 验证退出后零 listener 残留；任务只留下 Git 忽略的标准 `bin/obj` 和共享 .NET/NuGet 构建缓存。
- 没有 Python `__pycache__`、测试 secret、网络响应、凭据或 task-owned 服务进程需要回收。

## 尚未覆盖

- 未执行真实 HIBP 可达性、代理、DNS、证书链或服务端行为测试；按任务合同，这些不能成为 required CI。
- 未运行 GitHub 托管 Linux CI。
- 未实现 Host DI/路由、首位管理员 bootstrap/带外恢复、TLS、Cookie/Origin、持久 key、部署 secret、出站遥测脱敏或上游滥用控制。
- 未连接生产数据库、未创建生产账号、未访问真实资产/NAS，也未解除任何认证、数据库、业务 API、生产写入或 release target 门禁。

## 提交

- 核心实现：`6078c39f3d76acb790a74b57891adc95ac25c88b`
- 允许路径生命周期测试补强：`ac85f59162c8d781d26b2747962f2ce16684a971`
- 交接基线：`8bf3a76c1b739a4af11dad225741b39e4b5665ba`
