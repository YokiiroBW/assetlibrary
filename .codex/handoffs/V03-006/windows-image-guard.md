# Windows图片隔离：LPAC有效访问自检检查点

状态partial，**没有启用整体Windows预览或解除Explorer门禁**。实现提交 `a06d3f37dcaeea8791da0963787d9b00ebe27a6d`，基于依次合入root冻结57d1578和V03-007转交ca1d235后的 `7cd555b`。此批仅改生产WindowsImageIsolation.cs和WindowsImageProcessTests.cs。

## 失败定位

当前系统API版本10.0.26100.0，注册表ProductName原文Windows 10 Enterprise LTSC 2024，DisplayVersion24H2，Build26100/UBR8037。ntdll.dll为10.0.26100.7920、advapi32.dll为10.0.26100.3624；Windows SDK10.0.26100.0、MSVC19.44.35228.0、.NET SDK10.0.111。

转交无trace候选EXE强hash `3E93F9A61F5A7431617F1297CB5B5CF900BAF6F734244D6A03D9052618CA3897`；复制到自有.runtime后重验，未在021c产物旁写入。本地诊断副本仅增加数字阶段/异常类型与HRESULT输出，不改变Program生产源、GC64MiB、capability、Job或句柄策略。

诊断显示Skia Warmup完成，AppContainer=1、恰好唯一lpacCom SID检查成功；随后信息类46的GetTokenInformation(NULL,0)返回required0/error87，固定4-byte缓冲同样失败。仅在诊断副本试用NtQueryInformationToken(46,buffer4)也得到C0000003/STATUS_INVALID_INFO_CLASS，未把该API加入生产。原实现因此在Job查询前短路返回false/status7。诊断日志第一版过滤掉了负数值，第二版以十六进制显示并补充required/error，保留各次独立报告，不把旧静态enum核对当运行证据。

## root批准的替代验证

2026-09-08 root明确批准：仍在创建时设置ALL_APPLICATION_PACKAGES_OPT_OUT=1，保留AppContainer、唯一lpacCom、私有目录RX、Job与stdio白名单；通过自己的token的impersonation副本执行两个合法内存SD的有效访问检查，不把token赋给线程、不模拟其他身份。

两个SD都有owner/group=SYSTEM、当前User的固定对象读权限1。正控制另外明确授予本AppContainer SID；负控制只另外授予AAP(S-1-15-2-1)。使用GENERIC_MAPPING(read1/write2/execute4/all7)，所请求权限1不是owner隐式权限。正控制必须允许且granted=1；负控制必须拒绝且granted=0；任一API失败或控制结果异常均拒绝启动。

用可信原生常量观察器、同生产Startup/Profile/Capability/Job实现验证，再只将.runtime中的Startup副本 `Marshal.WriteInt32(policy, 1)` 改为0进行普通AppContainer对照。普通对照不执行decoder、图片或业务。文件marker位于本次自己成功创建的目录，子文件DACL受保护，避免继承AppSID污染AAP-only负控。

| 检查 | LPAC | 普通AppContainer |
| --- | --- | --- |
| AppSID正控制AccessCheck | API成功/允许/granted1 | 同左 |
| AAP-only AccessCheck | API成功/拒绝/granted0 | API成功/允许/granted1 |
| AppSID marker真实打开并读取固定字节 | 成功 | 成功 |
| AAP-only marker真实打开 | ERROR_ACCESS_DENIED=5 | 成功并读取固定字节 |

所有观察器退出，正常Dispose流程完成；两个自有state目录的owner记录数均0。这个对照证明本机的AAP opt-out有效，不能外推为所有文件/网络/COM边界已通过。生产只采用同样的双AccessCheck，没有将87或不支持查询视为成功，也没有增加capability。

依据：[LPAC含义](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ne-winnt-token_information_class)、[AccessCheck契约](https://learn.microsoft.com/en-us/windows/win32/api/securitybaseapi/nf-securitybaseapi-accesscheck)、[NtQuery信息类型](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntifs/ne-ntifs-_token_information_class)。CheckTokenMembershipEx未被用作唯一证据。

## 真实无trace产物与验证

原生产csproj/Program/锁、GC64MiB和其他生产源保持转交版本；实际NativeAOT发布的新EXE SHA256为 `155FD3C8334F3AD60E4437FFB83BD9B4C956C0A0225DF6E221E1261BEBFC97AA`。相同LPAC启动器下，现有NativeAotDecoderRunsInsideLpacAndReturnsARealThumbnail用例完成Ready、1024×600合成PNG输入、512×300真实PNG输出、exit0和owner目录为空，1/1通过、0skip。

这项测试验证响应PNG签名/尺寸/位深与协议闭环，未独立检查所有输出像素，也不代表完整格式矩阵。生成用的Skia代码没有移入Host或Shell。测试的失败输出改为只保留限定长度的阶段字段和异常类型/HRESULT，避免复制任意stderr路径到测试报告。

精确命令（dotnet使用转交说明中的10.0.111绝对路径）：

```powershell
dotnet restore services/worker-supervisor/ImagePreview/AssetLibrary.ImagePreview.Worker.csproj --locked-mode -p:RuntimeIdentifier=win-x64 -p:PublishAot=true -p:AssetLibraryReleaseLockRoot=<repo>/services/worker-supervisor/ImagePreview/locks --packages .runtime/nuget
dotnet publish services/worker-supervisor/ImagePreview/AssetLibrary.ImagePreview.Worker.csproj -c Release --no-restore -p:RuntimeIdentifier=win-x64 -p:PublishAot=true -p:UseSharedCompilation=false -o .runtime/windows-image/production-candidate-1
# 进程内ASSETLIBRARY_IMAGE_WORKER_TEST_EXECUTABLE指向上述本地候选
dotnet test tests/dotnet/AssetLibrary.Preview.Tests/AssetLibrary.Preview.Tests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~WindowsImageProcessTests --logger "trx;LogFileName=production-first-image.trx" --results-directory .runtime/windows-image/results
```

测试程序集先经locked restore与Release build。两个修改文件分别通过对应项目的 `dotnet format whitespace --no-restore --verify-no-changes --include <file>`；validate_dotnet_source.py通过（397个C#文件），diff检查通过。合入转交点后的verify_repository基线通过，Alpha仍blocked；没有重复原4项Explorer loader矩阵。

诊断工具曾因C6387/CA1502/CA2219、缺少诊断声明及测试重复using编译失败，均修正源而非压制分析器；这些不计为产品测试成功。诊断v1..v4记录status7失败，生产最终1/1通过。各次结果摘要及源/产物hash在[证据索引](windows-image-guard/evidence.json)，原始TRX保留自有.runtime；摘要仅保留实际测试结果与安全诊断，没有无关机器标识和堆栈。

## 对照测试来源与复现

`windows-image-guard`归档原生观察器、CMake文件、托管parent项目/入口的逐字节文本副本及原始两份结果。`.txt`后缀表示交接中的源码文本，不作为产品编译输入。原始工具位于.runtime/windows-image/access-probe、access-parent；源hash在evidence.json。native observer以/MT、/W4、/WX、/analyze及冻结SDK构建。

parent项目的RepositoryRoot指向本仓库，StartupSource在LPAC时指向提交7cd555b的生产Startup，在普通对照时指向仅将policy值1变0的本地副本；其余Profile/Capability/Job/ReadOnlyWorkerProtocol均来自同一提交。运行参数仅为自有原生观察器EXE与自有state目录。请勿拿后续修改过的Profile/Startup直接冒充本次对照来源。

## 后续仍需完成

启动/失败清理真实完成的StartAsync、有限pending-cleanup名额配合、逐阶段取消与原错/清理错分别保留、owner恢复，以及文件/网络/COM绕路/非白名单句柄/CPU/内存/父退出/取消的完整同启动器证据。相关代码继续由V03-006单写，Core broker/lease/共享Program仍由V03-007单写。Windows能力和原Explorer目标保持partial；root可先独立合入此检查点，但不能据此启用整体Windows能力。
