# Explorer 原生入口验证

V03-002 的 test-only C++17/Windows SDK 10.0.26100 验证，复用系统 DefView。只有当前用户独占 CLSID `{4FF8301D-2E73-4D49-9FE5-868D5F1EA302}`；拒绝已有注册，不修改 HKLM/UAC/系统策略，不重启 Explorer。代码无网络、资产 I/O、数据库、Provider 和 WinUI 运行时。

## 实际命令

```powershell
cmake -S tests/windows-shell -B .runtime/explorer-proof -G "Visual Studio 17 2022" -A x64
cmake --build .runtime/explorer-proof --config Release --parallel 2
pwsh -NoProfile -File tests/windows-shell/verify.ps1 -BuildDirectory .runtime/explorer-proof
```

构建启用 `/W4 /WX /permissive- /analyze /utf-8`。verify 在 finally 中卸载；隔离子进程最多10秒，校验 COM factory、PIDL、Desktop 枚举/属性/Folder 关联、DefView、子目录 bind、重复注册拒绝和最终无残留。它不替代 Explorer G1..G4。

交互验证必须由协调线程单独启用，限定注册/卸载和窗口所有权；`ExplorerProofProbe --navigate` 只通过 ShellWindows 前后差分定位本次新开的 Desktop 窗口，给 IWebBrowser2.Navigate2 传 PIDL SAFEARRAY。此 COM 调用可能被 Explorer 模态错误阻塞，外层必须设置进程超时和注册清理，不可直接无界运行。不得关闭用户已有窗口。任务曾观察到 UI 输入/截图拒绝，不能据此改变桌面安全状态。

## 当前证据

2026-09-08：隔离探针和可逆 HKCU 注册通过；真实 Explorer 的 CLSID、选择 API 和直接 Navigate2 入口均未证明加载本类，显示“无关联应用”。有界调用日志仅记录隔离探针；parent Desktop UPDATEDIR、正确 DWORD Folder 属性和成功 Folder open association 未消除故障。原因尚未定位，不擅自归因于某个系统设置，不关闭 G1。G2 故障生命周期、G3 取消/延迟、G4 20轮和8小时稳定性尚未获得真实视图证据。

`proof-calls.log` 仅为验证诊断，位于构建 DLL 同目录，上限1MiB；含操作、接口GUID、PID、单调时钟，未包含用户路径或资产名称。`QueryInterface` 行记录请求，`CreateDefView.result` 记录返回HRESULT。不得将此文件I/O移入生产 Shell。

参考：[Microsoft NSE implementation](https://learn.microsoft.com/en-us/windows/win32/shell/nse-implement)、[ExplorerDataProvider sample](https://github.com/microsoft/Windows-classic-samples/tree/main/Samples/Win7Samples/winui/shell/shellextensibility/explorerdataprovider)、[Shell notifications](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shchangenotify)。
