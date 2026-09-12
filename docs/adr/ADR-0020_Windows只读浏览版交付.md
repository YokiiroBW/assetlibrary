# ADR-0020 — Windows 原生浏览版安装与连接

状态：Accepted for implementation，2026-09-12。用户要求“可以，交付一下吧”，接受Windows优先的可安装只读浏览版。G1/G2/G3已实测通过，G4已明确豁免；不重新安排G4。

## 本批交付

Windows 11 x64，组件版本 `0.3.0-preview.1`。首次安装/连接设置使用进程外配置窗口；日常浏览始终在原生Explorer。交付用户可双击的安装程序和完整payload，完成安装、连接登录、库/目录/分页浏览、退出与卸载验收。文件内容预览/下载/同步/资产写入及完整V0.3不在此只读浏览包内；NAS图片引擎尚有独立平台兼容阻断，不能伪装成已交付。

已验收C++ Shell实现提升至 `apps/windows-shell`，测试编译同一实现的Proof身份，禁止长期维护两份。生产CLSID `{BBC992DE-CE5D-48C8-A86C-7230C7D72B02}`，显示名“资产库”，owner `AssetLibrary.Windows.Explorer`。生产快照pipe为 `AssetLibrary.Explorer.v1.<TokenUserSID>.<SessionId>`；原Proof管道/GUID保留测试用途，wire v1字节语义不变。

## 用户状态与进程边界

AssetHost继续独占服务器会话/网络和快照；Settings只通过版本化本机控制契约配置/登录/退出，不复制Core权限或路径业务。控制pipe同TokenUser SID、同Windows session、拒远程、首实例和有界请求，正文不进日志。非记住登录时凭据仅存内存；明确选择记住后使用CurrentUser DPAPI并配严格owner/DACL与重解析点拒绝。配置、状态和remembered blob位于 `%LOCALAPPDATA%/AssetLibrary/WindowsClient`，会话Cookie/CSRF不持久化；退出删除记住凭据、取消并清快照。

Settings采用既有预算的C#/.NET10、WinUI3与Microsoft.WindowsAppSDK **2.4.0 stable**（首个生产清单精确pin；不采用experimental）。采用unpackaged self-contained（.NET与WindowsAppSDK均自包含），不使用需要额外Singleton包的推送/AI/ML功能，不把任何WinUI/.NET加载进Explorer。Native许可证及每个传递包需按下载原件/hash审计并随包附许可，包体实际大小在交付时记录。安全更新沿锁定版本审计；退出路径为保持Session/Host控制契约并替换单一配置UI，不引入第二套同职责框架。

安装器是C#/.NET10部署OS适配，最小交互可使用系统TaskDialog/MessageBox，不引入WPF、WinForms、Electron或第三方安装框架。不把小型首次设置窗口视为独立浏览客户端。

生产Shell及其静态库统一采用MSVC Release `/MT` 静态CRT，以仅保留系统UCRT/Win32导入；Proof测试目标可以保留原动态CRT。包构建检查实际导入并保留VS/SDK适用许可，不要求用户另装开发环境或提升权限安装VC redistributable。此生产构建变体必须用实际产物验收，不能拿旧Proof DLL的hash代替。

## 安装、升级、卸载

固定每用户安装根 `%LOCALAPPDATA%/Programs/AssetLibrary`，版本目录 `versions/0.3.0-preview.1`，组件同目录发布。完整payload逐文件尺寸/SHA256/规范路径检查，拒绝绝对路径、逃逸、重解析点与foreign owner。同名文件来自多个发布输出时必须字节一致，不能静默覆盖依赖冲突。新版本完整写入验证后才切换HKCU注册及版本元数据，保留旧版本回滚能力。

仅生产CLSID的HKCU Classes和Explorer Desktop NameSpace，不增加MyComputer重复入口，不碰原4FF测试项。HKCU产品元数据 `Software/AssetLibrary/WindowsClient`；HKCU Run的AssetLibrary值启动Host `--user-session`；卸载项在HKCU的Windows CurrentVersion/Uninstall/AssetLibrary。无需提权/HKLM、不改UAC/证书信任/安全策略。安装器普通用户asInvoker、拒打包虚拟化上下文；不强制Explorer直系父进程，Windows设置中的卸载入口也应可用。仅当前视图自检不能证明系统Explorer可见，实际安装通过正常系统进程路径单独验收。

卸载先通过Host本机有界shutdown停止自有后台，再owner核验注销，最后仅清自己拥有的程序文件。不得杀用户Explorer。已加载DLL无法立即删除时准确记录延期清理，不把文件占用伪装成彻底删除，也不触碰NAS资产。无签名证书时明确交付未签名预览，不能安装自签信任或绕过系统拦截。

## 验证

V03-015连接会话、V03-016Shell生产化、V03-017安装打包独立worktree；root冻结共享契约、依赖政策与合并。测试覆盖无配置、错误口令/证书、正常连接、退出/换身份/失效、目录分页、坏包/路径逃逸/冲突/回滚/占用、安装和卸载原生读回；真实文件只用隔离合成夹具。正式包只能由最终合入源码构建并核强hash后交付。

官方依据：[Windows App SDK自包含部署](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps)、[Microsoft.WindowsAppSDK NuGet](https://www.nuget.org/packages/Microsoft.WindowsAppSDK)、[Credential protection](https://learn.microsoft.com/en-us/windows/win32/api/dpapi/nf-dpapi-cryptprotectdata)。
# 交付候选修订（2026-09-12）

首个内部整包 `0.3.0-preview.1` 已在实机暴露 WinUI 应用 PRI 缺失和通知助手 COM apartment 错误，不能对外交付。修复后交付号递增为 `0.3.0-preview.2`，其余产品身份、路径规则与协议保持原冻结值。旧候选已注销，关闭测试窗口后仍有 Explorer 对旧 DLL 的占用，按安装器规则记录待清理；不覆盖同版本文件、不结束用户原有 Explorer。新版使用独立版本目录完成验收。
