# V01-019 — Windows只读试用运行包交接

状态：ready_for_review，2026-09-07。所属模块：Windows部署适配器及本机Host进程控制；owner：server-packaging-owner。分支 `codex/v01-019-windows-read-only-trial-package`。任务实现与验证完成；不替主协调者宣布完整V0.1或平台发行完成。

## 交付

最终运行目录：`.runtime/sandbox-storage/V01-019/delivery-rsa-doc/assetlibrary-read-only-trial-win-x64/`；同目录上级包含ZIP、`build-evidence.json`和`native-final-smoke.json`。包含自包含.NET10 Windows x64 Host、最终Web、18个前进迁移、私密本机PostgreSQL初始化工具、中文说明，以及initialize/start/status/stop/operator入口。

- 不可变产物来源commit：`864da3b2bfeca614b2654eb380ef6074d826b6c4`。
- Git tree：`4cbfffc99e714466f20b6e3b44e2c836f188cb92`。
- ZIP：`assetlibrary-read-only-trial-win-x64.zip`，49,716,196 bytes。
- ZIP SHA-256：`13caada27dec64f165da1b5bd615422e1378c580ca7faf1dce06def6f372e360`。
- `package-manifest.json` SHA-256：`b2f4d12d3e2e5e6519debd67b9e76e18ab7910849c7a46e2680034cd0db419dd`。
- 372个包文件（含manifest）及ZIP内全部文件逐个流式核验长度与SHA-256通过。后续交接元数据commit不改变上述产物身份。

## 行为与复用

构建复用既有release source snapshot、精确SDK、RID锁和self-contained publish底层，从一个干净HEAD构建Host/Web/迁移/脚本；不读取其他任务的旧dist，不关闭原release gate。PG初始化复用migration_tool的角色provision、验证备份与前进迁移；创建自己独立的loopback SCRAM集群、迁移LOGIN及六个不同NOINHERIT运行LOGIN，保留固定NOLOGIN角色定义不变。拒绝外来state、身份漂移、链接、篡改包、重复启动和跨包重入。

管理员初始化/恢复/轮换复用Host operator及已有认证状态机；口令只通过受限stdin，重试ID/有效期持久保存但不存口令；真实风险检查仍失败关闭。已有账号登录不增加外网风险依赖。PowerShell7.5保留JSON字符串原值，输入输出固定UTF-8。

主协调者批准新增的TrialProcessControl仅在私密state使用≤4KiB的代次/PID/路径/开始时间/随机值绑定停止请求；每500ms检查，无网络入口。启动器隐藏进程并关闭父捕获句柄继承。正常stop及启动回滚都先请求退出、最多等待45秒；仅对重新核验身份的原进程作超时强杀并明确报告。初始化、扫描与停机不写资产内容。

## 验证与影响

8个V01-019唯一自动测试已通过，另执行既有Windows父硬死回收回归1项通过；不将该既有测试在总仓库计数中重复增加。真实包验证包含管理员bootstrap/replay/recover、受测试证书约束的HTTPS登录、重启持久性、私密控制请求伪造拒绝、PID伪造拒绝、源SHA-256/mtime不变、日志无秘密；最终包另通过初始化/密钥轮换/启动/正常停止/重启和完整性smoke。细节及真实失败修复记录见tests.md。

不新增语言、框架或重大依赖，不修改SQL/AssetLink/根依赖锁/既有发行门禁。物理路径、账号、权限和扫描业务仍在核心；新Host文件只负责本机进程生命周期。成本为O(包字节数)的入口完整性检查与固定频率的4KiB本机控制读取，和资产数无关；不冒称已完成50万资产性能验收。

## 范围、风险与合并

只面向Windows x64本机浏览器、loopback HTTPS和首次只读扫描；不是LAN公开部署、通用重扫、完整Alpha、Windows Service或Explorer发行。证书信任由用户手动配置；配置支持保留最多3张旧DP解密证书续期，本工具不申请证书或修改信任库/SCM/注册表/防火墙。

观察到旧强制终止保留1个本启动新增的Windows证书容器；新正常退出观察新增1、剩余0。没有读取密钥内容或删除profile容器，不能宣称早期残留已清理。强制崩溃/超时退出仍明确报告清理未确认。真实风险服务偶有TLS失败，保留同一逻辑身份重试，不绕过风险筛查。

建议在V01-016/017/018、Root Host组合及Windows Job修复后合并；这些来源已经进入产物快照。Root负责项目registry/总里程碑状态及两侧主仓库同步。保留历史worktree、旧证据和之前审批拒绝的9个空目录。当前任务源码工作区Git clean，运行包和测试证据属于ignored runtime。
