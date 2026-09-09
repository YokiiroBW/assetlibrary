# V03-006 测试记录

最新[ETW前置准备记录](explorer-etw-preparations.md)：四轮均未提交GUI入口，测试数不增加。原动态DLL hash核对、四轮600s guard均正常exit0、两键false、自有窗口/空目录清理；第一轮自动停止、第二轮UAC取消、第三轮Kernel-Registry PID范围异常及第四轮用户态候选UAC取消均由主协调报告。没有重跑源码/业务测试，不把未发生的导航标为成功或失败。

## 最新Explorer入口检查点（2026-09-09）

追加[静态CRT对照](explorer-mt-control.md)：独立Release构建零警告/错误、实际/MT与导入表核验通过；原probe的独立root-bind一次正控通过，真实Explorer相同目录入口仍失败。没有更换源码/SDK/接口或生产CRT默认；注册和自有窗口/目录均清理。此控制独立计数，不重跑原有矩阵。

主协调复核发现动态/静态两轮API图像payload同字节。现有对象/路径审计未发现变量误用，本轮PNG仍降为相同外观参考，不计独立时序证据；保留本轮不同窗口的独立UI读取与UTC/注册/模块记录。没有为此重跑GUI或造新图，详见screenshot-source-audit.json。

追加的[官方文件夹CLSID入口](explorer-folder-entry.md)只显示普通空目录，0项且无Explorer DLL/trace；注册在观测期间有效。guard正常exit0、finally两键false，自有窗口/目录清理。该真实入口未通过，不混入独立root-bind控制计数；无源码变化，不重复构建。

源码4780629，详见[现场证据](explorer-entry-20260909.md)。实际执行 `cmake --build .runtime/explorer-proof --config Release --target ExplorerProofProbe --parallel 2`，严格MSVC构建通过。相同不可变DLL注册后，以十秒外部期限执行 `ExplorerProofProbe.exe --root-bind`，Parse/Desktop/Bind/View四阶段均S_OK且exit0；卸载后同命令parse=80070057、PIDL absent、exit1且不继续Bind/View。两个控制通过预期断言；stdout无缓冲，空view不得冒充成功。

真实GUI已恢复操作：shell:Desktop正控成功，完整32项可见列表无自有入口；裸CLSID提示找不到，完整shell URI提示没有关联应用。导航及报错期间注册均有效，Explorer模块读取成功且无proof DLL。真实入口仍失败，不计G1通过；此前桌面拒绝访问是历史环境失败。清理确认两注册键false，仅用户原窗口保留。原始UTC/hash/截图/模块读回见证据索引。未重跑输入未变的loader、图片隔离或全仓库套件。

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

## 转交后的Windows图片guard检查点

详见[独立记录](windows-image-guard.md)与[证据索引](windows-image-guard/evidence.json)。合入root57d1578、V03-007 ca1d235的新基线后verify_repository通过（397个C#文件及既有35项回归，Alpha blocked）。仅guard和测试诊断修改后，两个文件format whitespace verify、NativeAOT实际publish、validate_dotnet_source及diff检查通过。生产Ready→PNG→退出/owner清理用例1/1通过，0skip；另有可信native LPAC/普通AC两个对照，四项访问/文件结果各自一致。此为新Windows图片证据，不改原4项loader计数，不将4次诊断失败日志当4个独立产品测试。其余故障、网络/COM旁路、资源与恢复仍待完成。

## 后续已完成的约定原生矩阵

详见[Windows原生矩阵](windows-native-matrix.md)与[完整安全摘要/hash](windows-native/evidence.json)。c6649c5的native-final-matrix为12/12、零skip；其中10项为文件/RX、COM、句柄、Job/CPU/memory/child、取消/父退出和SID恢复，另外2项准确表示LPAC初始化阶段阻断的本机路径。之前预期10013/5的失败不被删掉或等同于最终网络层通过。LAN第二轮6组以非本机dev-230 listener补证，由root确认普通token TCP/HTTP被接收，AC/LPAC无连接；普通AC connect10013与LPAC10107/12004分别记录。第一轮正控因服务器端口不开放而失败，不计隔离证据。

Windows图片MSTest不同逻辑行累计34项：生命周期/ACL/PNG8项、负值与规范数字14项、native12项；另列AAP2个控制与LAN6个控制，不与重复执行或root独立重测累计。最终verify_repository通过（432 C#文件与同一35项已有回归，Alpha blocked），Core/Tests零告警、目标格式和diff通过。更广泛原始network/其他COM/Windows版本及通用Provider门禁仍独立，Explorer实际入口未完成。
