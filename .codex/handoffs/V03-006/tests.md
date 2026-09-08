# V03-006 测试记录

Windows x64；MSVC19.44.35228.0、Windows SDK10.0.26100.0，CMake来自VS2022 BuildTools；Python使用Codex bundled Python。没有新增安装或重型.NET构建。

## 实际命令

```powershell
python -I -B scripts/verify_repository.py
cmake -S tests/windows-shell -B .runtime/explorer-proof -G "Visual Studio 17 2022" -A x64
cmake --build .runtime/explorer-proof --config Release --target ExplorerLoaderProbe --parallel 2
pwsh -NoProfile -File tests/windows-shell/diagnose-loader.ps1 -BuildDirectory .runtime/explorer-proof -DllPath C:/YOKI/Codex/AssetLibrary-worktrees/V03-002/.runtime/explorer-proof/Release/AssetLibraryExplorerProof.dll
dumpbin /dependents /imports C:/YOKI/Codex/AssetLibrary-worktrees/V03-002/.runtime/explorer-proof/Release/AssetLibraryExplorerProof.dll
dumpbin /dependents .runtime/explorer-proof/Release/ExplorerLoaderProbe.exe
pwsh -NoProfile -File tests/windows-shell/registration.ps1 -Action verify
git diff --check
git diff --cached --check
```

实际使用已发现工具的绝对路径，不依赖全局python/cmake/dumpbin PATH。基线verify_repository通过，含21个迁移与14个架构回归及契约/SDK/依赖/源码校验；Alpha有效性审计decision=blocked。未重复运行输入未变的.NET、原COM/DefView注册测试或真实Core fixture。

观察器最终 `/W4 /WX /permissive- /analyze /utf-8` 构建通过。初次C6387指针数组潜在空值警告导致/WX失败，改为编译期二维字符数组后通过；最终将预载检查置于COM初始化之后再构建，保证检查紧邻LoadLibrary。未降低告警等级或关闭分析。

观察器142336 bytes，SHA256 `0526f024ba101fbd73866d6b0bc2ba130b8ea7f55fd77ba9de8c4b1d405775f0`，导入表仅ole32.dll/KERNEL32.dll。被测DLL与V03-002原记录强哈希一致，源未修改。

| 场景 | 结果 |
| --- | --- |
| 默认搜索、继承PATH、空工作目录 | 加载/类工厂/实例成功，动态CRT来自System32 |
| 默认搜索、PATH仅System32 | 同上 |
| DLL目录+System32限制搜索、PATH仅System32 | 同上 |
| 不存在DLL负向控制 | 正确失败：Win32 126、退出码1 |

原始输出时间2026-09-08T11:54:05.5405623Z，见loader-evidence.json。四个独立子进程均有10秒上限，加载前确认动态CRT未预载。负向用例会在加载失败被错误报告为成功时失败。未复制旧DLL相邻的开发工具或添加新依赖。

## 环境与未完成项

同会话/Medium、三个mitigation policy读回和限定时间/条数的事件日志查询记录在environment-evidence.json；它们不计真实G1通过。CodeIntegrity查询截断200条，不能证明无任何阻断事件。没有实际目标进程完整loader/registry trace。

computer-use列举只有Codex可定位；launch_app(Microsoft.Windows.Explorer)返回GetCursorPos 0x80070005。随后只读列表仍无Explorer。该环境场景失败1，不是G1新复现或通过/跳过。停止UI输入，无截图/点击证据；旧窗口不能凭过期句柄关闭。

注册仅执行verify，ClassPresent=false、NamespacePresent=false。诊断进程均退出，原DLL hash不变，副本/trace保留在.runtime/explorer-proof/loader-diagnostics。没有触碰真实资产或NAS。

统计：新矩阵通过4、桌面可操作性场景失败1、跳过0。35个仓库基线回归单列，重复最终仓库检查不累计。G1入口未闭环，G2/G3/G4、20轮/8小时、IPC/断网/Host故障、真实Core联调和发行缺前置证据，均未标为通过。

## 最终收敛

实现和交接完整后最终diff审查/空白校验通过；verify_repository再次通过（交接、架构、契约/SDK、依赖/源码及同一35项回归），Alpha仍blocked。另运行 `python -B tests/architecture/check_release_gates.py --target explorer-v0.5`，按预期退出1，明确列出M0-002-G1..G4阻断。该负向发布检查不是新增业务测试失败，也未改门禁。之后仅记录本检查结果，没有再重复构建或回归。

## 追加只读判断（2026-09-08T12:29:00Z）

按主协调请求查询GetTokenInformation(TokenIsAppContainer)、GetPackageFullName、ProcessIdToSessionId、WTSQuerySessionInformation(WTSConnectState)，以及HKCU/HKLM两视图共16项精确注册值。未枚举其他CLSID、账号信息、用户资产或日志。

六个进程（四个Explorer、当前命令观察器、原哈希加载观察器）的AppContainer均0、包查询均15700/无包，Session2的连接状态均4/Disconnected。所查EnforceShellExtensionSecurity及自有CLSID Approved/Blocked值均不存在。这里只记录API实际观察，不认定G1根因或新增通过的产品测试。

原加载观察器使用CREATE_SUSPENDED|CREATE_NO_WINDOW创建，仅读进程创建时身份，不恢复主线程；TerminateProcess后WaitForSingleObject(3000)确认退出并关闭双句柄。没有运行加载矩阵、加载DLL、发起COM/GUI操作或修改Explorer。原EXE SHA256保持0526f024…，完整原始JSON含退出/哈希证据。

本追加只改交接文件；仓库既有 `python -I -B scripts/validate_handoff.py` 与 `git diff --check` 均通过，不重复输入未变的构建、矩阵或完整仓库回归。
