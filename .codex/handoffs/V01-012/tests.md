# V01-012 测试记录

## 执行环境

- Windows 独立 worktree：`C:\YOKI\Codex\worktrees\V01-012`
- .NET SDK：10.0.111；Release 配置
- Python：3.12.14，最终验证使用 `-B`/`-I -B` 禁用 bytecode 缓存
- PostgreSQL server/client：16.15
- PostgreSQL：测试进程创建的一次性 cluster；角色、数据库、备份和端口均为随机测试资源
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

本机命令使用任务记录中的固定绝对工具路径；连接串和合成测试 secret 只在测试子进程内使用，没有写入交接、日志或 Git。

## 架构与契约测试

- architecture baseline：通过，扫描 100 个输入；修正派生器位置后 GatewayAuth 无 Domain 反向依赖、跨模块内部引用或平台依赖。
- .NET source policy：通过，扫描 186 个 C# 文件；凭据登录与预配共用敏感字节校验/清零实现，无重复 token block 或敏感认证值日志。
- migration manifest：11 条连续 migration、11 个模块、PostgreSQL 16.15，checksum/owner/文件集精确一致。
- runtime 对 lifecycle audit、credential、session 和 principal 表的直接访问被拒绝；也不能执行内部 actor helper，只能执行 migration 11 的四个公开函数。
- CoreServer readiness 对版本 11 ready，对 pending、extra 和 checksum drift 继续 not-ready。

## 通过

- .NET：220/220；WebGateway 36/36、Packaging 30/30；完整 solution 0 failed / 0 skipped，Release 构建 0 warning / 0 error。
- database：62/62；最终完整真实 PostgreSQL 套件耗时 59.421 秒。
- repository：54/54；architecture：14/14；release：32/32。
- 唯一自动测试合计：382 passed、0 failed、0 skipped。
- migration validator：`MIGRATION_MANIFEST_OK migrations=11 modules=11 postgresql=16.15`。
- repository verifier、server release definition validator、默认 Alpha integration audit 均退出 0。
- `v0.1-start`：`RELEASE_GATE_ALLOWED`。
- `--require-ready` 与 `v0.1-release`：均输出预期 blocked，原生退出码 3。

## 过程性失败与修正

- 首轮旧就绪断言仍期望 10 条 migration；同步到 11 后通过。
- 新超时用例令单个测试类触发 CA1506；拆成独立小测试类后 WebGateway 36/36。
- source policy 发现新旧凭据对象重复清零代码；抽取 GatewayAuth 内部共享敏感哈希材料后通过。
- architecture gate 发现派生器从 Domain 反向引用 Application；将具体派生器归入 Application 后通过。
- 一次 release suite 因系统 `dotnet` 不含 SDK 而失败；设置任务固定 .NET 10.0.111 路径后 32/32。
- 一次误用 bytecode 编译生成单个 `__pycache__`；精确删除后 repository 54/54，最终扫描无缓存残留。
- blocked 命令的退出码 3 是负向验收，不计为测试失败。

## 生命周期、故障与恢复验证

- Application 层：普通用户在风险检查前拒绝；短、常见、泄露、不可用、超时和异常口令路径均不调用 store。
- 派生：每次独立随机盐，600,000 次 PBKDF2-HMAC-SHA256，32-byte 结果可由 V01-011 verifier 验证；临时输入、盐和摘要缓冲区清零，字符串化为 `[redacted]`。
- 数据库 actor 复验：普通账号、随机伪造 principal、陈旧 session version、禁用管理员均返回 `unauthorized`；拒绝路径不写 lifecycle audit。
- 预配：相同 operation 重放不重复写入；相同 ID 不同语义冲突；账号冲突不覆盖已有显示名、管理员事实或摘要。
- 轮换：credential/session version 从 `1/1` 到 `2/2`，失败计数归零、retry 时间清空、旧 session 立即拒绝。
- 启停与恢复：session version 随每次启停增加；禁用后凭据恢复重新启用并推进到 credential/session `3/6`，重放不再增版本。
- 重启：在生命周期恢复后快速重启 PostgreSQL，账号启用状态、版本和新摘要保持，管理员仍可精确读取。
- 最后管理员：两个启用管理员并发停用恰好一个 `applied`、一个 `last_administrator`，最终保持一个启用管理员。
- 审计：7 个首次写语义对应 7 行；重放不新增；表结构无 secret/salt/digest/token/CSRF 字段，runtime 无读取权限。
- 完整 database suite 继续覆盖并发 migration、ledger/checksum drift、备份恢复、TaskHealth、10 万目录查询、会话重启与 Host readiness。

## 性能与边界数据

- 口令 15–128 个有效 Unicode scalar；PBKDF2 固定最低 600,000 次、16-byte 盐、32-byte 派生值。
- 风险检查、应用操作、Npgsql command 和 PostgreSQL statement 均有 5 秒截止；真实风险超时单测约 5 秒并验证零写入。
- 生命周期按 operation UUID、账号名、subject 唯一键或索引查找；单操作 `O(log n)`，会话撤销只触及目标主体，和 50 万资产无关。
- 最终完整数据库回归 62 项耗时 59.421 秒。

## 清理与残留

- 最终检查为 0 个命令行关联 V01-012 的 `postgres.exe` 进程。
- 所有临时数据库、备份、cluster、监听端口、Python `__pycache__` 和空 `.runtime/sandbox-storage/V01-012` 已回收。
- 仅保留 Git 忽略的 `.runtime/dotnet-home` 和 `.runtime/nuget` 构建缓存；不含连接串、口令、token 或真实资产。

## 尚未覆盖

- 未运行 GitHub 托管 Linux CI；不把本机工作流定义验证冒充远端证据。
- 未实现或连接生产常见/泄露口令 Provider、首位管理员 bootstrap、带外恢复或部署授权 secret。
- 未实现 HTTP/CLI 账号入口、HTTPS、Cookie/Origin adapter、持久密钥、证书、外部速率限制或认证后的业务 Host 组合。
- 未连接生产数据库，未创建生产账号，未访问真实资产/NAS，也未关闭生产认证、数据库组合、业务 API、生产写入或任何外部平台 release gate。

## 提交

- 核心实现：`849a8ab8d7e5d05629ec401db4dc77c142f27e56`
- 最终恢复/重启证据：`250b49a38fc02d1d708eafe16775b0a1ad9e5282`
- 交接基线：`7b813a2f0b70dc97b4d5fbd977cddef5df73542d`
