# M0-002 测试记录

## 执行环境

- Linux x86-64 协调环境，Python 3.x，Git，CMake 可用。
- 无 Windows 11、MSVC/Visual Studio、Windows SDK、Explorer 或 HKCU 注册表。

## 执行命令

| 命令 | 结果 |
| --- | --- |
| `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s tests/spikes/windows-shell -p 'test_*.py' -v` | 通过，6/6 |
| `git diff --check` | 通过 |
| `cmake -S tests/spikes/windows-shell -B /tmp/m0-002-cmake-configure-unix -G 'Unix Makefiles'` | 通过配置；警告为 Windows-only，非 Windows 构建 |

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
重型依赖；协议版本、16-byte header、4 KiB payload、250 ms timeout 与 overlapped
cancel；注册脚本 HKCU-only、owner marker 和注册/卸载对称；host 故障入口；无
生成二进制、reg/log/pdb 或凭证值。

## 通过

- Python 静态/契约测试 6/6。
- Git whitespace check。
- Linux CMake configure 入口解析。

## 失败 / 跳过

- Windows build/register/verify/unregister：跳过，当前环境无 Windows 工具链和注册表。
- Explorer navigation、custom right-side view、host missing/crash/timeout/invalid
  recovery、uninstall Explorer recovery：跳过，必须真实 Windows 11 x64。
- 8-hour soak：跳过，必须 Windows host；不得将入口存在记为通过。

## 故障注入与恢复验证

仅完成静态入口检查。真实执行需使用 `run-host.ps1` 的 normal/invalid/crash/slow
模式，并观察 Explorer 不冻结、状态文案可恢复、normal host 可重新连接。

## 性能数据

无真实性能数据。协议静态上限为 4 KiB payload 与 250 ms Shell 侧 I/O deadline；
soak 结果待 Windows 门禁。

## 尚未覆盖

真实 COM activation、Windows 11 x64 MSVC build、namespace registration、Explorer
view lifetime、failure isolation、uninstall cleanup、20-cycle crash/restart、8-hour
soak，以及任何生产规模性能结论。
