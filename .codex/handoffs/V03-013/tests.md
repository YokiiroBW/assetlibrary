# V03-013 测试记录

2026-09-12；Windows SDK10.0.26100.0，MSVC19.44，VS2022 BuildTools自带CMake/CTest。Python使用已配置运行时，未安装依赖。

## 当前实际结果

严格配置/构建通过：C++17、`/W4 /WX /permissive- /analyze /utf-8`。首轮构建因新增测试的MAXULONG未定义失败，改用Windows已定义的MAXDWORD后通过；没有放松告警或分析选项。

首次 `explorer_enum_done_mechanism` 失败（1.57秒），失败正控为 `system view actually renders synthetic page`。系统送达1次BACKGROUNDENUMDONE，但当前空Desktop根PIDL使DefView呈现33项而非合成单项；未读取33项的任何名称/内容。不把其通知视为资产库等价接线通过。外部Refresh与内部自动Refresh阶段因正控失败尚未运行。

原始输出（本任务合成测试，仅线程/计数/耗时）：

```text
LoadingRefreshTests: system view actually renders synthetic page
enum_done_read triggered=1; phase=0; elapsed_ms=656; thread=24900; done=1; hr=00000000; count=33; valid=0; status=4294967295
enum_done_read triggered=0; phase=0; elapsed_ms=1547; thread=24900; done=1; hr=00000000; count=33; valid=0; status=4294967295
```

原始本地完整日志：`.runtime/explicit-refresh-loading/enum-done.log`。上述失败保留，不因改写诊断表述而重新标记通过。

## 执行命令

既有回归：`explorer_loading_refresh`通过（43.59秒）、`explorer_loading_view_wiring`通过（1.26秒），合计44.86秒；没有运行需要defaultpipe的snapshot/fault测试。此后仅为可选研究入口增加15秒进程监护、清晰标识及构造计数输出，并从默认CTest移除新研究登记；未改变这两个回归路径或DLL行为，按root裁决不增加测试/变体。

下面的cmake/ctest使用 `C:/Program Files (x86)/Microsoft Visual Studio/2022/BuildTools/Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/` 中的程序：

```powershell
cmake -S tests/windows-shell -B .runtime/explicit-refresh-loading -G "Visual Studio 17 2022" -A x64
cmake --build .runtime/explicit-refresh-loading --config Release --parallel 2
ctest --test-dir .runtime/explicit-refresh-loading -C Release --verbose --output-log .runtime/explicit-refresh-loading/enum-done.log -R '^explorer_enum_done_mechanism$'
ctest --test-dir .runtime/explicit-refresh-loading -C Release --output-on-failure --output-log .runtime/explicit-refresh-loading/loading-regression.log -R '^explorer_loading_(refresh|view_wiring)$'
```

其中第一条研究CTest仅在本阶段首跑时临时登记，最终默认CTest清单没有这项；复查原始失败使用日志，不把零匹配运行视为通过。保留的可选诊断入口为 `ExplorerLoadingRefreshTests.exe --enum-done-mechanism`，当前预期实际结果仍是构造正控失败exit1（不是验收通过），超限exit70；root要求研究到此收束，最终提示/监护调整后没有再跑新的场景。

## 边界

最终代码（含可选入口的15秒监护和明确诊断标识）严格构建通过。`python -I -B scripts/verify_repository.py`通过：handoff、架构基线、SDK/契约一致性、源边界与依赖检查通过，包含21项迁移清单测试和14项架构测试；Alpha有效性审计仍为blocked。该检查不连接真实Host/defaultpipe，也不等同完整平台或发布测试。

最终diff审查确认仅测试EXE/测试文档和本任务handoff变化：没有LoadingRefresh.cpp、ExplorerProof.cpp、注册脚本、Host/Core、协议、门禁或默认CTest清单变化。result.json的passed=2/failed=1统计本轮Shell CTest执行；仓库检查单独记录，不与研究机制等价性混淆。

未操作实际Explorer/注册表/defaultpipe/Host/Core/真实资产。只用测试进程自有隐藏窗口；没有强制后台枚举、私有Shell消息或自有IShellView包装器。测试自身的WM_APP消息只发给自有隐藏父窗口，用于500ms后一次读取，不是Shell私有通知。

未声称实际Explorer工具栏、键盘F5、Host重启单次刷新、G2/G3/G4或正式客户端已通过。本任务仅针对同视图Refresh后新Loading的有限推进机制。
