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
- 本机 `EnableLUA=0`，当前进程为 High Mandatory Level。已完成 Release x64 构建、
  隔离运行探针及看门狗保护的 HKCU register/verify/unregister；Explorer 从未加载
  Shell DLL，所有注册项、Host 和看门狗均已清理。

## 执行命令

| 命令 | 结果 |
| --- | --- |
| `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s tests/spikes/windows-shell -p 'test_*.py' -v` | 通过，15/15 |
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
| Windows：`SHParseDisplayName` + `SHBindToObject(IID_IShellFolder)` | parse 通过；bind 返回 `0x80040154`，与 UAC-disabled 高权限进程忽略 per-user COM 一致 |
| Windows：Explorer 地址/官方启动入口 | 未进入视图；Explorer 未加载 DLL、无崩溃事件，普通导航正常 |
| Windows：UAC-disabled 注册预检 | 通过；在任何写入前拒绝，CLSID/namespace 均不存在 |
| Windows：Codex bundled Python `-m unittest discover ... -v` | 通过，15/15 |
| Windows：`Get-AuthenticodeSignature` + `Get-FileHash -Algorithm SHA256` | installer 签名有效；DLL/host 为预期未签名本地 Spike 构建，hash 已记录 |

Windows 主机上的精确命令：

以下命令要求 `EnableLUA=1`；当前主机会由 `register.ps1` 在写入前拒绝。不得改用
HKLM 绕过该门禁。

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
collision guard、新键/旧值 rollback 和注册/卸载对称；枚举 partial-fetch/skip；
host-only soak 与人工 Explorer protocol 分工；无生成二进制、reg/log/pdb 或凭证值。

Windows 构建产物：

- `AssetShellExtension.dll`：28672 bytes，SHA-256
  `176F59EBCD3966B531893F749C0D265277B8A00AD4CC53A9817BB739BCC12D0D`。
- `AssetHostStub.exe`：28672 bytes，SHA-256
  `9531C66646D29EC685C28577AC31CD072FCD2290B17D61901DBEE852BCB16D28`。
- 生成目录已从源码包移入 Git ignored 的
  `.runtime/sandbox-storage/M0-002/windows-build-20260901-uac-blocked/`，可恢复且未提交。

## 通过

- Python 静态/契约测试 15/15。
- Git whitespace check。
- Linux CMake configure 入口解析。
- Windows Release x64 build、PE/export 检查、独立 host 故障替身和隔离 COM factory
  探针。
- HKCU register/verify/unregister 回滚、注册式 COM activation、UAC-disabled 零写入
  拒绝，以及 Explorer 未加载 DLL/无崩溃/普通导航恢复检查。

## 失败 / 跳过

- Explorer custom right-side view 未激活：当前主机 `EnableLUA=0`，Shell bind 对
  HKCU CLSID 返回 `REGDB_E_CLASSNOTREG`。注册/验证/卸载本身已执行并完整回滚。
- Explorer 内 host missing/crash/timeout/invalid recovery：跳过，必须在
  `EnableLUA=1` 的 Windows 11 x64 主机执行。
- 8-hour Explorer soak：跳过，`soak.ps1` 仅为 host-cycle helper；必须按
  `docs/spikes/M0-002/explorer-soak-protocol.md` 完成人工 Explorer 证据。

## 故障注入与恢复验证

独立 host 层已真实执行 normal、invalid、crash、slow：normal 返回
`asset-host-ready`；invalid 发出 magic=0、payload=4097 的畸形头；crash 在首个请求后
以 17 退出；slow 在 300 ms 内无响应并约 1000 ms 后 pong。该证据不等于 Explorer
恢复验证；仍需观察 Explorer 不冻结、状态文案可恢复、normal host 可重新连接。
`soak.ps1` 只提供 host-cycle 信号，不替代 Explorer 故障注入。

## 性能数据

仅有 host slow guard 数据：1000 ms 延迟替身在 300 ms 内未响应；这未测量 Shell
worker 的 250 ms deadline 或取消排空。协议静态上限为 4 KiB payload；取消排空
仍无时长上限证据，soak 结果待 Windows 门禁。

## 尚未覆盖

UAC-enabled Explorer view activation/lifetime、Shell worker deadline/cancel、Explorer
failure isolation、20-cycle crash/restart、8-hour soak，以及任何生产规模性能结论。
