# V03-005 当前验证记录

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
