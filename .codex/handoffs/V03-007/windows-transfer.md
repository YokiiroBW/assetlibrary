# Windows隔离收尾转交V03-006（待root正式切换单写所有权）

2026-09-08。V03-007停止修改转交文件后，由root通知V03-006开始，避免共享目录并发写。技术点均为partial，不启用Windows图片。

## 当前代码与事实

- 原Windows NativeAOT在LPAC/zero-cap/JOB_LIST内无ready、exit0xFFFFFFFF、stderr空；相同最小环境的非隔离可信1px warmup控制exit1+24byteUnavailable，未读取图片输入。
- 同启动器执行任务.runtime/preview-com-probe/observer.cpp的最小MSVC诊断：entry/Event/Thread均成功，CoInitializeEx(MTA)=0x80070005。这与精确.NET10.0.11 FinalizerStart→PalInitComAndFlsSlot必需COM初始化失败→Bootstrap -1调用链吻合。
- root已批准唯一lpacCom兼容capability。相同原生观察器仅增加此cap后CoInitializeEx/FlsAlloc/Wait均0，exit0。没有registryRead、network、loopback exemption、HKLM/UAC或全局策略变化。
- worker guard要求AppContainer、LPAC、恰好一个且SID精确匹配的lpacCom，以及Job512MiB committed memory/CPU3秒/active1/kill-on-close。STARTUPINFOEX包含显式三根stdio HANDLE_LIST、JOB_LIST和LPAC opt-out，创建suspended后核验Job再resume。
- LOCALAPPDATA缺失曾导致CreateProcess203；显式保留原始LOCALAPPDATA与SystemRoot/WINDIR后该错误已消失。环境排序/UTF16/双NUL，Host/DB环境不继承。GC预算只在worker csproj定义64MiB，launcher不覆盖。
- 最新实际AOT候选已进入Main并输出24byte status7 Unavailable（不再是无输出-1），尚未ready。需要区分Warmup原生库加载与token/capability/Job反查失败，不能先添加更多cap。原始TRX `.runtime/preview-tests/windows/windows-image-gc64-com.trx`。

## 文件所有权建议

转交WindowsImageStartup.cs、WindowsImageProcess.cs、WindowsImageProfile.cs、WindowsImageCapability.cs（都在Core/Infrastructure/ReadOnlyWorkers）、worker的WindowsImageIsolation.cs、Preview.Tests/WindowsImageProcessTests.cs及Windows专属新测试/临时工具。Capability源码由parent与worker编译链接，不重复定义SID算法。

WindowsWorkerJob.cs、ImageChildProcess.cs（跨平台共享接口/普通子进程）、ImageWorkerProtocol.cs、Program.cs、GC预算、csproj/locks以及Core broker/缓存/HTTP仍由V03-007单写；需要改动提交具体请求。root另持有根solution/CI/中央版本与公共wire。

## 构建、候选与复现

SDK `C:/Users/Administrator/AppData/Local/Temp/V01-014-tooling-and-tests/tooling/dotnet/dotnet.exe`。独立worker普通锁为net10.0，实际AOT用locks/<project>.win-x64.lock.json与显式`-p:RuntimeIdentifier=win-x64 -p:PublishAot=true -p:AssetLibraryReleaseLockRoot=<absolute>`；不要再把RID写回普通lock。

最新候选 `.runtime/preview-worker/win-x64-candidate`：exe1,469,952B、Skia12,274,488B、MIT1129B、完整notices139775B，另有8,359,936B项目PDB尚需从最终候选分离（native PDB已不在此候选）。这不是最终交付包。Native symbols保留在build输出供诊断。

设置进程内环境ASSETLIBRARY_IMAGE_WORKER_TEST_EXECUTABLE到候选exe，运行Preview.Tests并filter FullyQualifiedName~WindowsImageProcessTests。该测试编译链接精确内部源，不改Core friend API。观察器文件在自有.runtime/preview-com-probe；将该env指向observer exe可收集同launcher阶段数值，但图像期待器会故意失败，不能当图像成功。

## 必须收尾

1. 定位status7；验证实际AOT ready+派生像素后，再证明文件/网络/COM绕路/非白名单句柄拒绝。
2. 早期profile/copy/CreateProcess阶段的15秒取消覆盖；原始错误与清理错误分别保留；严格确认进程退出后才删profile。
3. Profile当前失败记录保留思路尚未完成启动恢复：disposed状态、Create/Update HRESULT、无条件finally掩盖首错及crash清理需收敛。仅操作本任务创建成功的名字/SID/私有目录，不接管已有profile，不以删除他物修复。
4. CPU、内存、子进程拒绝、取消/父退出和完整profile/目录/句柄清理的同代码证据。COM初始化成功不是隔离验收。

本任务未部署NAS或修改真实资产。root负责后续统一Core/PG/HTTPS验收，Windows修复不得造成Core在未证实隔离时默认启用。
