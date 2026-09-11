# Windows 进程外只读适配

用户已明确 Windows 日常入口必须位于原生资源管理器。本目录保存进程外 AssetHost 可复用的 .NET 10 AssetLink 适配组件；它不是已交付的独立桌面应用，也不要求用户打开独立客户端。

`Core` 只消费既有生成 SDK 与已批准的读取接口：HTTPS 登录/会话/退出、分类资源库、完整范围服务器排序与筛选、目录/范围搜索、详情。每页最多100条；导航历史100项、分页游标最多200项、当前页替换且有取消/代际检查。没有本地权限/身份/路径/文件操作业务副本，没有资产内容写入。

连接只接受一个 HTTPS origin；默认系统 TLS 信任，用户显式 SHA256 叶证书指纹仍校验主机名和有效期。Cookie 与 CSRF 只在进程内，禁止自动重定向；错误正文不会进入诊断，401/403/404优先清除快照，即使响应体错误或卡住。未实现持久凭证、设备配对、主备切换、预览、文件传输与同步。

## 验证入口

仓库 `global.json` 固定 SDK 10.0.111。Windows适配单独 solution，复用根级测试依赖版本；不向跨平台服务端solution加入平台项目。

```powershell
dotnet restore apps/windows-client/AssetLibrary.Windows.slnx --locked-mode
dotnet format apps/windows-client/AssetLibrary.Windows.slnx --verify-no-changes --no-restore
dotnet build apps/windows-client/AssetLibrary.Windows.slnx --configuration Release --no-restore
dotnet test apps/windows-client/AssetLibrary.Windows.slnx --configuration Release --no-build --no-restore --filter "TestCategory!=NativeLive"
dotnet package list --project apps/windows-client/AssetLibrary.Windows.slnx --include-transitive --vulnerable --no-restore --format json --output-version 1 > .runtime/windows-vulnerabilities.json
python scripts/validate_dotnet_dependencies.py --solution apps/windows-client/AssetLibrary.Windows.slnx --packages-dir .runtime/nuget --vulnerability-report .runtime/windows-vulnerabilities.json
```

实际 HTTPS Core/PostgreSQL 联调通过隔离支架提供的私密 profile 运行，见 [测试说明](../../tests/windows-client/README.md)。协议与 UI/Explorer 证据分别记录。

## Explorer 状态

[test-only DefView/IShellFolder2 验证](../../tests/windows-shell/README.md)已可构建。已定位诊断执行器的 MSIX 注册表视图与普通 Explorer 不同；同一微软样例改由经核验的普通用户 Explorer 启动登记后可实际进入。该结果只解除环境阻断，本项目自身类工厂/视图仍需重新实机验收。注册工具现在拒绝未经核验启动路线的登记，局部 presence/absence 不再代表系统 Explorer 注册或清理。当前没有可用 Explorer 安装包，不关闭 M0-002-G1..G4 或正式发布门禁。后续入口接通后，才把授权快照通过受审查的本机 IPC 接到 Shell。Shell 仍不得加载 .NET/WinUI、网络、媒体或 Provider；新增 C# 上下文检查只运行于进程外测试脚本。
