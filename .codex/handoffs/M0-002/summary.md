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
  中途失败会恢复原值并仅回滚本次创建且仍匹配 owner/path 的键。注册与卸载完成后
  均调用 `SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, ...)`，对称刷新 Explorer
  关联缓存；原生调用放在后台线程，脚本最多等待 3 秒。注册侧超时会触发原有回滚，
  卸载侧超时只警告并退出，因为 owner-guarded 注册表删除已经完成。
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
四种独立 host 协议情形和隔离 COM factory 探针均通过，静态/契约测试为 16/16。
启用 UAC 并重启后，`EnableLUA=1`、`FilterAdministratorToken=1`，Codex 与主
Explorer 均为 Medium Integrity。在独立自动卸载看门狗保护下完成多轮 HKCU
register/verify/unregister 且无残留；注册式 `CoCreateInstance`、
`SHParseDisplayName`、`SHBindToObject(IID_IShellFolder)`、`IPersistFolder`、
`EnumObjects`、`CreateViewObject`、`SHCreateItemFromParsingName` 和
`IShellItem.BindToHandler` 均返回 `S_OK`。隔离原生 `IShellBrowser` 探针进一步让
`CreateViewWindow` 返回 `S_OK` 并实际创建 view window。

另在独立 Windows 11 虚拟机、专用标准用户、只读测试包权限和自动卸载看门狗下，
以微软 `shell32.dll` 控制组验证了 HKCU namespace 入口。控制组成功注册，真实
Explorer 打开了 `AssetLibrary Microsoft Shell32 Control`，并显示测试临时目录中的
标记文件。这排除了“该 Windows/标准用户完全不支持 HKCU namespace”的宽泛假设，
但控制组不是自定义 DLL 或 custom `IShellView`。

控制组首次在 Explorer 窗口仍打开时清理，owner-guarded 注册表删除已经完成，但
清理进程停在最终同步 Shell 通知；只读 WSH 复核显示 class、namespace 和
HideDesktopIcons 值均不存在，注销测试用户后终端恢复。该故障没有损坏系统或留下
测试注册项。改为 3 秒有界后台通知后，自检与“不打开 Explorer 的注册后立即清理”
回归均通过且无超时警告；修复后的 live-window 清理同场景仍待一次小范围复测。
为这唯一一轮复测已启用 round-3 arm 和 120 秒自动清理看门狗；旧的手工注册与
无窗口回归启动器已退役，只会显示提示而不修改状态。

原始 Spike 的真实 Explorer 仍未取得 custom view 证据：微软文档列出的
`Explorer.exe /e,::{CLSID}` 与 `Explorer.exe ::{CLSID}` 两种入口均显示“没有与之
关联的应用”，`shell:desktop` 中也不枚举 `AssetLibrary M0-002` junction；测试 DLL
未被观察到加载。期间无 `Application Error`/WER 崩溃事件，主 Explorer 保持响应。
按用户 Approved 列表的诊断试验没有改变结果并已完整回滚。因此 UAC/high-integrity
阻断已排除，且控制组证明 HKCU namespace 入口可用；剩余问题进一步收敛到原始
自定义 COM 的注册形态、Explorer 发现路径或部署兼容性。本交接仍不是 M0-002
验收通过。

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

- 通过：Codex bundled Python 执行 `python -m unittest discover -s tests/spikes/windows-shell -p 'test_*.py' -v`（16/16）。
- 通过：`git diff --check`。
- 通过：Linux CMake configure（Unix Makefiles，仅确认入口可解析；不是 Windows 构建证据）。
- 通过：Windows 11 Release x64 CMake/MSVC 构建；`dumpbin` 确认 DLL 仅导出未修饰的
  `DllCanUnloadNow` 与 `DllGetClassObject`，PE machine 为 x64。
- 通过：独立 named-pipe host 的 normal、invalid、crash-after-one-request 和
  1000 ms slow 模式；slow 模式在 300 ms guard window 内无响应，随后正常 pong。
- 通过：隔离 PowerShell 进程直接调用 DLL；factory 创建/释放、前后
  `DllCanUnloadNow == S_OK`，错误 CLSID 返回 `CLASS_E_CLASSNOTAVAILABLE`。
- 通过：Windows 11 上使用 Codex 隔离 Python 重跑静态/契约测试（16/16）；包含
  UAC-disabled HKCU COM 拒绝门禁，以及注册/卸载后的有界 Shell cache 对称刷新检查。
- 通过：自动卸载看门狗保护下的 HKCU register/verify/unregister 与直接注册式
  `CoCreateInstance`；每轮卸载后 CLSID/namespace 均无残留。
- 通过：UAC-enabled Medium Integrity 子进程中的完整 Shell contract 探针和原生
  hidden `IShellBrowser` view 探针；bind、folder/item 接口、`CreateViewObject`、
  `CreateViewWindow` 均为 `S_OK`，view window 已创建。
- 阻断证据：真实 Explorer 的两种官方 CLSID 入口均显示无关联应用，Desktop
  junction 不被枚举，且未观察到 DLL 加载；无 `Application Error`/WER 事件，普通
  Explorer 导航仍响应。
- 通过：隔离 VM 中专用标准用户的安全预检；微软 Shell32 HKCU 控制组被真实
  Explorer 打开并显示标记文件。
- 已定位并安全恢复：控制组 live-window 清理的注册表删除成功，旧同步通知阻塞；
  WSH 只读复核为 clean，注销后会话恢复，无系统损坏或注册表残留。
- 通过：有界通知修复后的 self-test 和“不打开 Explorer 的注册/立即清理/只读复核”
  回归；`CONTROL_CLEANED`，class/namespace/hide-desktop 值均不存在且无超时警告。
- 通过：Visual Studio 2022 Build Tools 官方 bootstrapper Authenticode 签名为
  `Valid`，签名者为 Microsoft Corporation，SHA-256 为
  `2AEAC090A9CFB2C56474AA9A6C5817AD8CFB879539E0ED1AECEC33DE9FC2DC4F`；安装退出码
  为 0、无需重启。最新验证构建的 DLL SHA-256 为
  `176F59EBCD3966B531893F749C0D265277B8A00AD4CC53A9817BB739BCC12D0D`，host 为
  `9531C66646D29EC685C28577AC31CD072FCD2290B17D61901DBEE852BCB16D28`；Spike 产物
  未签名，已移入 ignored task sandbox，未提交。
- 未完成：真实 Explorer custom view、Explorer 内 host missing/crash/timeout/invalid
  recovery、crash/restart 20-cycle 和 8-hour soak。

## 架构测试与质量门禁

Python 测试覆盖包结构、Shell 禁止依赖、版本/长度/逻辑 deadline、取消完成顺序、
异步 view activation、单在途 ping、迟到结果 token 丢弃、worker 异常边界、
`IPersistFolder` 的实际接口/PIDL 生命周期、factory lifetime、
新建及既有 HKCU 注册回滚/对称性、枚举 partial-fetch/skip、故障模式入口、
SDK 常量/导出 ABI、构建退出码、UAC-disabled HKCU COM 拒绝、Shell association
cache 刷新的后台线程、3 秒 join 上限与卸载后 best-effort 语义、官方 Explorer
启动形式、host-only soak 定位和生成/私密产物排除。
已有 Windows 可逆注册、隔离 Shell bind 和 view-window 创建证据，但没有 Explorer
进程内加载证据，故 Explorer 故障隔离和恢复门禁保持 unmet。

## 文件安全、权限与性能影响

Spike 不触碰资产文件、数据库或网络；只读 IPC ping。注册只写当前用户 HKCU，
owner marker、路径、根键和未知子键检查避免覆盖/误删。Shell 关联通知最多阻塞脚本
3 秒；卸载以注册表删除为权威状态，通知超时不会让清理进程永久存活。Shell IPC
逻辑尝试预算为 250 ms、payload 上限 4 KiB；取消排空在 worker 上执行且未证明
独立上限。没有 50 万资产性能结论。soak 脚本提供 bounded host cycle 入口，未执行
不得推断稳定性。

## 技术债、已知问题与风险

- UAC/high-integrity 阻断已经排除，微软 Shell32 控制组也证明 HKCU namespace 可被
  同一类标准用户 Explorer 发现；原始自定义 DLL 仍不被枚举或加载。必须在 M0-009
  决定自定义 COM 的受支持注册/安装模型后，才可继续验证 custom IShellView 生命周期
  及右侧视图尺寸/重建行为；不得擅自改用 HKLM。
- 有界通知已通过不打开 Explorer 的安全回归，但旧故障发生在控制组窗口打开时；
  仍需在隔离 VM 复测一次 patched live-window cleanup，确认最坏只出现 3 秒警告并
  能正常退出。
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

保持当前注册表 clean，不修改 HKLM。隔离 VM 现只为一键 patched live-window
cleanup 启用 round-3 arm；该入口会注册、打开控制组、等待 20 秒、清理并复核，
另有 120 秒看门狗。若正常退出或在 3 秒内给出预期 warning，即可关闭“清理脚本
无限等待”回归，并立即再次轮换 arm。原始 DLL 则由 M0-009 裁决自定义 COM 的
namespace 注册/部署兼容路径，再按 `docs/spikes/M0-002/explorer-soak-protocol.md`
分步执行真实视图、故障恢复、20-cycle 与 8 小时 soak。

## Codex 线程链接（可选）

仅作为导航，不是唯一交接依据。
