# V01-014 测试记录

## 执行环境

- Windows 独立 worktree：`C:\YOKI\Codex\worktrees\V01-014`
- 分支：`codex/v01-014-admin-bootstrap-recovery`
- .NET SDK：10.0.111；Release 配置
- PostgreSQL：16.15，任务专属临时 cluster/database/roles，不连接生产环境
- Python：3.12.14；仓库/发布工具使用 `-I -B`，数据库集成使用 `-B`

## 执行命令

```text
dotnet restore AssetLibrary.slnx --locked-mode
dotnet format AssetLibrary.slnx --verify-no-changes --no-restore
dotnet build AssetLibrary.slnx --configuration Release --no-restore
dotnet test AssetLibrary.slnx --configuration Release --no-build --no-restore
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

本机执行使用任务临时目录中的固定 .NET/PostgreSQL 路径及仓库自带角色预配；没有在命令行、文件、日志或 Git 中写入真实 secret、token、生产连接串或资产路径。

## 架构与契约测试

- architecture：14/14；依赖方向和模块边界通过，GatewayAuth Application 不依赖 Npgsql/Host/网络/文件系统。
- .NET source policy：通过，扫描 206 个 C# 文件；未发现敏感日志或重复受管代码块。
- migration manifest：21/21 connection-free 检查通过；12 条 migration、11 个模块、PostgreSQL 16.15 和 migration 12 SHA-256 精确匹配。
- 新表无 runtime 访问权，内部 helper 无 runtime execute 权；runtime 只获两个有界函数的 execute。

## 通过

- .NET：257/257；WebGateway 73/73，完整 solution 0 failed / 0 skipped，Release 构建 0 warning / 0 error。
- database：67/67；repository：54/54；architecture：14/14；release：32/32。
- 唯一自动测试合计：424 passed、0 failed、0 skipped。
- locked restore、format verify、repository verifier、server release definition validator 和默认 Alpha integration audit 均退出 0。
- `v0.1-start` 输出 `RELEASE_GATE_ALLOWED`。
- `--require-ready` 和 `v0.1-release` 均输出预期 blocked，原生退出码 3；这两项是负向验收，不计为失败。

## 失败 / 跳过

- 最终结果为 0 failed、0 skipped。
- 首轮完整数据库回归暴露 3 个测试环境/旧版本断言：一个时间边界 fixture 和两个 migration 11 断言；修正为稳定过期窗口与 migration 12 后，定向及 67 项完整数据库套件全部通过，未发现产品逻辑回归。
- 首轮 repository/release 命令遗漏 Python `-I`，工具按设计拒绝导入；使用合同规定的 isolated mode 后分别 54/54 与 32/32 通过。

## 故障注入与恢复验证

- verifier rejected/expired/unavailable/unknown/null、绑定 action/operation/target/expiry 漂移、调用前已过期、风险检查期间过期和凭据派生期间过期均验证下游零写入。
- 风险 `Common`/`Compromised` 返回 secret rejected；`Unavailable`、超时和异常返回 dependency unavailable；调用方取消传播，内部 deadline 不泄露异常正文。
- bootstrap 验证空系统成功、相同请求幂等、authorization/operation 双 ID 绑定冲突、账号/subject/principal 冲突不覆盖、历史管理员停用后不重开，以及两个并发请求只有一个 applied。
- recovery 验证未知账号、普通账号、OIDC 管理员和陈旧 credential version 均得到相同无详情 conflict；成功路径清退避、重新启用、credential 7→8、session 1→2、旧 session 撤销并在数据库重启后保持。
- 数据库在锁后重新检查 expiry；过期请求不创建账号、不写 audit；审计表不存在 secret/salt/digest/token/CSRF/proof/risk 列。
- 真实 C# PostgreSQL adapter round trip 完成 bootstrap 与 recovery，证明参数顺序、类型、事务和结果映射不是仅 SQL fixture 自证。

## 性能数据

- Application 总 deadline 固定不超过 5 秒；PostgreSQL executor 使用既有 5 秒连接/statement timeout。
- 两个写函数均为单事务、单个全局低频管理锁；目标查找走账号唯一约束，防重放走 authorization 主键和 operation 唯一键，审计目标时间查询有复合索引。
- 单次凭据 material 固定 16-byte salt 和 32-byte digest，使用后清零；无资产读取、网络传输、媒体处理或与 50 万资产数量相关的扫描。

## 清理与残留

- PostgreSQL 测试实例、listener 和 .NET build/compiler server 已停止；进程检查为零任务进程。
- 工作区内任务工具目录已移出，避免 Codex 扫描大型 SDK/数据库树。
- 系统安全策略阻止递归删除；任务下载的临时工具仍位于 `C:\Users\Administrator\AppData\Local\Temp\V01-014-tooling-and-tests`（约 1.30 GB）和 `C:\Users\Administrator\AppData\Local\Temp\V01-014-pgAdmin4`（约 0.72 GB），可由操作者安全删除。
- 主工作区 `C:\YOKI\Codex\AssetLibrary\.runtime\sandbox-storage\V01-014` 仅剩空目录，无文件或进程。

## 尚未覆盖

- 未实现或测试真实部署授权 verifier、secret 轮换/撤销、操作者 UI/CLI/HTTP、Host DI、TLS、Cookie/Origin/CSRF、持久 key 或上游 abuse control。
- 未连接生产数据库、未创建生产账号、未访问真实资产/NAS，也未解除生产认证、业务 API、数据库组合、生产写或 release target 门禁。
- 未运行 GitHub 托管 Linux CI。

## 提交

- 实现：`ee8c0305bd0611d8698fcab9602964158d4d5d81`
- 交接基线：`4c6d390b8cfa2084b2a5495cd85f837d3914222b`
