# V03-014 测试记录

## 执行环境

日期：2026-09-12。独立worktree，Windows x64、VS2022 BuildTools MSVC19.44.35228、Windows SDK10.0.26100、C++17。
默认pipe由协调者预留；组件测试结束已通知归还。未注册命名空间、未操作真实Explorer/Core/Host、未接触资产。

## 执行命令

以下cmake/ctest实际使用 `C:/Program Files (x86)/Microsoft Visual Studio/2022/BuildTools/Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/`，
python使用 `C:/Users/Administrator/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe`。

```powershell
python -I -B scripts/verify_repository.py
cmake -S tests/windows-shell -B .runtime/explorer-snapshot -G "Visual Studio 17 2022" -A x64
cmake --build .runtime/explorer-snapshot --config Release --parallel 2
git diff --check
ctest --test-dir .runtime/explorer-snapshot -C Release --output-on-failure -R '^explorer_(snapshot|fault_harness|navigation_menu|loading_refresh|loading_view_wiring|diagnostics)$'
python -I -B scripts/verify_repository.py
```

## 架构与契约测试

首尾仓库验证均通过：handoff/架构边界、21 migration-manifest测试、14架构测试、协议/生成SDK/依赖/源代码检查；Alpha有效性审计通过且发布保持blocked。未运行无关完整.NET/Web/Android套件。
最终diff在受影响CTest前审查，无越界文件、共享协议、写权限、依赖或预算变化。之后仅补充交接和诊断说明，无需重复已通过的原生测试。

## 通过

严格构建启用原`/W4 /WX /permissive- /analyze /utf-8`，最终exit0，无放宽或警告屏蔽。

| CTest | 结果 | 秒 |
|---|---|---:|
| explorer_snapshot | passed | 0.85 |
| explorer_fault_harness | passed | 0.98 |
| explorer_navigation_menu | passed | 0.01 |
| explorer_loading_refresh | passed | 43.53 |
| explorer_loading_view_wiring | passed | 1.25 |
| explorer_diagnostics | passed | 0.02 |

总计6/6通过，46.65秒。原始本地CTest日志：`.runtime/explorer-snapshot/Testing/Temporary/LastTest.log`。
pid2空item、pid3未知、非空item拒绝、空结果拒绝、进程/QPC频率、跨Folder全局Enum/Query计数、重复读取无副作用通过。
Loading验证实际UI线程Refresh计时、RenderedReady只由有效呈现PIDL产生、错误行不Ready、首Ready时间固定；既有500ms/10秒/20次/4view、空视图、站点重入和资源持有测试通过。
ControlledDllOwner在callback持有时S_FALSE，关窗归还slot仍S_FALSE，最后Release后S_OK且LiveCallbacks=0。系统Desktop wiring保留callback1但其owner不属于proof DLL，不能拿其S_OK关闭真实生命周期门禁。

## 失败 / 跳过

第一次中间构建因测试用了未定义MAXULONG失败；换成已有std::numeric_limits。第二次中间构建因负向COM空出参调用违反SAL而被/analyze拒绝；测试改用直接诊断空参数校验及真实显式EnumObjects，保留全部严格编译选项。最终相关测试0失败、0跳过。
未执行已知失败的`--enum-done-mechanism`研究诊断和`--proof-owner-lifetime`可选保留诊断；其既有失败没有更改为通过。

## 故障注入与恢复验证

实际pipe累计预算样本156ms（保留原调度容差断言）、实际取消及时返回、四操作/第五Busy、取消后新Host Ready恢复均通过。
Query测量12次，CancelIoEx7次，资源完成7次，延后回收0次，最终op_live=0/pins=0。Reaped仍以原2000ms外限核对所有取消资源完成；没有为制造延后回收修改时序或伪造事件。

## 性能数据

cancel_max=614ticks，hz=10000000，即0.0614ms。注入最大值输出1170字符；进一步把所有数字统一扩成20位为1411字符，≤2048；全数字JSON断言通过。原pid1边界测试仍通过。

## 尚未覆盖

本轮无实际defer样本，异步回收延迟与真实Explorer对象增长仍需协调者实机证据。未进行G3实机裁决或G4的20轮/8小时；组件时间不能当作Explorer目标进程时间。
