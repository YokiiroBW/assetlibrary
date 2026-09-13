# Windows 原生资源管理器只读浏览

Windows 日常入口是原生 Explorer 左侧“资产库”，图库和列表显示在同一窗口右侧。连接设置是 WinUI 辅助窗口；普通用户会话 Host 常驻并处理登录、分页及图片请求。已交付版本、安装包和实机边界以 [Windows 发布说明](../../docs/releases/WINDOWS_EXPLORER_PREVIEW.md) 为准；当前已验收版本为 `0.3.0-preview.6`。

图库保留图片比例，按可见区域加载缩略图，支持图库/列表切换、密度、键盘导航、多选和明确分页。每页最多100个普通项，另有分页入口。目录与文件身份来自 Core 的只读授权投影，文件名只供展示；退出、权限失效或换代会清空旧页面及图片。没有预览 Provider 时显示预览不可用，不读取原件绕过限制。

`Core` 复用生成 AssetLink SDK 与现有只读接口；`AssetHost` 把当前授权页和受限像素投影传给原生 Shell。网络、媒体解码和会话秘密均留在 Explorer 进程外，没有本地权限/路径/文件操作业务副本。连接只接受一个 HTTPS origin，默认系统 TLS 信任；用户显式 SHA256 叶证书指纹仍校验主机名与有效期，禁止自动重定向。记住登录默认关闭，启用时使用当前用户 DPAPI 与用户专属安全文件；Cookie/CSRF 不写普通配置、命令行或日志。

## 大图 Host 模块（V03-024）

新增 `preview-v1` 固定1600通道，复用现有 `variant=preview` 服务端派生 PNG。原 `thumbnail-v1` 仍严格限制512边长和2MiB编码数据；大图限制1600边长、12MiB编码数据及10,240,000字节PBGRA。两通道合计最多4个图片客户和4个待处理任务，图片共用1个HTTP许可，HTTP总许可2个以保留导航能力。JSON保持8秒/1MiB，图片20秒总预算包含排队、网络、重试和解码。

系统 WIC 只在封闭规格的 Host 子进程执行；子进程受128MiB、1进程、3秒和关闭即终止的 Job 约束，不接收路径、URL或凭据。Shell仅接收校验后的预乘BGRA。客户端断开、退出或旧epoch失效会取消工作；没有图片完成缓存。该模块的自动验证不代表新的安装包或大图界面已经通过实机验收；集成结论继续由发布说明记录。

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

## Explorer 验收边界

preview.6 的真实 Explorer 图库证据见发布说明与[集成交付](../../.codex/handoffs/V03-005/windows-gallery-delivery/README.md)。G1/G2/G3沿用已记录实测证据；G4按用户要求豁免且未测试。NAS图片引擎限制仍在，本机隔离Core图片成功不能代替NAS能力；完整V0.3、签名与Android真机门禁没有因此关闭。

原 [Proof说明](AssetHost/README.md) 和 [C++测试说明](../../tests/windows-shell/README.md) 仍用于兼容性与独立机制验证。生产会话与Proof端点分离，不能用机制测试替代实际安装、Explorer生命周期及卸载证据。上传、下载、同步和文件写入不在此只读模块范围内。
