# M0-002 交接摘要

## 完成状态

`partial`

## 完成内容

已交付可审查的 Windows Shell Spike 包：

- C++17/Windows SDK C++/WinRT base + COM `IShellFolder`、`IPersistFolder`（保存
  初始化 PIDL 的 clone 生命周期）、单项 PIDL 枚举器、
  COM class factory 与 custom `IShellView` 最小桥接；Shell 只创建视图、导航
  和本机 IPC，不包含网络、哈希、媒体解码、Provider 或业务规则。
- 进程外 `AssetHostStub.exe`，通过 Spike-local named pipe 提供 ping/pong。
  帧有 magic、版本、固定 16 字节头、4 KiB payload 上限和 request id；Shell
  使用 overlapped I/O，单次 `AskAssetHost` 共享一个 250 ms 逻辑 I/O deadline。
  Explorer view activation 只启动 worker 即返回；worker 在超时后调用 `CancelIoEx`，
  等待取消完成并确认 `GetOverlappedResult` 后才销毁 event/`OVERLAPPED`/buffer，
  随后通过 window message 更新可恢复状态。取消排空是 worker cleanup，不延长
  Explorer 调用线程的返回路径；其自身上限仍须 Windows 门禁验证。
- CMake x64 构建入口（非 Windows 仅配置检查，MSVC/Windows-only 选项受条件
  保护）；PowerShell 当前用户 HKCU 注册、验证、卸载、host 故障模式和 host-cycle
  helper。注册带 owner marker、检查 namespace collision、拒绝覆盖其他 DLL；注册
  中途失败会恢复原值并仅回滚本次创建且仍匹配 owner/path 的键。
- Windows 实际构建后修正 SDK 常量和 COM 导出 ABI：使用 SDK 的 `STDAPI` 声明与
  模块定义文件导出 `DllGetClassObject`/`DllCanUnloadNow`；构建脚本现在会检查
  CMake 配置和编译的真实退出码，不再把失败误报为成功。
- Linux 可运行静态/契约测试，检查包结构、禁依赖、IPC 界限与取消生命周期、
  注册卸载对称性、故障入口、枚举 partial-fetch 语义、host-only soak 定位和
  生成/私密产物排除。
- Windows 继续验证前先收紧 Explorer 边界：F5 通过统一入口异步重试，同一视图
  最多保留一个在途 ping；每次 ping 使用单调 token，销毁或重建视图后会忽略旧
  worker 的迟到结果；detached worker 标记为 `noexcept` 并兜住 C++ 异常，异常只
  转换为可恢复的 unavailable 状态，不越过 Explorer DLL 边界。

已在 Windows 11 企业版 LTSC x64（10.0.26100）安装并核验 Visual Studio 2022
Build Tools 17.14.39、MSVC 19.44.35228、MSBuild 17.14.51、CMake
3.31.6-msvc6 与 Windows SDK 10.0.26100.0。Release x64 DLL/host 真实构建、导出表、
四种独立 host 协议情形和隔离 COM factory 探针均通过，静态/契约测试为 15/15。
在独立自动卸载看门狗保护下完成 HKCU register/verify/unregister 循环且无残留；
直接注册式 `CoCreateInstance` 成功，但 Shell 的 `SHBindToObject(IID_IShellFolder)`
返回 `0x80040154 (REGDB_E_CLASSNOTREG)`。本机 `EnableLUA=0` 且进程为 High
Integrity，符合微软记录的“高权限进程忽略 per-user COM”限制。Explorer 始终未
加载 DLL、无崩溃事件且普通导航恢复正常。注册脚本现会在该环境写入前明确拒绝；
真实 Explorer 视图证据仍未取得，因此本交接不是 M0-002 验收通过。

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
- `tests/spikes/windows-shell/src/AssetShellExtension.def`
- `tests/spikes/windows-shell/src/AssetHostStub.cpp`
- `tests/spikes/windows-shell/scripts/build.ps1`
- `tests/spikes/windows-shell/scripts/register.ps1`
- `tests/spikes/windows-shell/scripts/verify-registration.ps1`
- `tests/spikes/windows-shell/scripts/unregister.ps1`
- `tests/spikes/windows-shell/scripts/run-host.ps1`
- `tests/spikes/windows-shell/scripts/soak.ps1`
- `.codex/tasks/M0-002.md`（任务包纳入本次提交，内容未改写）
- `tests/spikes/windows-shell/test_contracts.py`
- `docs/spikes/M0-002/README.md`
- `docs/spikes/M0-002/explorer-soak-protocol.md`
- `.codex/handoffs/M0-002/summary.md`, `result.json`, `tests.md`

## 模块边界、依赖方向与复用

模块为 `windows-shell-spike`，owner 为 `codex-agent-m0-002`。依赖方向为
Explorer bridge → spike-local IPC port → test AssetHost；没有反向依赖、跨模块
写入或生产模块引用。复用了 Windows SDK 的 COM/Shell ABI；没有复制服务端业务
用例，也没有修改共享契约。

## 新语言、框架或重大依赖

仅在 Spike 内使用任务包允许的 C++17 + Windows SDK Shell/COM ABI，并包含
Windows SDK 提供的 C++/WinRT `winrt/base.h`。Windows 主机新增 VS 2022 Build
Tools + Windows SDK 作为验证期构建工具；没有第三方运行时、网络库、媒体库或
额外服务，正式语言/框架选择仍待 M0-009。

## 共享契约或数据库变化

无。`AssetShellProtocol.h` 是版本化 Spike-local IPC，不是 AssetLink；无数据库
迁移。

## 测试结果

- 通过：`PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s tests/spikes/windows-shell -p 'test_*.py' -v`（15/15）。
- 通过：`git diff --check`。
- 通过：Linux CMake configure（Unix Makefiles，仅确认入口可解析；不是 Windows 构建证据）。
- 通过：Windows 11 Release x64 CMake/MSVC 构建；`dumpbin` 确认 DLL 仅导出未修饰的
  `DllCanUnloadNow` 与 `DllGetClassObject`，PE machine 为 x64。
- 通过：独立 named-pipe host 的 normal、invalid、crash-after-one-request 和
  1000 ms slow 模式；slow 模式在 300 ms guard window 内无响应，随后正常 pong。
- 通过：隔离 PowerShell 进程直接调用 DLL；factory 创建/释放、前后
  `DllCanUnloadNow == S_OK`，错误 CLSID 返回 `CLASS_E_CLASSNOTAVAILABLE`。
- 通过：Windows 11 上使用 Codex 隔离 Python 重跑静态/契约测试（15/15）；新增
  UAC-disabled HKCU COM 拒绝门禁。
- 通过：自动卸载看门狗保护下的 HKCU register/verify/unregister 与直接注册式
  `CoCreateInstance`；每轮卸载后 CLSID/namespace 均无残留。
- 阻断证据：`SHParseDisplayName` 成功，但 `SHBindToObject(IID_IShellFolder)` 返回
  `REGDB_E_CLASSNOTREG`；本机 `EnableLUA=0`/High Integrity。Explorer 未加载测试
  DLL、无 `Application Error`/WER 事件，普通 `C:\Windows` 导航仍响应。
- 通过：Visual Studio 2022 Build Tools 官方 bootstrapper Authenticode 签名为
  `Valid`，签名者为 Microsoft Corporation，SHA-256 为
  `2AEAC090A9CFB2C56474AA9A6C5817AD8CFB879539E0ED1AECEC33DE9FC2DC4F`；安装退出码
  为 0、无需重启。最新验证构建的 DLL SHA-256 为
  `176F59EBCD3966B531893F749C0D265277B8A00AD4CC53A9817BB739BCC12D0D`，host 为
  `9531C66646D29EC685C28577AC31CD072FCD2290B17D61901DBEE852BCB16D28`；Spike 产物
  未签名，已移入 ignored task sandbox，未提交。
- 未执行：UAC-enabled 主机上的 Explorer custom view、Explorer 内 host
  missing/crash/timeout/invalid recovery、crash/restart 20-cycle 和 8-hour soak。

## 架构测试与质量门禁

Python 测试覆盖包结构、Shell 禁止依赖、版本/长度/逻辑 deadline、取消完成顺序、
异步 view activation、单在途 ping、迟到结果 token 丢弃、worker 异常边界、
`IPersistFolder` 的实际接口/PIDL 生命周期、factory lifetime、
新建及既有 HKCU 注册回滚/对称性、枚举 partial-fetch/skip、故障模式入口、
SDK 常量/导出 ABI、构建退出码、UAC-disabled HKCU COM 拒绝、官方 Explorer
启动形式、host-only soak 定位和生成/私密产物排除。已有 Windows 离线与可逆
注册证据，但没有 Explorer 进程内证据，故 Explorer 故障隔离和恢复门禁保持
unmet。

## 文件安全、权限与性能影响

Spike 不触碰资产文件、数据库或网络；只读 IPC ping。注册只写当前用户 HKCU，
owner marker、路径、根键和未知子键检查避免覆盖/误删。Shell IPC 逻辑尝试预算为
250 ms、payload 上限 4 KiB；取消排空在 worker 上执行且未证明独立上限。没有
50 万资产性能结论。soak 脚本提供 bounded host cycle 入口，未执行不得推断稳定性。

## 技术债、已知问题与风险

- 当前主机 UAC 被关闭；根据微软 COM 限制，高权限 Explorer 忽略 HKCU per-user
  COM。项目禁止改用 HKLM，因此必须在 `EnableLUA=1` 的 Windows 11 x64 主机验证
  Explorer namespace、custom IShellView 生命周期及右侧视图尺寸/重建行为。
- 独立 host 的正常、崩溃、慢响应和无效 frame 已验证；仍必须在 Explorer 内验证
  这些情形不会冻结 UI，并验证恢复后重连。
- 必须测量 deadline 后取消排空是否可靠完成且不造成 DLL/worker 长期滞留。
- Spike DLL/host 为本地未签名构建，只能用于受控验证，不能作为分发产物。
- 当前 PIDL、视图和 host payload 只是技术替身，不可直接演进为生产协议或业务
  实现。
- `soak.ps1` 明确只是 host-cycle helper；人工 Explorer soak protocol 和 8 小时
  运行证据仍为 downstream gate。

## 建议合并顺序

建议在 M0-009 汇总窗口中合并本 Spike，再依据真实 Windows 证据决定是否冻结
Shell 技术候选；不得将本 partial 交接当作生产 `apps/windows-shell` 实现。

## 下一步

保持当前未注册安全检查点，不修改本机 UAC、安全策略或 HKLM。下一阶段必须迁移到
`EnableLUA=1` 的 Windows 11 x64 验证主机，按
`docs/spikes/M0-002/explorer-soak-protocol.md` 使用微软官方
`Explorer.exe /e,::{CLSID}` 入口分步执行视图、故障恢复和卸载，再单独安排
20-cycle 与 8 小时 soak；若无法提供该主机，提交 M0-009 决策问题。

## Codex 线程链接（可选）

仅作为导航，不是唯一交接依据。
