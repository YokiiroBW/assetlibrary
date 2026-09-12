# V03-005 当前验证记录

## 2026-09-12 G2 实机故障恢复完成

原始归档加最终验证日志共177个摘要在Git索引逐项匹配；局部.gitattributes关闭换行转换，保留原始CRLF/LF字节，解决首次git add规范化造成的索引摘要差异。

最终集成verify_repository通过，原exit_criteria逐条未改，只有G2状态变化；explorer-v0.5返回3且仅列G3/G4。运行时DLL/Host/契约源码与4059e40一致，合入的V03-013仅测试EXE/研究交接。相关构建/回归沿用已审owner结果，不重复无变化成功检查。

07目标22692完成crash/silent错误及真实根恢复；08目标16108补齐invalid40B/partial20B错误与恢复，四份原生错误/恢复均在guard内。G2按原三条关闭，G3/G4仍open。175项归档摘要逐项复核；Application1000/1001/1002区间读取成功且无记录。所有自有窗口、Host/fault/Core与两轮注册清理；138原件hash/mtime不变、6角色回收。07晚于注销的invalid原生失败保留，由08补齐；08清理预留48.479821s未达计划60s，两guard按600s自然到期，不能记为后来stop触发。参见[实机验收](explorer-g2-live/README.md)。

V03-013集成ac9171f只增加可选研究诊断；严格build、两个既有Loading回归通过，但实际33项/合成1项的机制正控失败。该失败不接默认CTest、不标成通过；DLL行为/Host/协议均未改，体验未解决。参见[研究记录](../V03-013/tests.md)。

## 2026-09-12 G2 故障工具集成

最终集成仓库检查通过；explorer-v0.5 返回3且只列G2/G3/G4。四项原exit_criteria与26d7446逐项相同，四个工具源码blob与审查过的d3b2ba3一致，任务登记与现有交接对应。见[集成核验](explorer-gate-review/integration-verification.json)。

V03-012代码d3b2ba3已集成为88e6c21。严格Release构建、两个不同的受影响CTest通过；独立审查要求截断响应必须证明实际发送，修正后故障用例1/1通过并断言20B/40B写入成功。最终silent156ms、partial157ms；这些是独立CLI/client组件时间，不是实际Explorer G3数据。原snapshot已通过且未变，不重复累计。代码不修改Shell/生产Host/协议，只有独立测试进程和CMake/README；复用现有Query/pipe命名/请求校验。见[V03-012交接](../V03-012/tests.md)及[最终故障日志](explorer-gate-review/final-fault-harness.log)。

实机准备因截图两次0x80070057、收尾输入0x80070005而停止；只读WTS查询确认为Session2 WTSDisconnected。未注册/启动管道Host；合成Core清理验证138原件hash/mtime未变、6角色与服务退出。新SDK窗口HWND133536/PID18976关闭未确认，原Explorer6212身份未变且未重启。该阻断不计实机通过，见[准备与恢复计划](explorer-gate-review/g2-acceptance-plan.md)。

## 2026-09-12 原门禁证据裁决

独立审核后关闭 G1，原四项 exit_criteria 未变；两个归档索引共 79 个证据文件 SHA256 逐项匹配。G2 的 COM 卸载历史标签纠正为 G3，保留原始失败。最终 diff 检查和 verify_repository.py 通过（21 迁移、14 架构回归及现有契约/源码检查）；explorer-v0.5 精确返回 3，仅列 G2/G3/G4，按预期继续阻断正式发行。此轮未重复原功能测试、未增加测试通过数。详见 explorer-gate-review/README.md。

## 2026-09-12 Windows真实Core接入完成本切片验收

Host57普通测试、既有2真实Core组件测试、最终呈现状态修复4项受影响CTest、集成严格构建及repository-rendered-final.log仓库检查通过；重复运行不累计。第五轮真实Explorer无需F5/预热自动显示1库、100条+下一页、第二页38条、相册/夏日/文件，并通过Host停止不可用、重启一次负路径F5拒绝旧位置、重开根自动恢复。actual4FF/PIDL/计数/名称与原始截图一致；观测延迟仅上界，不称<10s/G3通过。

原600秒guard自动注销，9字段包外missing、Desktop32/入口消失、所有自有窗口与两个Host结束；原Explorer未重启或强杀。三轮Core支架分别核验138原件hash/mtime不变、6临时账号/数据库/runtime/HTTPS监听及进程清理。参见[最终索引](explorer-host-integration/final-cycle/evidence.json)。Windows CViewSettings仍持有对象的严格proof-owner诊断exit1/S_FALSE保持未通过，安装、预览、主动失效与G2..G4均未关闭。


本阶段是分工/启动，不是新功能验收。共享70ce45c基线已运行python -I -B scripts/verify_repository.py并通过（原迁移21、架构14及现有源/SDK/依赖检查），原日志在主目录.runtime/parallel-browse-baseline.log。此35项仅是基线，不算本批新增功能测试。

4个App创建的工作区均检查git-common-dir与本仓库一致，初始HEAD70ce45c/无改动，然后建立独立codex/v03-006..009分支，生成各自任务/交接。真实thread ID通过read_thread核实。新窗口默认权限造成命令审批等待，已向用户说明；不能把waitingOnApproval说成已实施。

本轮修改限规划、任务图/注册表/状态和交接；没有应用/核心/wire/数据库/依赖变更，不重复既有业务测试。规划元数据在提交前执行既有handoff/架构校验。实际功能、平台、权限/源安全、性能和联调测试在各窗口实现稳定后由V03-005集中汇总和复核。

统一预览验收的10个合成输入已准备在本协调worktree的.runtime/sandbox-storage/V03-005/preview-fixtures，覆盖JPEG/PNG/WebP、EXIF方向、透明、相同内容不同文件名、中文路径和损坏/超大头/SVG拒绝；manifest记录源hash/mtime。仅为待执行输入，不计预览通过，没有使用个人资产。

## 共享真实服务测试入口扩展

新增显式 `--image-fixtures` 与 `--image-preview-worker`。默认138文件、现有连接JSON、真实认证与清理路径保留；图片复制进同库的图片样例目录，参与原件hash/mtime核验。没有替换decoder或跳过生产隔离的测试模式。

稳定修改审查后执行现有入口（精确SDK10.0.111、Python3.12；均在V03-005私有worktree/cache）：

```text
python -I -B -m unittest discover -s tests/integration/native-clients -p test_serve.py -v
dotnet restore tests/dotnet/AssetLibrary.WebGateway.Tests/AssetLibrary.WebGateway.Tests.csproj --locked-mode
dotnet format tests/dotnet/AssetLibrary.WebGateway.Tests/AssetLibrary.WebGateway.Tests.csproj --verify-no-changes --no-restore
dotnet build tests/dotnet/AssetLibrary.WebGateway.Tests/AssetLibrary.WebGateway.Tests.csproj --configuration Release --no-restore
dotnet test tests/dotnet/AssetLibrary.WebGateway.Tests/AssetLibrary.WebGateway.Tests.csproj --configuration Release --no-build --no-restore --filter FullyQualifiedName~NativeClientTlsFixtureTests|FullyQualifiedName~NativeClientImageFixtureTests
```

Python 8项通过，.NET 6项通过，均0失败/跳过；其中新增图片输入验证3+3项，旧生命周期/TLS用例5+3项。复制路径/manifest/hash拒绝、旧样例数量不变、中文嵌套名与变更后原件核验失败均有实际断言。首次构建因MSTEST0037断言表达规范失败2处，按分析器改为AreEqual后format/Release零警告零错误通过；没有关闭规则。

该证据只证明真实服务测试入口的可构建性和输入边界，真实Core图片请求、隔离decoder和最终清理联调尚待执行，不计为图片功能通过。

## 集成检查点与真实图片样例生命周期

3890793在合入客户端/Windows诊断及同步实际状态后，通过 `python -I -B scripts/verify_repository.py`：handoff/模块架构/生成SDK/主题/源/依赖均有效，21迁移与14架构回归通过，Alpha依旧blocked。四个模块目录的Git tree与各自审查提交相同，见integration-checkpoint.json；原窗口65个Web、77个Android用例单列，不重复累计或重跑。Android候选APK已复制至本worktree.runtime/releases/native-clients，重新计算SHA256与原窗口一致，未替换旧包或宣称真实预览通过。

随后实际运行README的真实服务器入口，增加 `--image-fixtures .runtime/sandbox-storage/V03-005/preview-fixtures --lifetime-seconds 2`，未设置worker：真实PG16.15、21迁移、6个LOGIN、HTTPS及首次扫描成功，文件数148，READY后按期限退出。原hash/mtime保持不变，Host/PG退出、HTTPS端口关闭、6个LOGIN移除、私密runtime删除均verified，非敏感原始结果见image-fixture-lifecycle.json。这是一个真实新增图片样例生命周期用例，不是图片端点或客户端图片显示验收。

这10项输入随后按manifest强hash核对并逐字节复制进 `tests/integration/native-clients/fixtures/image-preview-v1`，方便其他worktree和CI复用；没有引入Pillow运行时依赖或修改图片内容。

## Linux 安全读取基础

f580b15经代码审查后合入，4b87896将Preview.Tests加入既有solution/Windows+Ubuntu CI矩阵和依赖缓存，不新增测试层级。该不可变集成源码tar为34,693,120B，SHA256 94b72b1d7fe25d42143df2b5e1729ccee4e5de341218ed7fb8e2f76945252919；传至dev-230自有临时目录后重新验证，再以安全tar过滤解包，未写远端工作仓库或NAS210生产。

复用已核实10.0.111 SDK镜像，仅在自有构建镜像添加clang/zlib开发包。测试容器非root、2CPU/3GiB/512进程限制、去全部capability和no-new-privileges，仅挂本任务源码；执行Preview.Tests的locked restore、format verify、Release build和StableImageSourceTests，全部通过，0warning/0error、9/9测试、0skip。原始TRX逐项读回含真实叶文件symlink拒绝；JSON索引、hash与工具镜像来源见linux-source-boundary.json，原始日志/TRX在本worktree.runtime/linux-source-evidence。

结束读回自有容器不存在，测试sandbox子目录为0。构建镜像和自有临时源码保留供后续NativeAOT测试；没有启动decoder、修改NAS、或用Docker外层拒绝冒充worker自身隔离。SDK首次提示workload验证诊断，但实际restore/format/Release和测试通过；未因此更新全局workload或忽略警告。

审查另发现数据库timestamptz与文件100ns时间精度潜在不一致，已交V03-007在实际索引接线前复现并修正；上述9项只覆盖物理读取基础，不能替代持久索引/解码/HTTP联调。

## 独立Linux解码平台验证进行中

6af1486与44ab45c分别按不可变tar强hash传入同一自有开发环境，均完成实际NativeAOT编译。44ab使用独立RID锁，restore显式传 `-p:RuntimeIdentifier=linux-x64` 加 `--locked-mode`，之后publish `--no-restore`；普通restore的 `--runtime` 设置复数属性导致首次误选普通lock/NU1004，纠正命令后通过，未解锁或改依赖。DebugType=None/DebugSymbols=false使44ab产物仅含2,182,152B可执行文件、11,756,440B Skia库和精确MIT/完整native notices。

实际直接运行在dev-230非root用户、父进程cap0/seccomp0下，没有Docker外层隔离替代。6af因Console.Dup/懒初始化与fd白名单冲突，Ready前SIGABRT；精确runtime源码与安全stderr互证，44ab改固定fd后已能Ready并读完输入。44ab的默认GC128MiB使VmSize752996KiB超过AS512MiB，8.6MiB位图mmap实际ENOMEM，图片返回Unavailable，仍不能启用。

同44ab的隔离probe实际从seccomp0进入2/NNP1，文件、创建socket、预存socket连接、跨进程信号/内存、fork、非线程clone、exec、io_uring均拒绝；io_uring前置EFAULT与后置EPERM区分外层策略。CPU探针由内核在约3.001秒SIGKILL，非parent timeout。native内存/线程探针虽报告拒绝，但在已过量保留VM下的失败不能单独证明目标预算；未把它们当最终通过。

只降低子进程GC至64MiB的单变量诊断使Ready VmSize424728KiB，10个合成正常/异常样例全部符合预期，方向/尺寸、相同JPEG改名派生hash一致，透明图corner alpha0/center160，派生图已实看。最初Pillow.info仅显示sRGB，不能据此断言完整PNG块清单；后续真实HTTP解析确认输出还包含固定8位sBIT，已补严格允许与变异拒绝回归。该覆盖用于指导owner固化预算，不能代替无环境覆盖的正式配置验收。原始文件索引与摘要见linux-decoder-progress.json；默认配置的后续结果见下节。

## 默认Linux引擎已完成的边界（40d2d69）

40d2d69固化64MiB GC与进入前VM检查，使用提交的独立RID锁做实际locked restore/NativeAOT publish。**未设置任何GC环境覆盖**，仍在非root、父进程无cap/seccomp且Docker外执行：Ready VmSize424736KiB，AS512MiB/CPU3秒/NPROC256/FSIZE0/CORE0读回；10项corpus全部符合真实派生或拒绝预期。标准Png/Jpeg/Webp、方向6、透明和改名一致性均通过，结果字节与前述诊断匹配。

隔离、768MiB native分配拒绝和3.001秒CPU SIGKILL全部通过。UID1000已有492线程时thread probe创建0，有限trace证实栈分配成功、clone3按设计回退、thread clone因UID NPROC返回EAGAIN。为验证正向边界，同发布字节复制到仅含程序/库/声明的自有临时目录，使用现有nobody身份（初始0线程），实际创建253线程后封顶、全部join、exit0/空stderr；身份线程回到0且目录完整移除，没有创建账号或改系统策略。

另用任务自有中间父进程验证parent-death：实际worker Ready后SIGKILL中间父进程，外层仍保留stdin写端排除EOF自行退出，worker在11ms内收到SIGKILL并由任务subreaper回收，未由外层主动杀decoder。独立子进程与无新权限边界均保持。

上述统计为15个独立默认配置场景（10输入、4隔离/资源、1父退出），不累计诊断/重复。详细结果、原始限额/输出和精确包hash见linux-decoder-default.json。它们是Linux6.14开发机证据；NAS5.10.55、真实Core端点、Web/Android跨端与Windows仍分别待验收，不关闭通用Provider或版本门禁。

## 2026-09-09真实Core与客户端集成检查点

实际Linux Core874fb6a、默认Worker40d2d69、PostgreSQL16.15和148个合成文件已在dev-230独立临时checkout运行。LiveImageEndpointTests设置PREVIEW_EXPECT_AVAILABLE=1后2/2、0skip，通过真实PNG/JPEG/WebP/方向/透明及损坏/超限、身份/请求边界；原始TRX复制至.runtime/linux-real-core-evidence/live-linux-images.trx。已修复fixture在Linux创建配置文件时依赖umask的问题，显式0600，不降低产品私密文件校验。

Android1838b96在同一服务上手机2/2、平板图片1/1通过（两个不同用例，不按视口重复累计），记录已合入11a9167；root逐张实看4截图，并重新核对两份JUnit原始SHA及候选APK8461631c…6158b。设备私密文件、reverse、模拟器及5584/5585端口均已由原窗口核验清理。

Web真实联调暴露已挂载虚拟列表方向键改变selection却未更新DOM焦点的问题；77271e0以单点effect依赖修正，新回归先失败后通过，并连同QuickLook两视口/多选共4/4通过。root审查合为f116f48，核新JS302093B/SHA b248f3ac…2866ea和CSS27768B/SHA c4b01b9d…f4a1。归档9d1316c7…578f06重新验hash后，仅切换临时Core的Web目录，旧d944产物保留；Core/PG/资产未变。77271e0真实运行已通过16项图片/错误检查，QuickLook后续交互仍待独立接续，原failed顶层receipt不改写、不冒充整项通过。

协调分支ad5d316的整solution格式、Release构建0警告0错误；完整dotnet test为340通过/0失败/34 NotExecuted。后续提供真实Host/Python后补source broker1/1、原生扫描取消1/1、六种故障回收6/6，均0skip；与完整suite相同测试不重复累计。新增de83嵌套迟到清理修复合为433ad01，2/2回归通过。原始TRX和按实际UnitTestResult outcome汇总在.runtime/v03-005-final-tests；不可将MSTest缺失的counter字段当0跳过。

a21a145 Windows生命周期源与a497权限测试链接按顺序合为fa45ce2/2e29fc2，StartAsync共享适配25a7bed合为7c8b58a。Windows窗口6/6与后端组合21/21+真实HTTPS2/2均通过；root独立复验9项为4通过/5失败：失败全在新目录首次设置Owner+DACL，未创建AppContainer或启动decoder。root父目录仅提供当前普通token Modify，原窗口父目录另有显式用户FullControl；同一root私有新对象先设严格DACL、再设Owner均成功，已交Windows单写修复此兼容性边界，未改workspace ACL或降低保护。自有探针和测试目录已清理；这5个真实失败保留，待修复复验。

当前共享服务尚在供Web后续验收使用，最终原件hash/mtime及Host/PG/临时角色/私密目录清理待root显式stop后记录。不得提前把客户端字段中的协调清理责任当作已经完成的事实。NAS包缺独立worker已查明，f673a12限定授权V03-007接入NAS镜像/离线包，缺省不开启，实际目标内核验收及部署仍由root负责。

## 2026-09-09共同验收完成及NAS目标阻断

上述共享生命周期随后显式stop并最终通过，原始real-core-cleanup.json确认148源hash/mtime不变、6角色移除、Host/PG/HTTPS/runtime清理；Web绑定原16项与最终interaction continuation两份原始SHA，Space/Escape、回焦、历史、404/401、URL和CSP均通过。所有浏览器/转发已关闭，root独立确认44147无监听。两个客户端scope已在注册表验收完成，整批仍partial。

Root带明确Host/Python/原生Worker的最新整solution格式、Release零告警，360通过/0失败/25明确PG或平台NotExecuted，TRX汇总见.runtime/v03-005-final-tests/solution-with-native-summary.json。随后新增已删除profile恢复1/1、负/非规范creation与合法边界14/14通过，未再重复整套；NAS失败不被这些通过抵消。

NAS打包d18首次0f4d0c6在NativeAOT发布遇NETSDK1112；00b03b1统一restore/publish的SelfContained后，同提交a12b0d1实际构建成功，Linux包测试12/12零skip。783892992B images.tar SHA5951b75d46b46c14124885bb71f0cdf7b1c4b3713d495d312861085836bf0626；core image2dd1f17c…、setup5c3bee00…、PG5f71c21b…均写入原始nas-image-build.json。传NAS后7项交付文件逐个SHA复核，载入三image并查identity；没有替换生产服务。

同一实际包在开发Linux6.14的20项corpus/错误/隔离/资源场景全部通过，source unchanged、cleanup verified，原始linux-package-worker.json。这里Docker外层seccomp存在，io_uring前后均EPERM不能单独证明内层新拒绝；先前40d2d69在Docker外的独立策略证据继续保留，两者不混算。包中同Worker字节的父退出验证使用setup镜像Python，保留stdin写端、只杀middle、收养并回收worker SIGKILL，10.385ms；linux-package-parent-death.json及逐字节临时helper归档parent-death-probe.py.txt。此结果仍是Linux6.14，不是NAS。

NAS5.10.55+第一image-0-0在Ready前受控失败（status7、零stderr、未发图、非parent timeout），其余19项未执行，所有自有容器回收、源hash/mtime不变，见nas-worker-first-failure.json。宿主只读查询PR_GET_SECCOMP=-1/EINVAL22，seccomp GET_ACTION_AVAIL=-1/ENOSYS38，proc/status无Seccomp，Docker仅apparmor；同策略临时容器cap0/NNP/AS/CPU/FSIZE/CORE/NPROC均成功，但TSYNC仍ENOSYS。禁止据此放宽隔离或启用图片。nas-kernel-capability.json与nas-platform-decision.md记录证据及待用户确认的部署方向。

Windows已恢复过真实桌面并收到用户明确恢复授权，随后在新鲜Ctrl+L前遇0x80070005，2026-09-08T17:16:27Z又读到WTSDisconnected。授权保留，但要保持RDP连接/解锁才能继续；已撤销本轮注册且两键false，不盲重复。无GUI边界测试仍在推进，普通token/普通AC/LPAC的LAN对照待精确端点回执；Windows整体和Explorer不宣告完成。

Windows稳定c6649c5随后已合为249fed9：正creation损坏时，仍须核验实际进程的精确AppContainer SID，不能只凭时间不符就把活Worker视为已退出。Root审查生产diff及相关测试，affected format/Release零告警；以原窗口已核hash的可信native probe DADE2F45F34DB1C271BE6BC8EB756DA4D830D68D20FE0E1B2BBA00E6CDD272EC实际执行新身份回归1/1通过，101ms、0skip。后续仓库/架构/契约验证通过；未重复完整360项。

原窗口稳定native矩阵12/12通过，范围包括宿主marker/RX、非stdio事件、FSO/BITS COM、真实内存、3秒CPU配置与周期性overshoot、子进程拒绝、取消、持stdin的父退出和身份；网络用例准确记为LPAC初始化阻断，不能改称直接connect EACCES。另一次LAN6对照在root短时端点完成：普通token TCP0/HTTP成功，普通AC solelpacCom且零network的connect10013/WinHTTP12029，LPAC WSAStartup10107/WinHTTP12004。Root服务端仅见自己先验1连接和普通token的2连接，无AC/LPAC连接，零源过滤拒绝，端点已关闭。windows-lan-control.json保存原始时刻与清理。第一轮59249被既有防火墙挡住的正控失败保留；第二轮选既有允许区间内空闲39420，没有修改防火墙。所有这些证据不代替真实Explorer入口或通用Provider门禁。

## RDP恢复后的Explorer入口对照

4780629/集成0f56068增加独立`--root-bind`，不预先CoCreate；owner以既有MSVC严格选项构建成功。root读回2026-09-09T01:49:06Z两份原始输出：注册态四阶段全S_OK/exit0，注销态RootParse80070057、PIDL absent/exit1，stderr均空。两种注册状态符合预期；没有重跑无变化的整solution，也不计为真实Explorer G1。

02:00:26Z root只读采样见explorer-process-context.json与对应ps1.txt，全部查询成功。当前executor和5个Explorer同用户/Session2/native x64，IL8192、Limited/elevated0；signature/extension-point/image-load flags全部0。两种registry view的HKCU/HKLM仅本CLSID Approved/Blocked及EnforceShellExtensionSecurity均未配置。它只排除这一组可观察差异，不是所有策略或缓存的证明。首版诊断给TokenElevation传4096字节收到ERROR_BAD_LENGTH24，改精确4字节后成功；首稿输出仍在.runtime/v03-005-final-tests/explorer-context-initial.json，未将失败查询计为成功。整个采样不操作GUI、不修改注册表或进程。

## 三类真实入口与归档复核

真实文件夹CLSID入口1969d0f合入0672dde，README72e7621合入5629738；截图显示普通空目录，非扩展视图。原注册/窗口/空目录已由owner清理并记录；root独立验两目录19项原SHA同时匹配working和HEAD blob。最初trace因自动CRLF转换不匹配，2e21ecf使该子目录按原始字节存档，CRLF作为合法行尾且保留其它空白检查；显式renormalize后已核实Git blob。未修改receipt字段来迎合错误hash。

现有系统日志、进程运行库和工具权限的只读结果见explorer-diagnostics.json；无本扩展的相关记录不等于未尝试加载。未启动driver/ETW、未提权或改策略。静态CRT后续结果单列于下段，不将诊断计划记为通过。上一稳定源/协调检查点c46aea5的verify_repository通过，记录repository-explorer-controls.log；未为后续纯证据更新重跑无变化的业务/图片套件。

静态CRT对照b9cdf5e及图像来源限定4240ae3已合8d7dcea/163da1b。root审查唯一RuntimeLibrary差异、导入表和源码规范化比较；20项新receipt与Git blob、原/新两份实际DLL强hash一致，三目录合计39项。独立根绑定1次通过、真实GUI未激活，现场清理已确认。两轮PNG完全同SHA；owner只读核对为不同WindowState/不同UI tree但API图像payload相同，已明确作为外观参考，不以图片证明独立采集时间。默认CMake与生产源没有变动，不再做DLL/注册试探。

限时ETW工具仅在.runtime中准备：零告警Release，49项ABI/12项合成解析与后续9项守护身份检查；root核对19项manifest和实际只读进程计划通过，两项proof注册当前均不存在。没有执行StartTrace、guardian、UAC或终止。真实事件筛选、投递、清理仍待授权实测，不能将这些预检计为Explorer通过。详见explorer-trace-preparation.json。

## 用户授权后的首次实际ETW准备与取消

原d5工具首次实际Start/Open/三个Enable返回0，Kernel-Registry正控104→0并ready；之后PID scope guard失败，自动Stop/Query无丢事件、guardian正常。root独立status4201、两进程退出；未发GUI导航。来源未记录，不能归因provider。新ae1只补7个安全头字段、显式Compile根目录排除历史源；0告警构建、2项不解引用无效payload指针/输出字段检查通过，原19项完整冻结。第二次UAC启动返回系统取消，无采集器日志，独立status4201；Windows两轮自有窗口/目录/注册清理由owner记录并由root核对10项Git blob/文件hash。原始及索引见explorer-first-live-trace、explorer-second-launch、V03-006/explorer-etw-preparations。没有增加G1通过或失败次数，也没有将取消当作第二次捕获测试。

本次集中归档后的verify_repository再次通过（handoff/架构/契约及原35项回归；Alpha仍blocked），日志.runtime/v03-005-final-tests/repository-authorized-trace-checkpoint.log。没有重复无变化的业务或图片套件。

## 继续后的内核筛选实测与用户态候选

ae1的bc7c7ebf原生启用和注册正控成功，Kernel-Registry event4/version0/headerPid10716不在目标集合，scope guard在读取payload前停止；root独立status4201及两进程退出。确定为Registry来源，不采用image归属猜测；原始四文件已归档。公开契约无所需内核PID过滤入口，停止该配置。新37e984dd仅User-Loader/COM/私有control，零告警、61 ABI/17解析和metadata/2安全头部检查；root审查并核对20项manifest，原ae1不变。新工具UAC启动取消，无实机捕获，独立status4201。Windows四轮准备均0次入口提交、全部清理，20项原件/Git blob hash通过；不增加G1测试通过/失败计数。

## 一次确认复用实测（2026-09-09）

固定runner dca9c53b通过Release零告警、14项离线检查，root独立核对13项manifest；复用37e984dd及原guardian，没有重跑未变更的产品套件。同一管理员会话完成两个60秒捕获和状态请求，无再次UAC；初始非标准GUID请求被拒绝但会话继续，随后标准请求成功。两轮无丢事件、schema或越范围事件，仅收到私有control；停止后runner和root分别确认status4201及采集/guardian退出。首次Return在捕获外，第二次最终Return在捕获内，预填除外。真实Explorer仍空目录，不能将诊断运行成功计为G1通过。Windows第五轮11项原件/git blob SHA一致后合入；guard600秒自然到期清理，稍后的stop不是触发原因，自有窗口目录清理、原窗口及管理员控制台保留。会话自身到期是后续状态，不预先记为已验证。索引见single-consent-session/evidence.json。

本轮最终diff审查后verify_repository通过：handoff、架构、契约、35项迁移/架构回归与源码有效性门禁；Alpha仍blocked。日志.runtime/v03-005-final-tests/repository-single-consent-checkpoint.log。28项本轮原始receipt的工作树/Git index SHA一致，暂存文件未包含活跃会话nonce；没有重跑未变更的产品套件。

## Windows优先的官方研究与相同入口对照

本轮仅新增两个有区分力的独立绑定场景：同一注册/原DLL，GUID根和真实sandbox绝对路径均Parse/Bind/GetClassID/GetCurFolder成功、在Bind阶段加载匹配DLL、各枚举1项，两个进程exit0、无stderr/timeout。root审查PathBind.cpp、固定hash外部10秒脚本和原始输出，不只依赖返回码。未执行真实Explorer导航或官方样例注册；原注册/空目录已清理，不计G1通过。PIDL长度采样差异保留原值，未宣称规范化。另核对12项精确隐藏键值与既有8036/3077/3033时段，无自有值或事件；CiTool列表80070005为缺证据。未修改或重复运行既有产品套件。研究依据见windows-entry-research/report.md。

本轮最终diff审查后verify_repository通过（handoff/架构/契约及35项迁移/架构回归，Alpha仍blocked），日志.runtime/v03-005-final-tests/repository-windows-official-research.log。17份已取官方源的长度/SHA256/Git blob与17项归档原件SHA分别核对一致；初次自写校验脚本误把证据索引当作官方源码，改用各自artifacts清单后通过，未更改原件或期望hash。

## 执行与额度中断后的恢复检查点

整合Windows f7958b0：E0/原生BrowseObject、官方原通知失败轮及正确STA完整控制、父级含/不含hidden查询原件共106项immutable SHA核对一致。客户端入口仍未成功；直接Browse返回S_OK不计通过，模态阻塞控制器没有返回HRESULT。注册后新进程旧轮只完成SDK正控，目标步骤因额度中断未发生；原guard自动清理，恢复后旧窗口按新鲜身份关闭。

root补齐官方Category.cpp并校验全18 Git blobs，原样源码经外部CMake/原.def/x64 /MT /W4 /WX构建、4导出与F298 DLL强hash通过；限制DLL目录/System32的独立加载、factory/正确类ID和卸载条件均S_OK。合成DbgEng最终normal/cancel两场景均成功，18文件manifest独立核对；断点清除/Detach/同目标存活及无debugger/合作退出有实际记录。未附加Explorer，Attach/Detach硬期限及异常崩溃清理没有保证。早期失败保留，相关源码/命令/原件见各证据目录。没有重跑未变的业务套件或解除生产Shell门禁。

恢复检查点最终diff与verify_repository通过，日志.runtime/v03-005-final-tests/repository-explorer-execution-checkpoint.log；原始构建日志末尾空行保留，使用该文件的whitespace属性容纳原件，没有改hash来掩盖字节变化。当前注册先行GUI对照仍在执行，此检查点不提前记录其结果。

## 注册先行完整对照集成

Windows e657813集成为1915b88；18份新原件SHA独立核对一致，纳入目录属性后diff --check通过。注册/进程创建顺序、SDK与ThisPC正控、唯一Browse超时且无返回HRESULT、仍ThisPC2项、模块未见及清理均保留原始结果。只读Folder关联记录归档，无HKCU Folder打开覆盖；没有修改注册关联。未重复旧成功构建/业务测试，未将诊断计作G1通过。

本检查点最终diff审查后verify_repository通过，日志.runtime/v03-005-final-tests/repository-ordering-checkpoint.log；handoff/架构/契约与35项迁移及架构回归通过，发布门禁不变。

## 受限目标激活观察器准备

Observer749cd509…与manifest21fa0b68…冻结，16文件长度/SHA独立核对，非二进制原件及完整诊断源归档target-debug-control。normal-final/cancel-final各3入口、3实际80040154返回及context1；线程/返回栈配对、6断点移除/剩余0、Detach、心跳与内部/外部无debugger、合作退出通过，目标自身first-chance处理执行。19项纯计划/ID/窗口谓词检查与合成进程精确创建时间正反检查有明确范围；未当作真实GUI身份验收。早期两次工具失败保留，未计产品失败。实际Explorer观测由单一owner执行，本准备检查点尚无真实结果。

准备检查点最终diff与verify_repository通过，日志.runtime/v03-005-final-tests/repository-target-observer-preparation.log，发布门禁不变。

## v2查询权限修复与真实有界观察

同PID17512/创建时间的只读句柄对照证明旧00101000查询错误5、新00100400成功且无debugger；旧准入失败18份原件经核hash合入7160f2b，没有附加/Browse，默认false字段不代表目标死亡。v2仅改查询权限及明确错误，新12文件manifest和两条final原件独立核对；每次先复现旧mask，再关闭全权限creation句柄，以实际mask重新打开目标完成3API/异常处理/正常或取消/断点清零/Detach/无debugger与合作退出。

Windows05b8eba集成为00272f1，27份原件hash一致。实际v2 ready249636937→Browse249637375→上限249667781，30.844秒而非完整60秒，4096非匹配、官方0匹配；外部Browse10秒超时无返回HRESULT。真实模态文本读回为03:27:14Z、晚于捕获结束，不推断其首次出现时刻。3断点移除/剩余0/Detach/同目标存活无debugger均成功；注册有效期内ThisPC2/官方不匹配和后置模块快照保留。guard、4CU/4LM检查及自有窗口/模态清理已验证，原用户窗口/Chrome保留。未增加计数上限或重跑原场景，未把0匹配等同完全无激活。

本集成检查点最终diff与verify_repository通过，日志.runtime/v03-005-final-tests/repository-real-com-capture.log；架构/契约/交接及35项迁移和架构回归通过，发布门禁不变。

## 父绑定、完整IID与实际属性分类

归档/审查的准备清单：Bind locator6、Bind observer19、IID metadata observer14、attributes locator7、attributes observer14项分别核对长度及SHA，保留早期与最终版本身份。两轮独立静态审查覆盖ABI、opaque cb边界、模块/RVA指纹、callId、cidl1/R9 DWORD掩码、输出有效性与v2清理复用；callId缺口已修复并复核。参考normal/cancel只证明新自有参考目标，不代表host；失败/null与多匹配交错的覆盖范围按各README区分。

实际官方双通知控制6f1fc78合入b60230f，21原件核对；加入ASSOC仍未修复且全部清理。Bind828cd73与IIDb2b719c分别合入a835739/9fa54dc，各27原件核对；后者完整GUID映射到SDK propsys.h:3765/516的IPropertyStoreFactory/IPropertyStore，实际80070490/null和80004002/null是属性请求失败，未确定因果。属性78c0a23及输入审计50a2a96合入89161fa/c61433e，27原件不变：四组输入20000000/40418000/40000000/2044007F，输出0/0/0/26，均S_OK；只有第一、第四明确请求FOLDER，结果缺位。33.031秒保护早停与5断点清零/Detach/存活无debugger、四根/双通知/自有UI清理分开记录。

旧独立28180000查询作用于enum child，并未证明其与parsed child逐字节相同；当前不能把差异单归因进程/策略，也未改属性位、系统关联或原窗口。新的同源同mask/枚举前后对照仍在准备。当前命令观察器重新查询包状态为15700/无包，仅补当前进程事实，不扩大为所有进程或所有虚拟化机制排除。

本分类检查点最终diff审查及verify_repository通过，日志.runtime/v03-005-final-tests/repository-classification-checkpoint.log；原始patch上下文空格保留，使用单独.patch whitespace属性，不改原件hash。

## 同条件双模式完成与冷启动范围

a118fbe合入1d00321，25份原件SHA独立核对。A20/B26共46query、两来源22B与完整hash完全相等，查询前后不变；B在枚举前已稳定返回FOLDER，后续枚举与双来源双顺序未改变5组输出。全部HRESULT成功、无超时/显式sample Bind/sample模块载入；guard/Owner/期限与最终8根absent、双通知、独立probe退出验证完整。当前可确认所测试来源/mask/先后变量未解释host差异，不能直接指定缓存/策略为根因。

冷启动方案仅计划、尚未执行。只读快照列出当前会话系统Explorer进程，包括原进程及窗口关闭后仍驻留的历史测试进程；不把先前UI清理记作所有Explorer进程退出，不据历史PID推断当前独占所有权。用户确认前不得停止原Explorer；实际操作必须再核当前用户/会话/创建时间/系统路径/调试与文件操作状态。

最终同条件比较检查点diff审查及verify_repository通过，日志.runtime/v03-005-final-tests/repository-controlled-comparison.log；此前已通过且未变的业务套件未重跑，发布门禁保持原状。

用户在冷启动问题后回复做吧；已记录前后两次当前用户Explorer重启的扩展授权。工具准备/身份核验仍是未执行状态，不复用旧PID、不计入验收通过。

冷启动准备232d753合入df6e41f，8项归档SHA及5项原件/脱敏副本映射核对一致；源码与wrapper经root/独立两轮静态复核，修复中途失败的退出确认，并保留逐目标活动会话/Default桌面门槛。仅编译、AST和只读inspect；最后WTS4/输入桌面错误5，无注册、无stop、无GUI输入。授权已获，等待重连不是等待重新授权。

最终冷启动准备集成diff与verify_repository通过，日志.runtime/v03-005-final-tests/repository-cold-preparation.log。没有将断开会话下的只读准备计作重启或实机通过。

## 已授权的两次Explorer重启实际结果

3474209合入3d3dbd1；48项归档及原件/脱敏映射独立核对。新增顶部细条分类的Add-Type编译和27项纯分类验证通过，另有20项仅工具内存执行的独立复核，不计作GUI案例。

首次22、清理后2个固定身份目标全部确认退出；两代自动恢复的桌面38388/32044各两次创建时间一致、无debugger。唯一Browse在观察器ready后406ms提交，SDK17项/此电脑2项正控通过，但入口缺失、无关联且实际视图仍为此电脑。四组掩码返回0/0/0/26，均S_OK；28.485秒保护退出不是完整60秒成功。5断点清零、detach/目标存活、窗口/8注册根清理、双通知及最终桌面无测试模块均有记录。停止helper身份未单独落盘，先后依赖同步工具完成记录与后续独立快照。

本轮缩小了对长期Explorer进程状态的怀疑范围，不宣称所有共享缓存重置或具体根因确定；无生产代码、依赖、契约及发布门禁变化。最终集成验证日志：.runtime/v03-005-final-tests/repository-cold-execution.log。

## 固定属性消费点与同周期差异

离线匹配Microsoft PDB和本机DLL，0x1194C3字段/固定帧关系经独立复核。root归档16件离线分析，v1/v2候选13/12件，完整二进制留runtime。d2900bb/5c32a1b/9234cc4分别合入72e9179/e2ac218/90e26ad；源审3件和reader5件含原始映射核对通过。reader20项missing-key为实际负控，未冒称全部值类型分支都已测试。

首次参考e169e51合入56bca99，33归档/32来源/5脱敏映射核对：两合法嵌套cache与return正确，exact1计数断言错误导致passed=false；已清理、未cancel/real。V2仅修计数条件及固定路径，38纯检查与独立review通过。3ec6f1f合入29078b5，59归档/58来源/7脱敏映射核对；normal/cancel各2逐call完整配对、cache flags1/attrsA0000020/callFor0/restricted0、S_OK/20000000。real4逐call缓存四字段全0；前后reader20实际类型和值相同，HKCU/readerHKCR均A0000020；最终20missing。ready领先唯一Browse约429ms，59.438秒保护结束非完整60秒；6BP清零、detach、目标健康、UI/8根双通知清理、用户窗保留经独立审查。

下一公开RegOpen候选39纯检查及normal/cancel缺失键参考通过，原生成功路径与真实SHCORE IAT尚待owner验证。该阶段不将API准备当实机根因证据。

## 精确开键观察、异常预算与short执行

公开入口由本地APIset及advapi thunk定位KernelBase RVA2BBF0，固定SHCORE IAT在真实目标核验通过。原26件manifest、缺失键normal/cancel均独立核对；registered正控也通过。b37eb4e合入7f64135，60归档/59来源逐字节核对：5.609秒旧首机会门槛在触发前结束，0match不是键缺失。原通知literal仅编译已核，没有增加或修改系统策略。

异常预算V2的19件manifest与三原始参考独立核对：50纯、normal/cancel32真实C++ targetcatch/total33/详16，status2与清理通过；超限trigger257/final258仍failed/exit1、cleanup和handler257通过。随后registered32CPP正控通过；dcbb48a合入0d9e84f，45归档/45来源核对，登记到期在attach前拒绝，未伪造实机通过。

short-key-open归档60件（43原字节/17明确脱敏），root重核75个来源含排除/重复源。第一次18行plan附加前拒绝；修正生成器320B样例与10负例后第二次11行321B已执行ready/原通知/首次ThisPC。other阈值40080201在25.813秒触发（81=64+17），26.797秒结果、cleanup总177=132+45、second0；0match/3896discard。nativeThisPC为3项、在capture结果后15.05ms启动，不宣称完整导航覆盖；Browse未dispatch且独立拒绝回执未保存，限制原样记录。两cycle前后20字段匹配、清理后20missing/8根/双通知、自有窗关闭均核对。SDK roerrorapi.h294明确此码为EXCEPTION_RO_ORIGINATEERROR，未记录其payload或caller，不作根因推断。

最终仓库验证日志：.runtime/v03-005-final-tests/repository-key-open-final.log。没有重跑未改产品套件或关闭G1..G4。

## 2026-09-11 文献优先对照

复核微软官方机制、同ExplorerDataProvider的Q&A亲历案例、失败缓存原始复现及官方仓库#349/PR#332的适用范围；与已有09-09进程/策略、09-10 UAC和冷重启证据比较。无新本地动态取证、产品代码、注册、GUI或系统策略修改。研究文档链接与JSON结构核对；最终仓库验证日志：.runtime/v03-005-final-tests/repository-literature-final.log。未重跑既有成功产品套件，未关闭Windows验收门禁。

## 2026-09-11 注册表视图隔离与原生入口恢复

当前token/policy只读检查仍Medium/非管理员/同用户会话，UAC开启；Procmon对照全局迹988220行，因文件尺寸阈值提前结束，原failed保留，未进入live阶段。95个固定目标行/36个Reg事件显示检查进程经过WC Silo；只读hivelist把该Silo映射到Codex包。当前manifest的新ExcludedKeys与旧disabled同时存在，按微软文献限定解释，未修改应用包。

原323 guard包内登记的同一有效期，inside20registered→包外同用户普通reader20missing→inside20registered；清理后两侧20missing。相同guard/DLL/字段的包外登记后native20registered；登记之后创建新Explorer9860，SDK17、此电脑4、一次Browse S_OK、实际BA16/42B相等PIDL/10项；注销后原生此电脑3项和UI入口消失。native8根/20missing/双通知/guard退出、自有窗口0、原desktop6212精确创建时间保留。成功范围仅官方样例控制；产品自身Shell待验，源码套件未因取证重复运行。

只读Procmon离线查看器后来卡住，精确持有身份后只结束该查看器；CSV前后SHA相同，PML因独占锁缺关闭前SHA，尺寸和mtime不变并记录关闭后SHA，不作不存在的逐字节前后证明。来源与脱敏限制见registry-view-boundary/evidence.json。本次证据与协调检查点验证日志：.runtime/v03-005-final-tests/repository-view-boundary-final.log。40副本、63个直接来源加2个冻结引用由root独立核对；Git暂存后的原件字节另行核对。

## 自有Shell登记来源防误报修正集成

ad84644合入4479bcb。root审查12文件的完整改动：写前执行来源准入、null owner保留、始终区分当前视图与真实系统注册/清理、原通知STA/COM与失败回滚。worker已通过19个上下文/报告案例、2个入口接线检查、17项Shell契约与帮助类型编译；这些成功输入未变，不重复执行。合并后的仓库验证通过，日志.runtime/v03-005-final-tests/repository-native-context-integration.log。自身4FF扩展的包外实机验证仍由Windows owner准备，未把此代码修正或微软样例恢复计作G1..G4关闭。

## 自有最小Shell真实入口与清理信号

bafd144合入f1443a0，self token QUERY|DUPLICATE查询修复及原始probe流留证已复核。worker verify-02实际普通Explorer父来源全部成立，独立COM/DefView和同进程清理通过；source对应运行的stdout310字节、stderr0、exit0/无timeout/流完成/退出确认。root读取本次cycle原件确认：target10088正常GUI双击后actual4FF/22B PIDL binary与canonical相等/1项；后续同包外guard清理、另一包外reader9字段missing、实际Desktop00021400/2B/32项及CUA入口消失，guard进程不存在。owner报告两自有窗口已关并移交GUI权，原desktop未重启。证据归档由V03-006继续完成，不将最小test-only闭环计作AssetHost/Core/G2..G4或完整客户端通过。

## 自有入口证据归档统一复核

aa17c04合入96c798d，54项原件/副本/Git内容SHA由root逐项核对（48原字节、6明确脱敏），独立review确认4FF/22B/1项、9字段和Desktop33→32及清理时序一致。末次模块快照未重核creation，相关总结已限定，不作持续卸载或稳定性通过。原SecurityException、只读权限对照、verify-02原始流和环境来源证据完整保留。最终仓库验证日志：.runtime/v03-005-final-tests/repository-own-entry-final.log；未重跑未变的C++/.NET/Web/Android套件或本次实机周期。
