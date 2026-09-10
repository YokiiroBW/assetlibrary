# V03-006 测试记录

新增[冷启动纯准备](cold-session-preflight.md)：最终C# Add-Type编译和wrapper AST通过，只读inspect记录22同用户/会话/系统image/无debugger；WTS后变4及输入桌面Win32 5，Eligible=false。初始辅助窗/隐藏100%完成态拒绝原件保留；未调用Stop或启动注册，不能算冷启动/产品测试通过。完整工具边界及恢复前置已封存。

新增[双模式属性控制](attribute-pidl-control.md)：严格原生构建、脚本AST/default validate通过，经主协调最终审查后仅一次run。A20+B26查询全S_OK/有效，顺序及前后22B/同SHA读回通过，两个进程各10秒外限均未触发；guard exit0/双通知/8根absent、probe退出确认，GUI0/附加0。46次属性查询不是46个产品测试，G1不计通过。

恢复只读审计：78c0a23内27份属性轮原件的工作区/HEAD字节SHA均一致。调用链发现独立属性正控使用枚举child与28180000输入，host捕获匹配解析child且输入mask不同；尚无同对象同mask的两来源结果。仅补充结论边界及下一项建议，未执行新注册、GUI或附加，也未重跑未变业务套件。

新增[单属性轮](attributes-debug-control.md)：4匹配4返回全S_OK/outputValid，输入20000000/40418000/40000000/2044007F对应输出0/0/0/26；仅call1/4请求FOLDER。约33秒异常保护早停，4返回+1入口断点清零/Detach/存活成功，最终双通知/四根/自有UI清理。未改写mask、调上限或叠加断点，不计完整60秒或G1通过。

新增[完整IID轮](bind-iid-control.md)：同一已审查场景仅增加请求IID输出，2对callId/完整GUID/pbc/HRESULT/null关系已记录；未扩大目标读取或上限。约29.75秒异常保护早停不计完整60秒，两断点清零/Detach/存活无debugger及有效注册期view/模块读回、最终双通知/四根/自有UI清理均完成。无接口身份猜测或关联修改。

新增[单Bind观测](bind-debug-control.md)：实际reference/系统候选校验和ready成立，唯一Browse触发两对callId关联（other/pbcfalse→80070490/null；other/pbctrue→80004002/null）。后续异常保护提前结束，非60秒通过；两断点删除、数量0、Detach、同目标存活无debugger均确认。有效期view仍ThisPC2项，双通知/四根与自有UI清理完成；不重跑或提高上限。

新增[双通知对照](assoc-notify-control.md)：候选hash及最小diff核准；注册/cleanup的UPDATEDIR与ASSOC在同一STA线程各自Invoked/Returned/Joined成功，原字段不变。SDK/ThisPC正控通过，唯一Browse10秒超时/实际无关联模态，有效期view仍ThisPC2项；最终四根与自有UI清理。未附加debugger或重跑旧套件，不计G1通过。

新增[v2真实附加](target-debug-bounded.md)：身份/ready及唯一Browse时序成立；4096总入口上限触发约31秒提前结束，0匹配，passed=false不能写成清理失败。三个断点删除/剩余0/Detach/目标存活无debugger均true；有效期实际view仍ThisPC/2项，最终STA注册清理和自有UI清理完成。未调上限或重跑旧合成/业务测试，G1不计通过。

新增[目标观察器准入拒绝](target-debug-admission.md)：新目标所有权/SDK/ThisPC正控有效，固定749CD509工具在查询debugger状态前置失败，未附加、未ready、未Browse；不计产品激活测试。root只读证实旧handle mask导致Win32 5，另mask查询成功且无debugger。注册/自有UI已清理，旧合成/业务测试不重跑。

新增[完整注册先行控制](register-first-control.md)：原guard/STA成功后，新PID创建时间严格更晚；SDK/ThisPC actualview正控通过，F5仍2项，唯一Browse产生无关联模态并触发10秒外限。有效注册期间目标匹配拒绝、ThisPC匹配/2项、模块无样例均已读回，正常清理及STA通知成功。没有重跑旧父枚举/源码构建/业务套件，不将原中断项算失败或将本次SDK成功算G1通过。

恢复检查点：[父级标准发现查询](parent-discovery-control.md)含/不含hidden均完整3项匹配官方，匹配项属性20000000；另一直接Bind正控匹配官方类。均外部10秒、无GUI、原样STA注册/清理。未重跑这些结果。旧时序GUI控制只完成注册后新PID和SDK正控，未发生ThisPC/目标导航；恢复时清理已匹配的旧窗口，不标失败。

最新官方对照分两轮：[首轮](official-runtime-control.md)确认MTA通知辅助缺陷并单独补成功STA通知，保留其无效前置/晚到读回；[修正前置的完整一轮](official-sta-runtime-control.md)先成功STA通知和11字段读回，再F5/唯一原生打开，仍无关联模态/10秒超时，实际view为ThisPC/2项。原DLL/字段不变，未伪造Factory日志，原失败不覆盖。两轮根键和自有UI均清理，最终清理STA通知成功；没有把控制器或读回成功计为G1通过。

最新：[E0与BrowseObject](active-view-dispatch.md)原生观察器/独立控制器均Release x64 `/W4 /WX /analyze`零告警。两份SDK和两份目标view读回成功；创建时间+1负控在ShellWindows枚举前拒绝。实际目标view均系统FS类/0项，唯一BrowseObject调用S_OK不计入口通过；所有进程外部10秒期限且无超时，原owner guards正常撤销，空目录/自有窗口清理。未改产品/tests源码，未重跑已通过的未变业务套件。

本轮最新：[路径绑定控制](path-bind-comparison.md)2/2完成，独立进程各10秒外部期限、无超时/错误输出，原CLSID根与实际sandbox路径均绑定原类/枚举1项。临时C++探针最终Release x64 `/W4 /WX /analyze`零警告/错误；600秒guard正常退出并卸载，空目录清理，GUI动作0。PIDL尺寸与ILIsEqual原数值保留且解释限制已写明。另[官方研究](official-sample-review.md)锁定revision并校验17份Git blob；官方样例缺源码且未构建，不能计通过。仅本轮归档/交接校验，不重跑未变业务套件。

最新[第五轮用户态捕获](explorer-user-trace-aligned.md)：首次Return13:21:33.466晚于捕获截止；第二次Return13:25:38.586有效对齐，但预填/补全不在范围内。UIA仍空目录，三计划进程无proof DLL/trace；主协调报告只见control、无目标失败事件，不能认定未尝试加载。guard自然到期exit0/两键false，自有窗口目录清理，管理员控制台及原窗口保留。未改代码或重跑既有套件。

此前[四轮ETW准备](explorer-etw-preparations.md)均未提交GUI入口，全部清理，不计新GUI测试；采集器失败或UAC取消由主协调报告。

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
