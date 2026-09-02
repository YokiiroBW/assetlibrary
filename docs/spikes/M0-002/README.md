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
- 注册与卸载后的 `SHChangeNotify` 关联缓存刷新在后台线程运行，调用方最多等待
  3 秒；注册超时会触发原有回滚，卸载超时时注册表清理结果保持有效并提示注销刷新，
  不再让清理进程无限等待 Explorer。
- Linux 可运行的 Python 静态/契约测试，阻止生产目录、共享契约和生成产物
  被带入此 Spike。

## Windows 11 x64 验证状态

Windows 11 企业版 LTSC x64 10.0.26100 上已经完成 Release x64 构建、导出检查、
独立 host 的 normal/invalid/crash/slow 模式、COM factory、完整 Shell folder/item
contract 以及 hidden `IShellBrowser` 创建 view window。启用 UAC 后，上述探针均在
Medium Integrity 下通过。

原始自定义 DLL 尚未取得真实 Explorer 进程内加载证据：两种官方 CLSID 启动入口
仍在 DLL 加载前显示无关联应用，Desktop junction 也没有被枚举。因此下面的自定义
视图故障恢复和 soak 流程仍是未完成门禁。

2026-09-03 又在独立 Windows 11 虚拟机、专用标准用户和自动清理看门狗下运行了
微软 `shell32.dll` 控制组。该控制组使用同一类 HKCU Desktop namespace 入口，但
把目标映射到测试用户临时目录：注册成功，Explorer 实际打开了命名空间，并显示了
预置标记文件。这证明该系统与标准用户并非一概禁止 HKCU namespace；原始 Spike
的剩余问题应继续收敛在自定义 COM 注册/部署形态和 Explorer 发现路径上。

首次在控制组窗口仍打开时清理，所有受控注册表项已经删除，但进程停在最后的同步
Shell 刷新；只读 WSH 复核显示 `ClassPresent=false`、`NamespacePresent=false`、
`HideDesktopValuePresent=false`，注销该测试用户后终端恢复。由此将卡住边界定位到
清理后的 Shell 通知，而非注册表删除或系统损坏。改为 3 秒有界后台通知后，
`register -> immediate cleanup -> read-only verify` 回归完整通过，没有超时警告；
修复后“保持控制组 Explorer 窗口打开再清理”的同场景复测仍待执行。

## 剩余 Explorer 验证协议

在 Windows 11 x64、Visual Studio 2022 Desktop C++、Windows 11 SDK、CMake
环境中，从此目录执行：

```powershell
./tests/spikes/windows-shell/scripts/build.ps1
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
该脚本本身只是 host-cycle helper，不执行 Explorer 导航；soak 未执行或缺少
人工 Explorer 证据不得记为通过。

## 观察与结论边界

当前证据已覆盖真实 Windows 构建、隔离 COM/Shell 探针、可逆 HKCU 注册，以及
微软 Shell32 控制组的真实 Explorer 导航与清理故障定位；控制组不是本项目的
custom `IShellView`，不能替代原始 DLL 的 Explorer 进程内加载、host 故障恢复、
20 次 crash/restart 或 8 小时 soak 证据。测试截图未提交到仓库，避免固化机器、
账号和本地路径信息；本记录只保留与结论直接相关的去标识化结果。

是否继续采用 C++/WinRT + COM + 进程外 Host 必须由 M0-009 根据真实 Windows
证据裁决；本 Spike 不冻结正式架构。当前建议是“保留候选，等待 Windows 门禁”，
不是通过或最终采纳。
