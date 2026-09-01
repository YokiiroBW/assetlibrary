# M0-002 测试记录

## 执行环境

- Linux x86-64 协调环境，Python 3.x，Git，CMake 可用。
- 无 Windows 11、MSVC/Visual Studio、Windows SDK、Explorer 或 HKCU 注册表。

## 执行命令

| 命令 | 结果 |
| --- | --- |
| `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s tests/spikes/windows-shell -p 'test_*.py' -v` | 通过，14/14 |
| `git diff --check` | 通过 |
| `cmake -S tests/spikes/windows-shell -B /tmp/m0-002-cmake-configure-unix -G 'Unix Makefiles'` | 通过配置；警告为 Windows-only，非 Windows 构建 |
| `python3 scripts/validate_handoff.py` | 通过 |
| `python3 scripts/validate_architecture_baseline.py` | 通过 |

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
方法/PIDL clone/lifetime 与 factory lifetime；注册脚本 HKCU-only、owner marker、
collision guard、新键/旧值 rollback 和注册/卸载对称；枚举 partial-fetch/skip；
host-only soak 与人工 Explorer protocol 分工；无生成二进制、reg/log/pdb 或凭证值。

## 通过

- Python 静态/契约测试 14/14（含 build/soak 资源护栏静态断言）。
- Git whitespace check。
- Linux CMake configure 入口解析。

2026-09-02 低资源修订实际执行：

- `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s tests/spikes/windows-shell -p 'test_*.py' -v`：通过，14/14。
- `git diff --check`：通过。
- 未运行 CMake/build、20 次 crash/restart、8 小时 soak、大数据或大文件测试。

复核小修的静态断言包含 `MaxIterations` 允许上限 12000，以及 cleanup 的
`WaitForExit(5000)` false 分支抛错终止 helper；未执行 PowerShell 分支或 helper。

## 失败 / 跳过

- Windows build/register/verify/unregister：跳过，当前环境无 Windows 工具链和注册表。
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
