# V03-017 — Windows 用户级安装器与真实打包入口

状态 ready_for_review；代码提交 `62f0814`；分支 `codex/v03-017-windows-setup-package`。交接补丁另补 Session owner 冻结的 `AssetLibraryProductionHost=true` 发布参数。root共享基线 2ebda30/fe8b4f6 在本分支分别重放为 f9cf7b4/e067fe6，仅用于最终依赖验证，不要重复合并这两笔。

已交付可实际运行的 C#/.NET 10 自包含 AssetLibrary.Setup.exe、版本化复制/完整 manifest 与 SHA256 校验、当前用户注册事务/回滚、真实文件锁延期清理、install/uninstall/status 和双击原生 TaskDialog。未改 Session、Host、Settings、Shell 实现或 Windows 主 solution；本任务独立测试 solution 只包含 Setup 与安装测试。

产物在本 worktree `.runtime/setup-publish-final/AssetLibrary.Setup.exe`，73,586,439 字节；SHA256 `9f1a330f4456ecccf4096215f79de831ff5295a1e6cdd99b67a6004851b136a7`。这是用于验证安装逻辑的真实安装器，尚未与最终三个生产组件组装为对外交付包；根任务应从合并后统一源码重建。未签名，未安装自签证书、修改信任/UAC、写 HKLM、结束 Explorer 或访问真实 NAS。

35 个范围内验收通过：23 个 .NET 安装/故障/安全案例、4 个 Python 打包单元测试、8 步真实自包含 EXE 的 sandbox CLI。format/build 零警告，普通与 RID locked restore 通过，依赖/许可证/漏洞检查通过（2 projects、15 locked packages），仓库 architecture/source/contracts 验证通过。详见 tests.md 与 cli-results.json。

复用共享生产 CLSID/owner、Desktop NameSpace 注册形态、STA/COM/PIDL 有界通知方式和 Host --shutdown-user-session。安装器只持有本产品文件/注册，不复制资产权限、目录身份、读写或传输业务。AssetHost/Core/Shell 仍各自在已冻结边界执行职责。无新语言、框架、重大依赖或数据库迁移。

构建脚本 infra/windows-client/build_package.py 从真实 CMake 与 dotnet publish 输出组包。组件位于同一版本目录，重复文件必须 SHA 相同；同版本不同内容拒绝覆盖。源/目标文件均流式校验；manifest ≤2 MiB、4..4096文件、payload总 ≤2 GiB、路径 ≤220字符/16层、目录遍历 ≤8192项；安装复杂度随包文件与字节数线性增长，不依赖资产数量，不建立资产索引。

只写 HKCU 生产 CLSID、Desktop NameSpace、产品元数据、AssetLibrary 单一 Run 值、Uninstall/AssetLibrary 与 Settings App Paths。无 HKLM/UAC 后备。安装前检查普通用户/Win11x64/无 package identity/UAC enabled；此检查不替代系统 Explorer 的原生来源实测。status 准确只称当前进程注册视图。

卸载保留用户连接配置。DLL 或执行中的 Setup 被占用时返回3010，记录待清理版本，不宣称全部组件已删除；再次双击解压包 Setup 会继续待清理卸载。升级保留旧版本，回滚注册不覆盖配置。外来归属、未知文件和改变的哈希均保留并拒绝或报告待清理。当前清理采用显式重试，尚未引入注销/重启清理任务。

建议合并：root共享契约/ADR/依赖策略 → V03-015 Session/Settings/Host → V03-016 Shell → 本任务62f0814与交接补丁 → root生成其余RID锁并统一重建/安装。Shell使用批准 /MT，脚本校验导入表无动态VC CRT；root提供适用VS/SDK/WinAppSDK notices。生产HKCU、TaskDialog、原生Explorer登录/浏览/换身份/卸载验收归root单一操作。本任务不把合成fixture、源码测试或G4豁免记为系统UI/完整V0.3完成。

整包评审补充：所有 .NET 组件的 restore/publish 共同传入 Version=0.3.0-preview.1；RuntimeFrameworkVersion=10.0.11 只传 Setup/Host，Settings 依其项目的 Microsoft.NETCore.App 定点 metadata 固定运行时，避免污染 Windows SDK framework reference。新增边界回归验证三个项目六个调用，打包测试共5项通过。上文旧单文件安装器与SHA保留为首次安装逻辑证据，最终EXE必须使用本次版本参数重新发布。

已静态复查 Windows 设置的卸载调用：带引号绝对路径 Setup.exe uninstall → Program.Parse/前置检查/非quiet确认 → UninstallAsync → 完整核验旧Host → --shutdown-user-session → 注销与清理。卸载不依赖外部payload，也不启动Settings/Host新会话；Host退出失败先保留注册，正在运行Setup自身造成3010延期符合已披露流程。未发现命令解析或分支接线新阻断，没有执行GUI或真实注册；Windows设置实际启动上下文仍由root实机验收。未改root维护的notices。
