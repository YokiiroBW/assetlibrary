# V03-012 测试记录

日期2026-09-12，Asia/Shanghai；指定V03-012 worktree，MSVC19.44.35228/Windows SDK10.0.26100，VS2022 BuildTools CMake/CTest。所有测试进程无窗口，不注册、不连接Core。

## 命令和结果

```powershell
$cmakeBin = 'C:/Program Files (x86)/Microsoft Visual Studio/2022/BuildTools/Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin'
& "$cmakeBin/cmake.exe" -S tests/windows-shell -B .runtime/explorer-fault-harness -G 'Visual Studio 17 2022' -A x64
& "$cmakeBin/cmake.exe" --build .runtime/explorer-fault-harness --config Release --parallel 2
& "$cmakeBin/ctest.exe" --test-dir .runtime/explorer-fault-harness -C Release --output-on-failure -R '^explorer_(snapshot|fault_harness)$'
```

配置通过。首次构建发现新工具缺CoCreateGuid声明，补objbase.h后完整Release构建通过，启用/W4 /WX /permissive- /analyze /utf-8。09:16:04–09:16:06冻结pipe时段：snapshot0.80s，fault_harness0.97s，2/2通过，既有累计deadline156ms。结束后子进程/pipe全部释放并通知协调者。

协调审查指出partial仅观察Unavailable不足以区分silent，补response_written实际完成字节数和20B/40B断言。最终diff复核及git diff --cached --check通过。仅重建新增目标及复验对应测试：

```powershell
& "$cmakeBin/cmake.exe" --build .runtime/explorer-fault-harness --config Release --target ExplorerFaultHost ExplorerFaultHarnessTests --parallel 2
& "$cmakeBin/ctest.exe" --test-dir .runtime/explorer-fault-harness -C Release --output-on-failure -R '^explorer_fault_harness$'
```

09:17:58–09:17:59第二次pipe时段：1/1通过，1.02s。silent=Unavailable/156ms，partial=Unavailable/157ms，invalid=InvalidResponse/0ms，crash=Unavailable/0ms；Ready=0..16ms。partial/invalid若未实际写出20/40B则失败。snapshot源码/输入未变，保留09:16成功结果未重复。两个CTest均TIMEOUT30/RUN_SERIAL。最新原始输出在.runtime/explorer-fault-harness/Testing/Temporary/LastTest.log，前轮数据记录于本文。

## 行为与负控

- 无execute惰性、执行缺mode拒绝；未知mode和lifetime/连接数越界拒绝。
- 第一实例持有时第二Host拒占用退出3，第一实例仍Ready；实际pipe DACL只有TokenUser SID。
- 真实CLI接收原Query请求，状态/有界返回正确；同一Host两个不同request-id均成功。
- crash在完整合法请求后异常退出、client_pid匹配测试进程；partial/invalid要求真实完成写出20/40B。
- stop取消未完成请求，停止空闲/响应后监听；无客户端250ms deadline；连接总数1上限退出。
- 每次故障/停止/期限/上限后新Host占同pipe恢复Ready，停止后Unavailable；客户端取消操作回收。

## 架构与契约

```powershell
& 'C:/Users/Administrator/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe' -I -B scripts/verify_repository.py
```

基线和最终通过：handoff、架构质量/依赖方向、Alpha blocked有效性、.NET source、迁移manifest/21测试、SDK生成/依赖、Web边界、原生主题/Android依赖、14项架构回归。未改共享契约/SDK/Core/Host/现有Shell实现。只运行受影响真实命令，无无关.NET全套或GUI/注册测试。

## 未覆盖

不同SID/session、远程拒绝实机实验；日志堵塞/OS迟迟不完成取消的监护分支；真实Explorer G2交互/Core重新授权恢复；G3 COM保留/增长；G4循环及8小时。短测不算120秒/8小时耐久证据。没有资产访问，空间不足/强哈希变化/真实写入故障不属于本CLI范围。完成时自有运行进程和占用pipe均为0。
