# M0-002 测试记录

## 执行环境

- 原始证据：Linux x86-64 协调环境，Python 3.x、Git、CMake 可用；无 Windows
  运行时证据。
- 当前补充环境：Windows 11 企业版 LTSC x64 10.0.26100、PowerShell 7.6.4、
  Codex 隔离 Python、Visual Studio 2022 Build Tools 17.14.39、MSVC
  19.44.35228、MSBuild 17.14.51、VS CMake 3.31.6-msvc6、Windows SDK
  10.0.26100.0。
- Visual Studio Build Tools 官方 bootstrapper Authenticode 状态 `Valid`，签名者
  Microsoft Corporation，SHA-256
  `2AEAC090A9CFB2C56474AA9A6C5817AD8CFB879539E0ED1AECEC33DE9FC2DC4F`；最小组件
  安装退出码 0、`RebootRequired=false`。系统 pending rename 仅含 bootstrapper JSON
  和 3 个临时文件的下次重启删除项，不含 Codex 或工具链二进制替换。
- 启用 UAC 并重启后的有效值为 `EnableLUA=1`、`FilterAdministratorToken=1`、
  `ConsentPromptBehaviorAdmin=5`、`PromptOnSecureDesktop=1`；Codex 和主 Explorer
  均为 Medium Mandatory Level。已完成隔离运行探针及看门狗保护的多轮 HKCU
  register/verify/unregister；所有测试注册项、Host 和看门狗均已清理。
- 2026-09-03 补充隔离 Windows 11 虚拟机控制组。使用专用标准用户；预检确认
  `EnableLUA=1`、非提升 token、不是本地 Administrators 成员，测试目录只授予用户
  读取/执行。控制包锁定机器、用户与 SID，注册必须有单次 arm 文件，并由自动清理
  看门狗兜底。最终 live-window cleanup 通过后，控制注册表状态为 clean，arm 已
  轮换到不存在的 round-4 文件，全部注册入口均已退役。

## 执行命令

| 命令 | 结果 |
| --- | --- |
| Codex bundled Python：`-m unittest discover -s tests/spikes/windows-shell -p 'test_*.py' -v` | 通过，16/16 |
| `git diff --check` | 通过 |
| `cmake -S tests/spikes/windows-shell -B /tmp/m0-002-cmake-configure-unix -G 'Unix Makefiles'` | 通过配置；警告为 Windows-only，非 Windows 构建 |
| `python3 scripts/validate_handoff.py` | 通过 |
| `python3 scripts/validate_architecture_baseline.py` | 通过 |
| Windows：`scripts/build.ps1 -Configuration Release` | 通过；MSVC Release x64 DLL 和 host 均生成 |
| Windows：`dumpbin /exports`、`dumpbin /headers` | 通过；x64，恰有 `DllCanUnloadNow`、`DllGetClassObject` 两个未修饰导出 |
| Windows：隔离 named-pipe client + `AssetHostStub.exe --once` | 通过 normal、invalid、crash-after=1、delay-ms=1000 四种情形 |
| Windows：隔离 P/Invoke COM export probe | 通过；factory 创建/释放、可卸载和错误 CLSID 返回码均正确 |
| Windows：看门狗保护的 `register.ps1 -> verify-registration.ps1 -> unregister.ps1` | 通过；所有者/路径正确，卸载后无 CLSID/namespace 残留 |
| Windows：注册式 `CoCreateInstance` | 通过；创建并释放 IUnknown，引用归零 |
| Windows：完整 Shell contract 子进程探针 | 通过；parse/bind、`IPersistFolder`、枚举、`CreateViewObject`、Shell item 创建和 bind 均为 `S_OK` |
| Windows：原生 hidden `IShellBrowser` view 探针 | 通过；`CreateViewWindow=S_OK`，实际创建 view window |
| Windows：Explorer 两种官方 CLSID 启动入口 | 阻断；均显示无关联应用，未观察到 DLL 加载，无崩溃事件 |
| Windows：普通 Explorer `shell:desktop` 枚举 | 阻断；未显示 `AssetLibrary M0-002` junction，普通导航和窗口响应正常 |
| Windows：UAC-disabled 注册预检 | 通过；在任何写入前拒绝，CLSID/namespace 均不存在 |
| Windows：注册/卸载 Shell association cache 刷新 | 通过；两端均调用 `SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, ...)`，契约测试覆盖顺序和对称性 |
| 隔离 VM：微软 Shell32 HKCU namespace 控制组注册与 Explorer 导航 | 通过；Explorer 打开命名空间并显示测试临时目录中的标记文件，证明该标准用户环境能发现 HKCU 控制组 |
| 隔离 VM：首次保持控制组窗口打开后清理 | 部分失败但已安全恢复；受控注册表项先删除，最终同步 Shell 通知未返回，隐藏清理进程 30 秒超时；只读 WSH 状态为 `CLEAN`，注销测试用户后终端恢复 |
| 隔离 VM：3 秒有界 Shell 通知自检 | 通过；`SELF_TEST_OK`、`ShellNotification=ok`、`RegistrationArmed=false`，没有注册表写入 |
| 隔离 VM：注册后立即清理回归（不打开 Explorer） | 通过；`CONTROL_REGISTERED`、`CONTROL_CLEANED`、最终 class/namespace/hide-desktop 值均不存在，没有通知超时；仅测试临时目录保留 |
| 隔离 VM：保持控制组 Explorer 窗口打开时清理 | 通过；注册、真实 Explorer 打开、20 秒观察、cleanup、verify 全部完成，输出 `FINAL_LIVE_WINDOW_CLEANUP_REGRESSION_COMPLETE`；无通知超时，class/namespace/hide-desktop 值均不存在 |
| Windows：per-user `Shell Extensions\Approved` 诊断 | 未改变 Explorer 行为；策略未启用，试验项已回滚且空键已清理 |
| Windows：`Get-AuthenticodeSignature` + `Get-FileHash -Algorithm SHA256` | installer 签名有效；DLL/host 为预期未签名本地 Spike 构建，hash 已记录 |

Windows 主机上的精确命令：

以下命令要求 `EnableLUA=1`；当前主机已满足。不得改用 HKLM 绕过 Explorer
兼容问题，也不要在 M0-009 决策前继续无界重试。

```powershell
./tests/spikes/windows-shell/scripts/build.ps1
./tests/spikes/windows-shell/scripts/register.ps1
./tests/spikes/windows-shell/scripts/verify-registration.ps1
./tests/spikes/windows-shell/scripts/run-host.ps1 -Mode normal
./tests/spikes/windows-shell/scripts/run-host.ps1 -Mode invalid -Once
./tests/spikes/windows-shell/scripts/run-host.ps1 -Mode crash -Once
./tests/spikes/windows-shell/scripts/run-host.ps1 -Mode slow -Once
./tests/spikes/windows-shell/scripts/unregister.ps1
./tests/spikes/windows-shell/scripts/soak.ps1 -Hours 8
```

## 架构与契约测试

`test_contracts.py` 检查：包文件完整性；Shell 源码不含网络/数据库/Provider 等
重型依赖；协议版本、16-byte header、4 KiB payload、250 ms 逻辑 I/O deadline、
overlapped cancel completion ordering、async view activation；`IPersistFolder` 实际
方法/PIDL clone/lifetime 与 factory lifetime；单在途 ping、迟到结果 token 丢弃、
detached worker exception containment；注册脚本 HKCU-only、owner marker、
collision guard、新键/旧值 rollback、注册/卸载对称，以及 Shell cache 刷新的后台
线程、3 秒 join 上限和卸载后 best-effort 语义；枚举
partial-fetch/skip；
host-only soak 与人工 Explorer protocol 分工；无生成二进制、reg/log/pdb 或凭证值。

Windows 构建产物：

- `AssetShellExtension.dll`：28672 bytes，SHA-256
  `176F59EBCD3966B531893F749C0D265277B8A00AD4CC53A9817BB739BCC12D0D`。
- `AssetHostStub.exe`：28672 bytes，SHA-256
  `9531C66646D29EC685C28577AC31CD072FCD2290B17D61901DBEE852BCB16D28`。
- 生成目录已从源码包移入 Git ignored 的
  `.runtime/sandbox-storage/M0-002/windows-build-20260901-uac-blocked/`，可恢复且未提交。

UAC 重启后的隔离探针记录：

- PowerShell Shell contract 探针：parse、bind、`IPersistFolder`、`EnumObjects`、
  `CreateViewObject`、`SHCreateItemFromParsingName`、`IShellItem.BindToHandler` 全部
  返回 `0x00000000`。
- 原生 C++ hidden `IShellBrowser` 探针：
  `Parse=0x00000000; Bind=0x00000000; CreateViewObject=0x00000000;`
  `CreateViewWindow=0x00000000; LastError=0; ViewWindow=created`。
- 两类探针均在独立子进程内执行并退出；真实 Explorer 诊断保持一轮一清理，未把
  隔离成功误记为 Explorer 进程内加载。

## 通过

- Python 静态/契约测试 16/16。
- Git whitespace check。
- Linux CMake configure 入口解析。
- Windows Release x64 build、PE/export 检查、独立 host 故障替身和隔离 COM factory
  探针。
- HKCU register/verify/unregister 回滚、注册式 COM activation、UAC-disabled 零写入
  拒绝、完整 Shell contract、隔离 `IShellView` window 创建，以及 Explorer
  无崩溃/普通导航恢复检查。
- 隔离 VM 的标准用户/UAC/只读目录安全预检、微软 Shell32 控制组的真实 Explorer
  导航、清理后只读注册表复核，以及有界通知修复后的无窗口和 live-window 清理回归。

## 失败 / 跳过

- Explorer custom right-side view 未激活：UAC-enabled Medium Integrity 隔离探针已
  成功 bind 并创建 view window，但真实 Explorer 不枚举 Desktop junction，官方
  CLSID 入口也在加载 DLL 前显示无关联应用。注册/验证/卸载已完整回滚。
- Explorer 内 host missing/crash/timeout/invalid recovery：跳过，需先由 M0-009
  决定 Windows 11 26100 上受支持的真实 Explorer 注册/部署路径。
- 8-hour Explorer soak：跳过，`soak.ps1` 仅为 host-cycle helper；必须按
  `docs/spikes/M0-002/explorer-soak-protocol.md` 完成人工 Explorer 证据。

## 故障注入与恢复验证

独立 host 层已真实执行 normal、invalid、crash、slow：normal 返回
`asset-host-ready`；invalid 发出 magic=0、payload=4097 的畸形头；crash 在首个请求后
以 17 退出；slow 在 300 ms 内无响应并约 1000 ms 后 pong。该证据不等于 Explorer
恢复验证；仍需观察 Explorer 不冻结、状态文案可恢复、normal host 可重新连接。
`soak.ps1` 只提供 host-cycle 信号，不替代 Explorer 故障注入。

控制组故障链的观察顺序为：owner-guarded 注册表删除完成 → 同步
`SHChangeNotify` 未返回 → 清理包装器 30 秒超时 → WSH 只读复核为 clean → 注销后
终端恢复。由此将阻塞点收敛到最终 Shell 通知；它不是操作系统损坏，也不是注册表
残留。仓库注册/卸载脚本现使用与 VM 回归相同的后台通知 + 3 秒等待上限。注册侧
超时会回滚；卸载侧超时只警告，因为删除结果已经完成。修复后在控制组窗口保持
打开的同一触发场景中，cleanup 正常返回、verify 为 clean、没有超时 warning，
因此“清理脚本无限等待”回归关闭。测试完成后已解除 arm。

## 性能数据

仅有 host slow guard 数据：1000 ms 延迟替身在 300 ms 内未响应；这未测量 Shell
worker 的 250 ms deadline 或取消排空。协议静态上限为 4 KiB payload；取消排空
仍无时长上限证据，soak 结果待 Windows 门禁。

## 尚未覆盖

原始 DLL 的真实 Explorer view activation/lifetime、Shell worker deadline/cancel、
Explorer failure isolation、20-cycle crash/restart、8-hour soak，以及任何生产规模
性能结论。

## 2026-09-05 两端历史对齐

ALIGN-001 将 NAS `6fd8fb1` 的保护性修复与本地 Windows 证据整合。本交接原有 commit、平台观测和测试计数保留为对应历史快照；新实现及验证见 `.codex/handoffs/ALIGN-001/`。原 partial 状态、缺失平台证据和所有发布阻断不变。
