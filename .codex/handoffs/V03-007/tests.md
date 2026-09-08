# V03-007 验证记录

2026-09-08，在本任务独立工作区运行基线 `python -I -B scripts/verify_repository.py` 成功。Python 使用 `C:/Users/Administrator/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe`，默认 PATH 没有 python；没有安装新运行时。

包含既有迁移21项、架构14项及仓库/合同/源策略检查；Alpha有效性审计成功且发布仍blocked。没有运行或声明预览功能、性能、隔离平台或NAS验收通过。

前置只读命令一度因执行环境启动失败，经本窗口批准后成功；用户恢复时已切换为允许执行的环境。这不是业务测试失败。

## 安全读取基础阶段

精确SDK `C:/Users/Administrator/AppData/Local/Temp/V01-014-tooling-and-tests/tooling/dotnet/dotnet.exe` (10.0.111)。复制已安装NuGet缓存到本工作区.runtime/nuget，Core锁定restore通过；新Preview.Tests使用既有MSTest pins生成本工程lock，无根版本修改。

- `dotnet build services/core-server/AssetLibrary.CoreServer.csproj --configuration Release --no-restore`：通过，零警告。首次Linux UTF8 P/Invoke触发CA2101，改严格UTF8零终止byte数组，没有压警告。
- `dotnet format whitespace tests/dotnet/AssetLibrary.Preview.Tests/AssetLibrary.Preview.Tests.csproj --no-restore`：完成。
- `dotnet test tests/dotnet/AssetLibrary.Preview.Tests/AssetLibrary.Preview.Tests.csproj --configuration Release --no-restore --logger "trx;LogFileName=source-boundary.trx" --results-directory .runtime/preview-tests/source`：8通过、0失败、1跳过；TRX位于本工作区.runtime/preview-tests/source/source-boundary.trx。
- `python -I -B scripts/validate_dotnet_source.py`：通过。

通过内容：Unicode字节/原hash与mtime不变、陈旧长度/mtime/容量拒绝且不写副本、取消、真实Windows目录junction/库根junction拒绝、并发写删拒绝、ADS/设备名/路径深度、句柄释放。首次两个symlink fixture因缺SeCreateSymbolicLinkPrivilege失败；目录改复用既有SandboxDirectoryLink（Windows真实junction），文件symlink单列真实缺口，未改系统权限且没有算通过。Linux安全打开/源变化分支以及Windows文件symlink仍需对应平台证据。

阶段测试Compile链接同一内部生产源而不改Core friend API；Core独立编译同样成功，HTTP/隔离实际进程集成不能由此替代。所有样例在本独立worktree内由RepositorySandbox生成的GUID路径，退出清理，没有真实NAS资产。

## 独立decoder技术验证点（未启用）

独立worker Release build成功，win-x64 NativeAOT publish成功；后续新增notices/锁隔离的完整发布尚待复核。Skia绑定API/AOT告警未压制。正常无参数二进制请求：stdin为6个little-endian int32的24-byte header（magic=0x31495041,status=2,profile=0或1,length=输入字节数,width=height=0）+输入，随后关闭stdin；stdout先ready(status1)24-byte，再result header(status3)+PNG。未建立sandbox时只能返回status7退出1，不读取输入。NativeAOT Linux须在同机发布后运行；JIT Linux明确拒绝进入decoder。

Windows LPAC阶段实际失败已记录在.runtime/preview-tests/windows/windows-image.trx。203来自CreateProcess，显式保留Windows原始LOCALAPPDATA/WINDIR后消失；现在进程在ready前退出0xFFFFFFFF、stderr空。非隔离、相同最小环境的AOT可信warmup控制运行正常：exit1、24-byte Unavailable。说明不能只凭可编译或零caps guard称LPAC验证通过。当前该测试1失败，不转换为跳过。

持久时间精度修正：新增100ns余数7的源按Npgsql微秒索引被接受，随后变化到余数8仍被当前全精度stamp拒绝，单项通过；另有真实PG SELECT timestamptz二进制往返回归（包含2000年前时间）等待ASSETLIBRARY_TRIAL_TEST_CONNECTION的现有私有fixture运行，尚未计通过。

root于2026-09-08报告dev-230独立非root容器已对f580b15安全源执行locked restore/format/Release零警告与9/9测试，无skip（含叶symlink）。证据为其私有Linux目录/tmp/assetlibrary-v03-005-linux-izfgezzr/source-read-boundary/.runtime/preview-source-results及source-boundary.log；仅安全源，不代表decoder/NAS隔离。

Native Win32/Linux.NoDependencies包的THIRD-PARTY-NOTICES均为139775B、SHA256 21504C46C4C58AA64C1055BD2DCBC5F9A136B4B8C412ED3CC6740E22C5B127F5。Skia与Win32 MIT LICENSE的SHA256为89101E35A8C66FD4D6DFFC1763259161D35CB564C169714EC227A768C89F2938。worker csproj使用GeneratePathProperty复用精确package路径，构建时核验并发布这两份声明；PDB/dbg留构建目录，新的干净候选目录尚待发布核验。

## 最终Core路径阶段（2026-09-08晚至09日）

上述早期失败用于说明修正来源，不代表当前出图仍失败。当前Core模块用例分组：源边界10项中Windows9过/1叶symlink缺本机权限；服务容量/late startup/source disposal7/7；授权初查/末查/索引变化3/3；完整PNG3/3；真实Host source broker1/1；真实PG时间往返1/1；真实HTTPS图片与信任2/2。另基线21迁移+14架构，受影响Host/Auth包62/63。结果字段123通过/1共享基线失败/1本机平台缺证据按不同逻辑用例计，不累加重复跑或root异平台相同用例。

Core测试命令沿原Preview.Tests csproj，Release/no-restore，分别filter ImagePreviewServiceTests、AuthorizedImagePreviewTests、PngDerivativeValidationTests、ImageSourceBrokerTests、ImageTimestampIntegrationTests、LiveImageEndpointTests；TRX在.runtime/preview-tests/core、live、live-final。新增source late/failure场景确保只有实际reap才能放回名额，失败记录且有限隔离；不以Task被取消当native资源已释放。

真实200运行 `.runtime/preview-live-final/20260908T153904Z-b1b0437f` 使用现有serve.py、21迁移、6临时LOGIN、已审计合成corpus和本地实际LPAC/AOT。PREVIEW_EXPECT_AVAILABLE=1时2/2、0skip通过。PNG512、JPEG/WebP/EXIF6/alpha1600为真实200；SVG/非图415，截断/超大头422；私密连接只进入测试环境，TLS验证精确SHA256+主机名+有效期。显式stop后NATIVE_CLIENT_CLEANUP verified，148源文件hash/mtime、Host/PG/角色/证书/目录都由原fixture核验。最终client TRX为.runtime/preview-tests/live-final/live-images.trx。

此前422源于严格PNG集合漏了Skia的标准sBIT，不是放宽源元数据要求来绕过：来自Windows owner的真实1677B PNG（F67FE315...3057）有IHDR/sBIT08080808/sRGB/IDAT/IEND；现仅允许一次、IDAT前、RGB3/RGBA4个精确8值并验证CRC。正例及正确CRC但bit7/重复/顺序错误负例通过。CacheControl测试也改断言Private和NoStore的语义，避免.NET ToString重排导致虚假失败；HTTP本身未改。

受影响包装/认证回归实际命令：dotnet test tests/dotnet/AssetLibrary.Packaging.Tests/AssetLibrary.Packaging.Tests.csproj -c Release --no-restore，TRX .runtime/preview-tests/host-regression/host-auth-regression.trx。63项中62过/1失败/0skip，唯一DatabaseReadinessTests.EmbeddedManifestMatchesCurrentProductionContract仍期待18而真实manifest21，是既有共享陈旧断言；已交root，未动DB/迁移或该共享测试。

依赖额外使用精确SDK dotnet nuget verify --all逐个验证4包作者/仓库签名及NuGet contentHash，均匹配正常锁；signed contentHash排除签名，与完整nupkg SHA512不同，完整归档也单独匹配cache.sha512。transitive vulnerable查询无已报告包，现有validate_dotnet_dependencies用临时单项目scope通过（4 locked packages），无政策变更。公开摘要decoder-dependencies.json，原始日志.runtime/preview-tests/signature-*.txt及decoder-vulnerabilities.json；NuGet覆盖不是所有native漏洞不存在的证明。

root独立Linux当前默认40d2d69：无GC覆盖、Vm约424736KiB、AS512MiB，完整corpus与isolation/native memory/CPU已通过；低权限现有身份实际创建253线程后NPROC256封顶并join，普通繁忙UID因既有492线程保守EAGAIN，不能用0容量冒充正常线程测试。root已完成Linux真实Core图片2/2及部分移动UI联调，原始统一证据和服务清理由root持有，不计入本窗口重复总数、不当NAS目标核证据。

Windows在ca1d235后由V03-006单写；其a06d3f有效LPAC access-check guard已集成，并支撑上述本地200。追加3个cleanup helper的匹配提交尚待到来，csproj链接已获批；此组合及StartAsync适配完成前不跑缺文件的项目，不宣告最终故障/平台门禁通过。

## 2026-09-09延迟清理审查与共享基线修正

root以66b25ce修正DatabaseReadinessTests陈旧18断言，报告相关5/5通过；本分支合为f9b7eb7。root此时完整solution格式、Release零告警及340通过/0失败/34明确平台或环境未执行，不把跳过视为平台证据，不与本任务不同阶段计数重复相加。上述62/63保留为历史实际结果。

de83a3c修复ReapLateStartupAsync等待startup时的嵌套ImageChildCleanupPendingException：真正Completion成功后reaper才成功，失败继续传播。新增ImageDecoderCleanupTests两例；Core Release --no-restore构建0警告0错误。当前本机测试工程三个Windows已批准Compile链接的源尚待V03-006提交，未临时删除链接或假装组合已通过；root现有完整源组合可独立验证此最小提交。

root随后将de83a3c合为433ad01，在未引入a497缺源链接的完整测试组合上实际编译并执行ImageDecoderCleanupTests：2/2通过、27ms、0skip。源broker实际1/1与扫描子进程取消1/1亦补证（不重复累计已有源broker测试）。本地最新verify_repository通过：420份C#源、21迁移测试、14架构测试；Alpha发布仍blocked。当前不同逻辑用例汇总126通过/0当前失败/1本机叶symlink缺证据（原123通过，加已修正的旧基线1项及新增reaper2项），历史失败记录保留。

## Windows最终源码组合与真实HTTPS（25a7bed）

a21a145匹配源已合为0e6027d，与a497三个链接完整组装。共享decoder直接await WindowsImageProcess.StartAsync原任务，并在deadline到期后由late reaper持有真实完成语义；Linux仍原Task.Run进程启动。

`dotnet format tests/dotnet/AssetLibrary.Preview.Tests/AssetLibrary.Preview.Tests.csproj --verify-no-changes --no-restore`通过。Release/no-restore实际编译并执行ImageDecoderCleanupTests、ImagePreviewServiceTests、AuthorizedImagePreviewTests、PngDerivativeValidationTests、WindowsImageLifecycleTests、WindowsImageProcessTests共21/21、0skip，429ms；明确ASSETLIBRARY_IMAGE_WORKER_TEST_EXECUTABLE指向本工作区已发布win-x64-live候选。证据.runtime/preview-tests/assembled/assembled-cleanup.trx。新增Windows5 lifecycle加1真实AOT测试计入本任务逻辑汇总，当前132过/0失败/1叶symlink本机缺证据。

Host与WebGateway fixture均重新Release构建0警告0错误，避免no-build fixture误用旧Core。现有serve.py启动实际临时PostgreSQL/Core/HTTPS，使用新launcher和相同已验证NativeAOT Worker；fixture为.runtime/preview-live-assembled/20260908T162756Z-034e0f1c。ASSETLIBRARY_PREVIEW_EXPECT_AVAILABLE=1，LiveImageEndpointTests 2/2、8秒、0skip；客户端证据.runtime/preview-tests/assembled-live/live-images.trx。主动写入自有stop后父fixture退出0并报告NATIVE_CLIENT_CLEANUP verified，合成原件hash/mtime、Host/PG/角色/证书与运行目录已按原fixture核验清理。此重复完整HTTP验证用于新启动/回收实现，未重复计入逻辑总数。

打包只读检查见packaging-review.md：现有发行尚不包括Worker，未运行新的发行构建或NAS部署。Windows完整资源/故障/COM边界仍由V03-006继续，不以这些生命周期测试替代。

最终源组装与交接更新后verify_repository再次通过：424份C#源码、21迁移与14架构测试；Alpha有效性审计继续blocked。后续仅追加此执行事实与任务检查点，无运行代码变更。

## NAS实际打包接线的本地回归（d18b224）

按f673a12限定5文件修改；最终diff保持通用Docker、原生发行、中央依赖、NAS资源/安全边界与门禁未动。新增专属测试用真实临时文件验证：缺少任一产物/声明/完整性文件、额外DLL/符号、revision错、任一payload的SHA漂移、已重算SHA但错误PE/ARM64/32bit ELF、空声明、重复/路径逃逸/漏项/超限checksum；mock只用于Docker适配边界，验证不启动检查容器且拒绝坏包仍删除该所属容器。合成ELF仅验证打包拒绝逻辑，不是执行/隔离证据。

精确命令`python -I -B -m unittest discover -s tests/release -p test_nas_image_preview_package.py -v`：11项9过/0失败/2POSIX缺本机平台。随后`ASSETLIBRARY_TEST_DOTNET=<exact10.0.111>`，`python -I -B -m unittest discover -s tests/release -p test_*.py -v`：52项47过/0失败/5未执行，12.318秒；其中2项同POSIX权限，另3项为原有native试用包/PG环境变量未设置。既有Host过程smoke按其真实固定dotnet运行通过。

本机无Docker CLI，未伪称Compose解析、Docker构建或NAS目标内核已过；root收到d18b224精确SHA后负责Linux11项和同提交镜像/平台验收。任务当前计入新增9个逻辑用例，总141过/0失败/3本机平台缺证据；已有整发行suite单独报告不与旧逻辑汇总相加。

NAS代码、Windows39ce06d及交接组合后verify_repository通过：425份C#、21迁移、14架构；发布审计仍blocked。此后仅追加本执行事实。

root补证：NAS实现d18b224合为9b72499后，dev-230干净Git检查点0f4d0c68cf693a854b4dde1932cbe1c623b1b6c2对新test_nas_image_preview_package.py实际11/11通过，38ms、0skip，包含本机缺失的POSIX读取与执行权限2项。因此当前任务相同逻辑例累计143通过/0失败/1本机源叶symlink缺证据；历史Windows新组9/11与整release47/52记录保留。root同提交真实builder已启动，镜像/manifest/目标平台结果尚待报告，没有生产NAS操作。root另报告Windows53目录兼容在同一失败目录7/7通过，独立owner证据不重复累加。

## 首次真实镜像构建失败与属性一致性修正

root实际0f4d0c6 Docker Step26中，image-preview restore35.29秒成功，但publish NETSDK1112失败：未下载Microsoft.NETCore.App.Runtime.linux-x64。原因是restore未显式SelfContained，而publish使用--self-contained true；开发机已有runtime pack缓存不能证明干净镜像依赖恢复完整。此真实构建失败保留，不因文件/清单测试通过而抹去。

00b03b176a65e18655e8886ad183d785c9559661仅在NAS worker restore补-p:SelfContained=true，并同步自有Worker README/packaging-review/Windows transfer命令，保持locked restore和--no-restore publish；锁未改变。新增测试从实际Docker指令解析恢复/发布参数，对RID、AOT、SelfContained、release-lock及locked/no-restore边界做一致性断言，避免再次出现跨阶段依赖图漂移。精确新NAS测试命令执行12项：10通过、0失败、2POSIX本机缺平台，70ms；旧POSIX2项已有root同源码Linux证据，不重复算缺口。任务逻辑测试汇总144通过/0测试失败/1本机叶symlink缺证据，但实际镜像build在修复后仍待root重新执行。

## 实际包通过与test-only NAS harness

root新的干净a12b0d139765357b1e33437a4c7a34823dd556ad先Linux12/12无skip，随后实际AOT发布/out-image-preview与整NAS包built_not_deployed成功；此前NETSDK1112已被实际复验解决。Core image SHA2dd1f17c28be1969b6488c5afe14499c71f7f26aa5d36c179b09768056cf54f0，离线images.tar 783892992B/SHA5951b75d46b46c14124885bb71f0cdf7b1c4b3713d495d312861085836bf0626。原失败中间容器aaad47755157由root核身份/退出状态后清理。root完整solution360通过/0失败/25明确平台/PG未执行、零告警，单独报告不累计到本任务重叠逻辑例。

c5495f7308903aabe2a356ca496c777f5b9287b0新增tests/integration/native-clients/verify_nas_worker.py，目标系统Python3.8.15；ast.parse(feature_version=(3,8))和--help通过，本机原生Python可导入运行。固定manifest10输入内容身份通过；已有真实Skia sBIT PNG正例及截断/尾随/错尺寸/长度上限/CRC五坏例全部按预期；有界reader精确上限与超限两分支通过。实际本机Python子进程三场景：正常输出、300000B超过262144B stdout限额、20秒sleep被0.2秒deadline中止，全部子进程确认returncode非None；这些为harness单项自检，不计NAS原生用例通过数。

脚本每Docker control20秒、ready10秒、worker25秒、全组600秒；stderr16KiB、PNG按2MiB/12MiB+24-byte header、probe16KiB。Core镜像身份/commit/固定Worker path核对；每新容器设置并inspect校验1654:1654、cap_drop ALL、NNP、init、readonly、networknone、1GiB、cpu-shares1024、tmpfs64m，不设置CFS/PID上限或seccomp覆盖。原样例不挂载，只ready后发有界字节。CPU必须137/OOMfalse/非parent timeout；threads必须真实created>0且内部passed（含join）；保存initial seccomp/io_uring而不把外层拒绝冒充新增隔离证据。输出仅安全JSON/派生PNG，完整ID ledger与逐项cleanup verification，失败也finally核身份后删除并查询不存在。

运行入口：python3 -I -B verify_nas_worker.py --build-manifest <verified-bundle>/build-manifest.json --corpus <fixed-image-preview-v1> --marker <existing-daemon-visible-readonly-canary> --evidence <new-directory-under-owned-parent>。Target NAS kernel5.10.55+/Docker24.0.2/Python3.8.15由root执行，本窗口未连接NAS。实际20项仍待root结果，parent-death后续单列。
