# 注册视图边界已定位，官方 Explorer 样例实际恢复

2026-09-11 的同周期 A/B/A 对照证明：登记执行环境导致了这次环境阻断。包内登记有效时，原读取环境可见的 20 项字段，在由既有 Explorer 启动的同用户、普通权限读取环境中全部缺失。改由包外普通用户执行同一登记器后，真实 Explorer 成功进入官方 BA16 样例，显示 10 项；注销后入口消失，回到此电脑 3 项。

这是官方样例与诊断环境的恢复，**不是 AssetLibrary 自有 Shell 实现通过，也不关闭 G1..G4 或产品发布门禁**。本归档没有运行原工具、捕获、登记、GUI、编译或提交，没有修改外层任务状态。

## 证据链

### 1. Procmon 暴露了读取环境的差异

已有 control CSV 共 988,220 行，仅在本地流式解析；目标选择结果 95 行，其中 36 条为固定官方 GUID 的 Reg* 事件。归档只保留这 36 条的脱敏路径、状态与时序，不保留 Detail、编码命令或其他文件事件。[选定注册事件](procmon/control-reg-events.redacted.json)

| 固定进程 | Reg* 事件 | WC Silo 事件 |
|---|---:|---:|
| root PowerShell 2728 | 2,067 | 176 |
| 参考 PowerShell 15344 | 2,133 | 214 |
| 新 SDK Explorer 15848 | 10,417 | 0 |
| 桌面 Explorer 6212 | 33,164 | 0 |
| 管理员 runner 17396 | 0 | 0 |

管理员 runner 没有注册表事件覆盖，不能拿其零数证明“无虚拟化”。两个 PowerShell 的匿名 WC 子名称相同；这不是三个独立包的证明。REPARSE 不是最终打开结果，默认 CSV 又没有线程/活动标识，不能仅凭相邻行重建精确 API 调用。[流式统计](context/silo-comparison.json)

随后只读 hivelist 将这组 WC 命名空间映射至 OpenAI.Codex 包的 Helium 私有数据，包含 `User.dat`、`UserClasses.dat`。原件中的 SID、个人路径与 Silo ID 均被替换，原件 SHA 保留在索引。[映射投影](context/observed-silo-hivelist.redacted.json)

包清单同时出现旧 `RegistryWriteVirtualization=disabled` 和新 `ExcludedKeys`，后者仅排除 Chrome NativeMessagingHosts。[实际清单读回](context/codex-virtualization-manifest.json) 微软 [Flexible virtualization](https://learn.microsoft.com/en-us/windows/msix/desktop/flexible-virtualization) 说明 Windows 11 识别的新声明优先；该排除没有覆盖本次 CLSID 登记。其 [运行机制说明](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-behind-the-scenes) 区分包身份、full-trust 与 AppContainer，并限定虚拟化规则的适用范围。Medium、AppContainer=0，或者某个 API 返回 NoPackage，都不能单独证明没有注册视图隔离。

### 2. 同一个有效登记周期中的 A/B/A

下列时间均为 UTC；副本保留原时间字符串及精确 FILETIME。

| 事件 | 时间 | 20 字段结果 |
|---|---|---|
| 原环境登记，原 guard 323C / DLL F298 | 13:38:30.892 | 四个 HKCU 根，双通知，600 秒期限 |
| inside-during | 13:38:31.810–31.843 | 20 项实际类型和值正确 |
| outside-during | 13:38:55.493–55.523 | 20 项全部缺失 |
| inside-after-outside | 13:40:26.046–26.073 | 20 项仍正确 |
| 原环境清理 | 13:40:26.338 | 八根 absent；随后 20 项 missing |

outside-during 的执行进程 ParentPid=15848、Session=2、SameUserAsExplorer=true、AdministratorEnabled=false。四次包外读取均有启动、执行上下文和完成收据；实际 20 字段读取来自其相同冻结读取链。[包外 during 收据](dispatch/outside-during.receipts.redacted.json)

登记前包外读取、原环境清理后包外读取也都是 20 项 missing。相同有效期中的“20 正确 → 包外 20 缺失 → 包内仍 20 正确”排除了单纯登记过期造成这次差异；没有将包内 reader 的 HKCR 成功当作原生共享视图证明。[九次实际字段记录](readers/inside-during.redacted.json)

### 3. 包外登记后，真实视图恢复，并在注销后逆转

原 guard、DLL、四个 HKCU 根、字段值、双通知及 600 秒规则保持不变。**改变的是登记 guard，以及 native 触发/读回 helper 的执行环境；恢复周期还使用了新的 Explorer 身份和时间。** 因此不将整个 GUI 周期描述为严格只有一个因素变化。

包外普通用户 guard PID14432 的 ParentPid=15848。执行复用既有 ShellFolderView.Application.ShellExecute；[原始包装脚本的脱敏展示副本](dispatch/register-native.ps1.redacted.txt)不作为可执行原件。[微软 Execute-in-Explorer 模式](https://devblogs.microsoft.com/oldnewthing/20131118-00/?p=2643)提供分派方式，本机包外可见性由实际 A/B 与收据验证，不由该文章保证所有 MSIX 场景。

| 事件 | UTC 时间 / 身份 | 文件化结果 |
|---|---|---|
| native 登记 | 13:45:37.778 | 四个 HKCU 根、原值；outside-native 20 项正确 |
| 新 Explorer | PID9860；creation FILETIME `134336080130447535`；HWND3671782 | 创建在登记之后，desktop 仍为 6212 |
| SDK 基线 | 13:47:47.274 | native 17 项 |
| ThisPC 基线 | 13:49:06.031 | 实际 20D04、22 B、4 项 |
| 唯一 Browse | 13:49:06.933 提交 | `browse.return=S_OK`，无超时 |
| official-after | 13:49:07.754 | 实际 BA16、42 B，binary/canonical 相等，10 项 |
| 官方视图 CUA | 13:50:52.781 | 保存的 UI 树投影确认 Zero…Nine |
| native 清理 | 13:52:06.465 | 八根 absent、双通知；guard 返回 |
| 独立清理复核 | 13:52:07.777 前完成 | native 20 missing，guard 退出确认 |
| 注销后 CUA / native | 13:53:44.259 / 13:54:24.241 | 样例标签消失；实际 20D04、22 B、3 项 |

ThisPC 四项的证据是 native `thispc-baseline`。当时 CUA 标签观察只在对话中，**没有单独保存四项 ThisPC 树，不补造这份原件**。保存的 official-view 与 after-unregister UI 树只投影公共样例名、数量、时间和窗口 ID；完整树、NAS/存储名及选择内容不进 Git。[native 读回](native/official-after.redacted.json) · [最小 UI 投影](native/ui-minimal-projection.redacted.json)

## 工具失败与清理限制

Procmon 只运行了 control，没有 live 捕获阶段。control 触发 512 MiB 轮询阈值后请求官方 `/Terminate`：终止命令 exit0、capture 主进程 exit1、CSV export exit0，runner 保持 `passed=false`。最终落盘 PML 大小不能反推触发瞬间大小；阈值不是硬瞬时上限。成功导出和 CSV 语义可读不改写原始失败，也不把 capture 的正常关闭固定假定为 exit0。[原始结果汇编](procmon/control-lifecycle.redacted.json)

挂起的只读 `/OpenLog /NoConnect` 查看器发生过一次强制退出。它是精确核验身份的离线查看器，捕获进程此前已结束，不是调试器、capture 或 Explorer 的强杀。CSV 前后 SHA 相同；PML 因独占锁缺少退出前 SHA，只能说前后长度/mtime 相同，并保留退出后 SHA，不能称 PML 字节前后一致。[离线查看器清理原件](procmon/offline-viewer-cleanup.json)

14:01:05 UTC 最终检查：自有窗口 0、Procmon 0、guard 0，原 desktop6212 的 creation FILETIME `134335976399732731` 保持一致，活动会话及 Default desktop 正常。**没有宣称所有 Explorer 进程退出。**[最终最小投影](final-scene.redacted.json)

## 归档方式与结论边界

40 份必要原件/投影见 [evidence.json](evidence.json)，每个来源都有原 SHA，副本另有 SHA，并明确 `original_copy` 或 `redacted_copy_not_original`。组合文件也不冒充单一原件。读取结果先解析嵌套 JSON，再保留实际类型/值/状态；不保留可能编码私人路径的 stdout、字节缓冲或 Procmon Detail。`.gitattributes` 使用 `* -text`，防止 Git 换行转换破坏原字节哈希。425 MB PML、160 MB CSV、系统库、Procmon 二进制和 EULA 留本地不复制；既有冻结 guard/reader 以链接和 SHA 引用。

当前证据已足以定位并解除这次**登记执行环境造成的视图阻断**。没有在登记期间逐条捕获私有 hive 写入，也没有因此证明过去全部异常或每个缓存条目的来源。恢复的是微软官方样例；产品自己的 Shell 仍须由 owner 在真实 Explorer 中按独立门禁验证。
