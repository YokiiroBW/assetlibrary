# AssetLibrary Windows 只读试用包

本包提供同源 HTTPS Web、登录、物理资源库登记、首次只读扫描、目录浏览和名称/路径搜索。它不是完整 Alpha 或 Windows Service 发行；不安装 Explorer 扩展，不启用资产上传、下载、移动、改名、删除或 Provider。扫描写入索引和任务记录，不修改资产文件。

## 前置条件

- Windows x64、PowerShell 7.5+、Python 3.12+，以及 PostgreSQL **16.15** 的完整 Windows `bin` 目录（含 `initdb`、`pg_ctl`、`postgres`、`psql`、`pg_dump`、`pg_restore`、`pg_controldata`）。工具路径只保存在本部署的私密配置中，不修改全局 PATH。PowerShell需保留JSON字符串的原值，避免把名称或重试有效期隐式改成当地日期格式。
- 包已包含自包含 .NET Host 和预构建 Web，日常运行不需要 .NET SDK、Node 或 pnpm。
- 将包解压到本机固定磁盘的独立目录；保留 `package-manifest.json`。每次操作均校验所有包文件的长度、SHA-256 和来源绑定；不要把状态或资产放入包目录。
- 使用拥有资产**读取权限**的普通 Windows 用户运行。状态目录的父目录必须已存在；状态目录必须是新的或已由本包初始化的私密目录，完整路径最长140字符。脚本拒绝网络状态盘、链接、外来目录及配置重入不一致。

## 1. 初始化

新建 `settings.json`，配置可登记的物理目录范围。下面仅为格式示例，请替换为自己的路径；初始化不读取或创建资产内容。Web 管理员只能在配置的范围内登记库。

```json
{
  "public_origin": "https://localhost:5443",
  "storage_sources": [
    {"source_key":"nas","display_name":"我的 NAS","allowed_root":"\\\\nas\\assets"}
  ]
}
```

在包目录打开 PowerShell 7，替换下面的工具和状态路径：

```powershell
.\trial.ps1 -Action initialize -StatePath C:\AssetLibraryTrialState -SettingsFile C:\TrialSettings\settings.json -Python C:\Tools\Python\python.exe -PostgresBin C:\Tools\PostgreSQL16\bin
```

默认数据库端口55432，仅监听127.0.0.1；冲突时首次初始化可指定 `-DatabasePort 55433`。Web Origin 仅允许 localhost 或127.0.0.1的 HTTPS 高端口，且不能与数据库端口相同。本包先提供本机浏览器试用，不替代局域网公开部署。

初始化创建独立 PostgreSQL 数据目录、随机数据库口令、六个分模块 NOINHERIT 登录角色、迁移账号、TLS证书和持久密钥。迁移复用仓库工具，先校验备份再前进应用；不修改现存 PostgreSQL 实例。数据同步与 WAL 保持 PostgreSQL 默认耐久设置。完成后数据库停止，状态保留。用完全相同参数重入会确认已初始化；中断后允许继续可识别的阶段，身份不明或半个 `initdb` 结果会拒绝覆盖，请保留现场并另选新状态目录或检查错误码。

初始化会生成90天的 localhost 自签名服务器证书，不自动修改系统或用户信任库。浏览器使用前，核对 `tls\localhost.cer` 的 SHA-256 与你刚初始化的文件，然后由你手动将**这个证书**导入“当前用户 → 受信任的根证书颁发机构”。可双击证书使用 Windows 证书导入向导；请选择当前用户范围。不要导入来源不明的证书，也不要关闭浏览器 TLS 校验。该证书还保护持久密钥，**不要直接替换或删除原 PFX、口令和密钥目录**。本轮没有证书与数据保护密钥的联合续期工具；到期前应停止、保留完整备份，并等待明确支持旧密钥解密的升级流程。卸载试用后可手动移除你导入的这张证书。

## 2. 首个管理员

```powershell
.\trial.ps1 -Action operator -StatePath C:\AssetLibraryTrialState -OperatorAction bootstrap -AccountName admin -DisplayName 管理员
```

脚本隐藏输入口令，口令不会进入命令行、配置文件或日志。至少15个字符，并且必须通过现有联网风险筛查；首次初始化管理员、恢复或改密需要风险服务可用，断网会明确拒绝而不会跳过检查。已有账号的日常登录和浏览不依赖该联网服务。

授权ID、操作ID和10分钟有效期保存在不含口令的私密 `operator-attempt.json` 中。遇到暂时失败时，重复同一条命令、输入相同口令即可重试，保持同一逻辑操作身份。过期或明确开始另一项操作时使用 `-NewAttempt`。`-PasswordFromStdin` 仅供受控自动化，通过标准输入传入口令、随后关闭输入；不要将口令拼入命令、管道历史或磁盘文件。

## 3. 启动、检查、停止

```powershell
.\trial.ps1 -Action start -StatePath C:\AssetLibraryTrialState
.\trial.ps1 -Action status -StatePath C:\AssetLibraryTrialState
.\trial.ps1 -Action stop -StatePath C:\AssetLibraryTrialState
```

启动后访问初始化时显示的 HTTPS 地址，登录 → 登记资源库 → 明确开始首次扫描 → 查看进度 → 浏览与搜索。启动脚本先启动自己的 PostgreSQL，再启动隐藏 Host，等待实际 HTTPS 就绪；重复启动会拒绝。脚本核验记录的 PID、可执行文件路径和创建时间，仅停止本部署的进程，不按进程名批量终止。

停止会终止本部署 Host/只读子进程，再让独立 PostgreSQL 快速、安全停止。数据库记录、索引、配置、密钥和资产保留。重启仍使用同一目录；已确认的首次索引不会因进程重启自动清空。不要在运行时移动包或状态目录。机器重启后手动执行 `start`；本包不注册服务、计划任务或自动启动项。

## 管理员恢复与密钥

```powershell
.\trial.ps1 -Action operator -StatePath C:\AssetLibraryTrialState -OperatorAction recover -AccountName admin -NewAttempt
.\trial.ps1 -Action operator -StatePath C:\AssetLibraryTrialState -OperatorAction rotate-key
```

恢复复用已有核心授权与会话撤销规则。密钥轮换是显式的本机管理员操作，不能恢复丢失的全部部署状态。正常重启不要轮换密钥。备份应包含整个已停止的私密状态目录和对应包版本；它不包含、也不应该添加资产原文件。

## 故障与升级

输出只含稳定错误码。`trial_already_running` 表示同一实例正在运行；`trial_host_process_owner_mismatch` / `database_process_record_mismatch` 时保留现场，不自行删 PID 文件或终止陌生进程。`trial_package_integrity_failed` 表示包文件不匹配，重新解压可信原包；`trial_prerequisite_integrity_changed` 表示原工具被替换。数据库失败时检查私密 `logs\postgres.log`；Host 日志在同目录，不要将整个私密目录公开上传。

本轮不提供自动跨版本更新。包和状态绑定来源清单，替换包会拒绝启动；升级前停止并备份，由后续版本明确迁移/回退流程。旧备份、数据和工具不被清理脚本自动删除。完整规模、长期运行、预览和 Explorer 等仍受原项目门禁约束。

## 从源码构建

在干净、已提交的仓库中运行：

```powershell
python -I -B scripts/build_read_only_trial.py --dotnet C:\Tools\dotnet\dotnet.exe --node C:\Tools\node-v24.20.0\node.exe --pnpm C:\Tools\pnpm\bin\pnpm.cjs --output-root .runtime/sandbox-storage/V01-019/build-01
```

构建使用该次 HEAD 的隔离 Git 快照；Host、Web、迁移和运行脚本来自同一个提交，不读取其他任务的旧产物。SDK精确版本来自仓库，Web工具固定 Node24.20.0 / pnpm11.19.0，依赖锁保持不变。输出包含可运行目录、ZIP、包文件清单和构建证据；构建成功本身不等于通过原生端到端验收。已有输出目录不会被覆盖或删除。
