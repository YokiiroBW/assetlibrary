# WinUI 连接设置

Windows 11 x64，WinUI 3 / Microsoft.WindowsAppSDK **2.4.0 stable**。
此单窗口仅配置HTTPS NAS、证书指纹、账号、登录/退出与状态；资产浏览入口仍是原生Explorer。
默认系统字体/主题/原生焦点与输入控件，窗口可缩放且内容可滚动，密码允许正常粘贴。
关闭窗口取消该窗口的控制请求，后台已建立的连接继续运行。

Settings不引用Core网络适配，只通过本用户控制pipe联系同目录Host。
若后台尚未运行，仅启动同目录`AssetLibrary.Host.exe --user-session`。
同用户session使用AppInstance单窗口激活；不改Explorer注册或自动信任证书。

构建从仓库根使用`apps/windows-client/AssetLibrary.Windows.slnx`原有restore/format/build/test流程。
生产发布：

```powershell
$releaseLocks = (Resolve-Path infra/windows-client/locks).Path
dotnet restore apps/windows-client/Settings/AssetLibrary.Windows.Settings.csproj -p:RuntimeIdentifier=win-x64 -p:Version=0.3.0-preview.1 -p:AssetLibraryReleaseLockRoot=$releaseLocks --locked-mode
dotnet publish apps/windows-client/Settings/AssetLibrary.Windows.Settings.csproj --configuration Release -p:RuntimeIdentifier=win-x64 -p:Version=0.3.0-preview.1 -p:AssetLibraryReleaseLockRoot=$releaseLocks --self-contained true --no-restore --output .runtime/windows-settings-publish
dotnet restore apps/windows-client/AssetHost/AssetLibrary.Windows.AssetHost.csproj -p:RuntimeIdentifier=win-x64 -p:Version=0.3.0-preview.1 -p:RuntimeFrameworkVersion=10.0.11 -p:AssetLibraryProductionHost=true -p:AssetLibraryReleaseLockRoot=$releaseLocks --locked-mode
dotnet publish apps/windows-client/AssetHost/AssetLibrary.Windows.AssetHost.csproj --configuration Release -p:RuntimeIdentifier=win-x64 -p:Version=0.3.0-preview.1 -p:RuntimeFrameworkVersion=10.0.11 -p:AssetLibraryProductionHost=true -p:AssetLibraryReleaseLockRoot=$releaseLocks --self-contained true --no-restore --output .runtime/windows-host-publish
```

Host生产属性改输出名/窗口类型，保留默认Proof程序集与CLI供原测试使用。
安装器合并同目录发布组件时对重复依赖逐一验证SHA256一致，不能只复制exe或覆盖不一致依赖。
WinAppSDK采用Microsoft EULA，不是MIT；随包保留批准的许可与第三方声明。
单个Settings自包含产物约223MiB，精确发布大小与许可审计见V03-015交接。
Settings通过FrameworkReference元数据固定.NET运行时10.0.11，不能向它传全局RuntimeFrameworkVersion；
全局值会错误覆盖Windows SDK Ref框架版本。restore使用单数RuntimeIdentifier全局属性以在Directory.Build.props评估时选择发行锁。
