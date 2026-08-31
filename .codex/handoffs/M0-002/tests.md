# M0-002 测试记录

## 执行环境

- 原始证据：Linux x86-64 协调环境，Python 3.x、Git、CMake 可用；无 Windows
  运行时证据。
- 当前补充环境：Windows 11 企业版 LTSC x64 10.0.26100、PowerShell 7.6.4、
  Python 3.12.13、任务本地 CMake 4.4.3。
- 当前没有 MSVC、MSBuild、Visual Studio Installer 或 Windows SDK；未构建、注册
  或加载 Shell DLL。
- 已下载但未执行 Visual Studio 2022 Build Tools 官方 bootstrapper。Authenticode
  状态 `Valid`，签名者 Microsoft Corporation，SHA-256
  `2AEAC090A9CFB2C56474AA9A6C5817AD8CFB879539E0ED1AECEC33DE9FC2DC4F`。

## 执行命令

| 命令 | 结果 |
| --- | --- |
| `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s tests/spikes/windows-shell -p 'test_*.py' -v` | 通过，13/13 |
| `git diff --check` | 通过 |
| `cmake -S tests/spikes/windows-shell -B /tmp/m0-002-cmake-configure-unix -G 'Unix Makefiles'` | 通过配置；警告为 Windows-only，非 Windows 构建 |
| `python3 scripts/validate_handoff.py` | 通过 |
| `python3 scripts/validate_architecture_baseline.py` | 通过 |
| Windows：`python.exe -B -m unittest discover -s tests/spikes/windows-shell -p 'test_*.py' -q` | 通过，13/13 |
| Windows：`Get-AuthenticodeSignature` + `Get-FileHash -Algorithm SHA256` | Microsoft 签名有效；hash 如上；bootstrapper 未执行 |

Windows 主机上的精确命令：

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

## 通过

- Python 静态/契约测试 13/13。
- Git whitespace check。
- Linux CMake configure 入口解析。

## 失败 / 跳过

- Windows build/register/verify/unregister：跳过；当前有真实 Windows 11 和注册表，
  但没有 MSVC/Windows SDK，且没有执行任何注册表写入。
- Explorer navigation、custom right-side view、host missing/crash/timeout/invalid
  recovery、uninstall Explorer recovery：跳过，必须真实 Windows 11 x64。
- 8-hour Explorer soak：跳过，`soak.ps1` 仅为 host-cycle helper；必须按
  `docs/spikes/M0-002/explorer-soak-protocol.md` 完成人工 Explorer 证据。

## 故障注入与恢复验证

仅完成静态入口检查。真实执行需使用 `run-host.ps1` 的 normal/invalid/crash/slow
模式，并观察 Explorer 不冻结、状态文案可恢复、normal host 可重新连接；
`soak.ps1` 只提供 host-cycle 信号，不替代 Explorer 故障注入。

## 性能数据

无真实性能数据。协议静态上限为 4 KiB payload，逻辑 I/O 尝试预算为 250 ms；
deadline 后的取消排空在 worker 执行且尚无时长上限证据。soak 结果待 Windows 门禁。

## 尚未覆盖

真实 COM activation、Windows 11 x64 MSVC build、namespace registration、Explorer
view lifetime、failure isolation、uninstall cleanup、20-cycle crash/restart、8-hour
soak，以及任何生产规模性能结论。
