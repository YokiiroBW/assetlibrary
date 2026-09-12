# Windows preview dependency review

2026-09-12，ADR-0020 的精确 Windows 依赖审查。完整上游条款随包保留，不能将 Microsoft 专有 SDK 条款标成 MIT。

- `license-review.json` 记录 14 个实际还原 NuGet 包的许可文件与 SHA256。Windows App SDK（含 ML Runtime）第 3 条允许分发其 NuGet 放入应用输出的文件；只分发本应用的 Release publish 输出，保留适用条款，不分发开发工具或独立 SDK。Settings 提供主要连接功能；本应用未调用 AI、ML、WebView2 功能。
- `Microsoft.Windows.SDK.BuildTools` 10.0.26100.4654 仅有旧式 licenseUrl。精确 nuspec SHA256 `aac383f40ed26caedb6b748ae1470722380039baec34923bb37006517cada5a7` 被单独批准；任何版本、URL 或元数据变化重新审查。其官方 URL https://aka.ms/WinSDKLicenseURL 于审查日解析至 https://download.microsoft.com/download/0/F/F/0FF2B061-47DD-4F55-89B6-FD1D8C44F14D/sdk_license.rtf，完整原始字节保存在本目录，SHA256 `dd07eb178e00c6bba4148457fc00ff77cd4887eb521d504186fe59c9ec8bbe62`。此包属于构建工具，不作为独立工具分发。
- 生产 Shell 使用 Visual Studio 2022 C++ Release 静态运行库。仅分发与应用链接后的 DLL，不分发 `.lib`、调试运行库或系统 DLL。Microsoft 的说明：https://learn.microsoft.com/en-us/visualstudio/releases/2022/redistribution 与 https://learn.microsoft.com/en-us/cpp/windows/determining-which-dlls-to-redistribute 。系统 UCRT 由 Windows 11 提供；生产 DLL 的实际导入检查必须通过。
- 自包含 .NET 10.0.11 的 LICENSE/ThirdPartyNotices 与 Windows App SDK publish 自带第三方声明必须保留。所有运行库均随实际组件合并，重复路径只有 SHA256 相同才能合并。

这些例外仅解除所列精确依赖的自动许可证审查阻断，不批准新的框架、Provider、自动更新、遥测业务或其他包版本。重新分发方及最终用户须遵守随包 Microsoft 组件许可；不得移除声明、以微软名义提供本产品或单独分发开发工具。
