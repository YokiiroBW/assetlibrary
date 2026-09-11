# Explorer 入口故障：公开案例与官方资料对照

检索日期：2026-09-11。状态：文献调研完成，具体根因未确定，Windows 实机验收未通过。

按用户“先从网络寻找文献”的要求，本阶段暂停新的本地动态取证。检索微软 Learn、Microsoft Q&A、Windows-classic-samples 的公开问题与修复，以及开发者提供复现步骤的原始报告。官方接口文档用于判断契约；发帖者亲历报告用于建立候选解释，不把论坛回复当系统保证。未找到微软已确认的“本机版本中 ExplorerDataProvider 独立绑定正常、实际 CRegFolder 消费缓存全零”的同案或现成补丁；这是本轮检索结果，并非证明不存在同案。

## 1. 同一个微软样例：注册成功，Explorer 不加载 DLL

2022-04-28 的 [Microsoft Q&A 原始报告](https://learn.microsoft.com/en-us/answers/questions/830295/shell-namspace-extension-does-not-work-on-all-comp)使用同一个 ExplorerDataProvider：在两台个人 Windows 10/11 上工作，在企业 Windows 10 上注册成功、能看到虚拟根目录，但 Explorer 不加载 DLL。加入 Approved 列表没有解决；作者改为机器级 COM 登记后可工作，并在 2022-05-01 确认 UAC 是该案例原因。

微软的 [UAC: COM Per-User Configuration](https://learn.microsoft.com/en-us/previous-versions/bb756926%28v%3Dmsdn.10%29)说明，完整性级别高于 Medium 的进程不使用用户级 COM 配置；提权进程及关闭 UAC 的管理员运行环境属于文档列出的情况。[Raymond Chen 的官方说明](https://devblogs.microsoft.com/oldnewthing/20190801-00/?p=102745)也解释了提权时忽略 HKCU COM 注册的原因。不能把“账号名称为 Administrator”或“有管理员组成员资格”等同于进程已提权。

**与本机的差异：**[09-09 既有进程记录](explorer-process-context.json)中的五个 Explorer 与执行器同用户、同会话，均为 Medium（RID 8192）、elevated=0；09-10 已保存的 UAC 只读记录为 EnableLUA=1、FilterAdministratorToken=1。因此，这个简单解释不符合当时本机记录。记录不能代替最新目标进程或线程安全上下文；后续可只读核对，不能直接据此改为 HKLM 登记。

另须区分两类机制：[HKCR 官方文档](https://learn.microsoft.com/en-us/windows/win32/sysinfo/hkey-classes-root-key)分别描述注册表合并视图和 COM 激活限制。COM 忽略用户级配置，不等于每个 RegOpenKeyExW(HKCR) 都自动返回机器级数据；独立 reader 成功也不能证明 Explorer 在故障时使用了相同视图。

## 2. 首次失败后不再重试：有明确复现的相似案例

2017 年的[开发者原始复现](https://stackoverflow.com/questions/43596688/reload-a-namespace-extension-in-explorer-exe-that-failed-to-load-previously)描述：Explorer 在 COM 未登记时先解析旧快捷方式，随后重新登记，My Computer 中出现扩展却不能进入，Process Monitor 没看到新的加载尝试，重启 Explorer 后恢复。作者使用系统自带 Shell Instance Object 复现，降低了自有 DLL 实现的影响。

**匹配之处：**重复登记、独立进程可工作、宿主不重试，与缓存差异值得对照。**不匹配之处：**该报告的重启会恢复，我们已完成的[冷会话对照](../V03-006/cold-session-execution.md)仍失败；我们也没有证明故障窗口曾先解析未登记的目标。因此，不能把现有全零缓存直接命名为“失败缓存”，更不能把再次重启作为已验证修复。

该讨论建议 SHFlushSFCache，但[微软接口文档](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shflushsfcache)仅承诺刷新特殊文件夹路径缓存，并提示未来版本可能改变或不可用；没有保证刷新所有命名空间 COM/CLSID 缓存。另一个[2018 年亲历报告](https://stackoverflow.com/questions/49504259/how-to-update-a-shell-namespace-extension-without-explorer-restart)也记录该函数和通知没有解决其更新场景。这些材料不足以支持直接调用它作为本案修复。

## 3. Windows 11 样例确有问题，但必须按故障阶段区分

[官方仓库 issue #349](https://github.com/microsoft/Windows-classic-samples/issues/349)报告 Windows 11 中 ExplorerDataProvider 的面包屑不更新，关联 [PR #332](https://github.com/microsoft/Windows-classic-samples/pull/332)增加 IExplorerPaneVisibility 支持。报告已能进入样例，属于进入之后的地址栏行为，不能解释当前入口缺失或消费属性全零；不据此修改我们的接口。

[微软样例说明](https://learn.microsoft.com/en-us/windows/win32/shell/samples-explorerdataprovider)还记录旧 SDK 的 x64 Release 配置遗漏 .def，以及非提权登记缺少自定义属性功能但仍应能运行命名空间。我们已有四个 DLL 导出和独立绑定成功证据，故不把这两个已知差别重新当成当前首要故障。

## 4. 扩展批准策略是有文档的条件，不是通用解释

[微软 EnforceShellExtensionSecurity 策略文档](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-admx-windowsexplorer#enforceshellextensionsecurity)说明，启用该策略时需要相应用户级或机器级 Approved 登记。[既有策略证据](../V03-006/shell-policy-evidence.json)和 09-09 进程记录没有发现该策略值；这是指定时间、键和视图的证据，不是排除所有系统或第三方策略。当前没有依据添加 Approved 项或改变安全设置。

## 5. 由资料决定下一轮，而不是继续扩大自制钩子

优先评估并使用微软 [Process Monitor](https://learn.microsoft.com/en-us/sysinternals/downloads/procmon) 的有界记录。官方说明支持注册表操作、输入输出信息、进程/用户/会话和调用栈；上述两个相似原始报告也使用了该工具。选择它的目的是获得独立于现有调试器异常预算的实际访问证据，不能预先保证它一定能看到尚未发生或已被缓存替代的访问。

执行顺序应为：

1. 复核新目标与登记执行器的实际用户、会话、完整性级别，确认前述文献条件是否成立；读取已有证据不足的当前值，不重跑不变产品套件。
2. 确认工具来源、运行权限、临时组件与清理方式；先让已知参考产生目标键的成功与失败记录，验证过滤范围。此文没有启动或安装该工具。
3. 捕获从登记前开始覆盖目标首次接触的完整短周期，绑定新进程身份，聚焦固定官方 CLSID、Attributes 和 DLL 加载；保留所需状态、时序和丢失事件信息，原始记录留本地并对归档脱敏。
4. 实际出现 NAME NOT FOUND、ACCESS DENIED、错误视图或加载错误时，再沿该条记录处理；若参考可见而目标没有记录，先检查覆盖时段、缓存及是否由其他进程完成，不把零事件当作“键不存在”。只有外部记录无法区分时才选择一个内部缓存分支观察点。

本阶段没有注册、刷新缓存、操作桌面、附加调试器、重启 Explorer 或更改系统策略。重型 Provider 仍不得进入 Explorer；本研究不改变原生 Explorer 入口要求、产品契约、依赖或 G1..G4 门禁。
