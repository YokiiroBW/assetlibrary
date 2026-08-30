# M0-002 交接摘要

## 完成状态

`partial`

## 完成内容

已交付可审查的 Windows Shell Spike 包：

- C++17/Windows SDK C++/WinRT base + COM `IShellFolder`、单项 PIDL 枚举器、
  COM class factory 与 custom `IShellView` 最小桥接；Shell 只创建视图、导航
  和本机 IPC，不包含网络、哈希、媒体解码、Provider 或业务规则。
- 进程外 `AssetHostStub.exe`，通过 Spike-local named pipe 提供 ping/pong。
  帧有 magic、版本、固定 16 字节头、4 KiB payload 上限和 request id；Shell
  使用 overlapped I/O，连接、读写均以 250 ms 为上限，超时后取消 I/O 并显示
  可恢复状态。
- CMake x64 构建入口；PowerShell 当前用户 HKCU 注册、验证、卸载、host 故障
  模式和可配置 soak 入口。注册带 owner marker、拒绝覆盖其他 DLL，卸载只删
  自己创建且路径匹配的键。
- Linux 可运行静态/契约测试，检查包结构、禁依赖、IPC 界限、注册卸载对称性、
  故障入口和生成/私密产物排除。

真实 Windows 证据尚未取得，因此本交接不是 M0-002 验收通过。

## 关键决策

- IPC 明确为 Spike-local，未使用或修改 AssetLink。
- 进程内视图只显示桥接状态；重型视图与所有网络/解析能力留在进程外。
- 采用 HKCU-only、可逆、带所有权标记的注册策略，不做系统级安装和静默覆盖。
- C++/WinRT + COM + 进程外 Host 仅保留为 M0-009 候选；本 Spike 不冻结正式架构。

## 修改文件

- `tests/spikes/windows-shell/CMakeLists.txt`
- `tests/spikes/windows-shell/README.md`
- `tests/spikes/windows-shell/src/AssetShellProtocol.h`
- `tests/spikes/windows-shell/src/AssetShellExtension.cpp`
- `tests/spikes/windows-shell/src/AssetHostStub.cpp`
- `tests/spikes/windows-shell/scripts/build.ps1`
- `tests/spikes/windows-shell/scripts/register.ps1`
- `tests/spikes/windows-shell/scripts/verify-registration.ps1`
- `tests/spikes/windows-shell/scripts/unregister.ps1`
- `tests/spikes/windows-shell/scripts/run-host.ps1`
- `tests/spikes/windows-shell/scripts/soak.ps1`
- `tests/spikes/windows-shell/test_contracts.py`
- `docs/spikes/M0-002/README.md`
- `.codex/handoffs/M0-002/summary.md`, `result.json`, `tests.md`

## 模块边界、依赖方向与复用

模块为 `windows-shell-spike`，owner 为 `codex-agent-m0-002`。依赖方向为
Explorer bridge → spike-local IPC port → test AssetHost；没有反向依赖、跨模块
写入或生产模块引用。复用了 Windows SDK 的 COM/Shell ABI；没有复制服务端业务
用例，也没有修改共享契约。

## 新语言、框架或重大依赖

仅在 Spike 内使用任务包允许的 C++17 + Windows SDK Shell/COM ABI，并包含
Windows SDK 提供的 C++/WinRT `winrt/base.h`。没有第三方运行时、网络库、媒体
库或额外服务；正式语言/框架选择仍待 M0-009。

## 共享契约或数据库变化

无。`AssetShellProtocol.h` 是版本化 Spike-local IPC，不是 AssetLink；无数据库
迁移。

## 测试结果

- 通过：`PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s tests/spikes/windows-shell -p 'test_*.py' -v`（6/6）。
- 通过：`git diff --check`。
- 通过：Linux CMake configure（Unix Makefiles，仅确认入口可解析；不是 Windows 构建证据）。
- 未执行：Windows 11 x64 CMake/MSVC build、HKCU register/verify/unregister、
  Explorer navigation/custom view、host missing/crash/timeout/invalid recovery、
  crash/restart 20-cycle 和 8-hour soak。

## 架构测试与质量门禁

Python 测试覆盖包结构、Shell 禁止依赖、版本/长度/超时边界、HKCU-only 注册
对称性、故障模式入口和二进制/日志/凭证产物排除。没有真实 Windows 运行时证据，
故障隔离和 Explorer 恢复门禁保持 unmet。

## 文件安全、权限与性能影响

Spike 不触碰资产文件、数据库或网络；只读 IPC ping。注册只写当前用户 HKCU，
owner marker 和路径比较避免覆盖/误删。Shell IPC deadline 为 250 ms、payload
上限 4 KiB；没有 50 万资产性能结论。soak 脚本提供 bounded host cycle 入口，
未执行不得推断稳定性。

## 技术债、已知问题与风险

- 必须在 Windows 11 x64 真实环境验证 COM activation、Explorer namespace
  显示、custom IShellView 生命周期及右侧视图尺寸/重建行为。
- 必须真实验证 host 缺失、崩溃、超时、无效/超长 frame 不冻结 Explorer，及恢复
  后重连；Linux 不能替代这些证据。
- 当前 PIDL、视图和 host payload 只是技术替身，不可直接演进为生产协议或业务
  实现。
- `soak.ps1` 是可配置入口，8 小时结果仍为 downstream gate。

## 建议合并顺序

建议在 M0-009 汇总窗口中合并本 Spike，再依据真实 Windows 证据决定是否冻结
Shell 技术候选；不得将本 partial 交接当作生产 `apps/windows-shell` 实现。

## 下一步

在隔离 Windows 11 x64 主机按 `docs/spikes/M0-002/README.md` 执行完整验证协议，
保存构建/注册/导航/故障/卸载/Explorer 恢复及 soak 证据；若任一边界失败，先
提交 M0-009 决策问题，不扩展本 Spike 范围。

## Codex 线程链接（可选）

仅作为导航，不是唯一交接依据。
