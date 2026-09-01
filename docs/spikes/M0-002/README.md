# M0-002 Spike 记录

## 目的与范围

验证最小 C++/COM Shell namespace、custom `IShellView`、进程外 AssetHost
和故障隔离的工程边界。实现只在 `tests/spikes/windows-shell`，没有生产
Explorer 集成、业务规则、网络、哈希、媒体解析、Provider 或共享契约变更。

## 已交付的可审查准备

- `AssetShellExtension.dll` 的最小 `IShellFolder`、`IPersistFolder`（含初始化
  PIDL 克隆与生命周期）、单项枚举器、COM class factory 和 `IShellView` 桥接源代码。
- `AssetHostStub.exe` 及 spike-local named pipe frame：magic、版本、16 字节
  固定头、4 KiB payload 上限、250 ms `AskAssetHost` 逻辑 I/O deadline。Explorer
  view activation 只启动 worker 并立即返回；worker 自持有事务状态，在 deadline
  后取消并确认 overlapped 完成再释放资源，通过 window message 回传结果。取消
  排空不阻塞 Explorer UI 线程，其独立时长上限仍须在真实 Windows 门禁验证。
- PowerShell 构建、HKCU 注册/验证/卸载、host 故障模式和 soak 入口。
- Linux 可运行的 Python 静态/契约测试，阻止生产目录、共享契约和生成产物
  被带入此 Spike。

## Windows 11 x64 验证协议（待在真实 Windows 执行）

在 Windows 11 x64、Visual Studio 2022 Desktop C++、Windows 11 SDK、CMake
环境中，从此目录执行：

```powershell
./tests/spikes/windows-shell/scripts/build.ps1 -Parallel 2
./tests/spikes/windows-shell/scripts/register.ps1
./tests/spikes/windows-shell/scripts/verify-registration.ps1
./tests/spikes/windows-shell/scripts/run-host.ps1 -Mode normal
```

打开 Explorer Desktop namespace，进入 `AssetHost (M0-002)`，确认右侧自定义
视图显示桥接状态和 `AssetHost connected`。分别停止 host、使用
`-Mode crash -Once`、`-Mode slow -Once` 与 `-Mode invalid -Once`，确认 Explorer
仍可导航，状态栏显示可恢复的 host unavailable 文案；再恢复 normal host，
确认可重新连接。最后执行：

```powershell
./tests/spikes/windows-shell/scripts/unregister.ps1
```

重启 Explorer，确认该 namespace 消失且 Explorer 正常。重复 crash/restart
至少 20 次后，在保持 Explorer 视图打开并按
`explorer-soak-protocol.md` 采集证据的同时执行 `soak.ps1 -Hours 8`。
该脚本默认最多执行 1000 轮（8 小时、30 秒间隔约需 960 轮），每轮只保留一个
Host 进程；达到上限而尚未到 deadline 会明确失败。该脚本本身只是 host-cycle
helper，不执行 Explorer 导航；soak 未执行或缺少
人工 Explorer 证据不得记为通过。

## 观察与结论边界

当前执行环境为 Linux x86-64（Python 3.x、Git），没有 Windows SDK、MSVC、
Explorer 或 HKCU 注册表，因此构建、COM 激活、导航、自定义视图、故障隔离、
卸载恢复和 8 小时 soak 均没有真实证据。当前只能确认源码结构、协议边界、
脚本对称性和静态禁依赖规则。

是否继续采用 C++/WinRT + COM + 进程外 Host 必须由 M0-009 根据真实 Windows
证据裁决；本 Spike 不冻结正式架构。当前建议是“保留候选，等待 Windows 门禁”，
不是通过或最终采纳。
