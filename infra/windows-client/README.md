# Windows 11 x64 只读预览包

在主任务合入 Setup、Session/Settings/Host 和生产 Shell 后，从统一源码构建。版本 `0.3.0-preview.6`；产物未签名，如实标注。禁止拿合成测试文件组装对外交付。

使用 `global.json` 固定 .NET SDK 10.0.111、Visual Studio 2022 Build Tools（C++ workload、CMake、Windows SDK 10.0.26100）。生产 Shell 使用批准的 /MT；不安装 VC redistributable、不提权。需将实际 Visual Studio/Windows SDK 与 Windows App SDK 适用 license/notices 通过 `--notice` 加入最终包；.NET 自包含 publish 中 LICENSE/ThirdPartyNotices 同目录保留。

```powershell
python -I -B infra/windows-client/build_package.py --dotnet <dotnet.exe> --cmake <cmake.exe> --build-root .runtime/windows-package-build-1 --output .runtime/AssetLibrary-0.3.0-preview.6-win-x64 --notice <applicable-license-or-notice-file>
```

脚本逐个 locked restore / self-contained Release publish，然后调用生产 CMake target，校验 x64 PE 与 DLL import、运行时必需文件、Settings 应用资源 AssetLibrary.Settings.pri 存在且非空、重复依赖 SHA 相同，再生成完整 manifest、来源 commit/PE imports 证据、ZIP 与外部 SHA256。`--output` 和组件 publish 目录必须不存在；不会清空旧产物。

打包后的 zip 只需用户全部解压并双击 Setup。源码调试可对已真实构建的四组件执行同一 assemble 逻辑：

```powershell
python -I -B infra/windows-client/build_package.py --assemble --setup-dir <setup-publish> --host-dir <host-publish> --settings-dir <settings-publish> --shell-dll <AssetLibrary.Explorer.dll> --output <new-output-directory> --notice <applicable-license-or-notice-file>
```

RID restore 必须显式 `-p:RuntimeIdentifier=win-x64`，避免仅 `--runtime` 的外层 RuntimeIdentifiers 让 RID 锁路径条件失效。共同参数为 SelfContained=true、Version=0.3.0-preview.6、AssetLibraryReleaseLockRoot=本目录/locks，确保清单版本与可执行文件产品版本来自同一冻结值。

Setup/Host 使用 RuntimeFrameworkVersion=10.0.11；Settings 由其 csproj 定点固定 Microsoft.NETCore.App FrameworkReference 的运行时版本，禁止向 Settings 传全局 RuntimeFrameworkVersion，以免污染 Windows SDK framework reference 还原。Setup 另用 PublishSingleFile=true 与 IncludeNativeLibrariesForSelfExtract=true；Host 另用 AssetLibraryProductionHost=true 以产生 AssetLibrary.Host.exe（默认项目输出保留 Proof 测试兼容）。锁文件由主协调者生成并审核；发行脚本只接受 `--locked-mode`，不自行解锁。

尚未包含 Authenticode 发布者签名、无人值守自动更新、日志轮转或完整长期稳定性验收；G4 明确为用户豁免未测试。实际 HKCU 与系统 Explorer 安装/卸载验收由主协调任务单一操作，沙箱测试不替代此证据。
