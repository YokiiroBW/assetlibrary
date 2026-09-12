# V03-019 交接摘要

状态：ready_for_review。分支 `codex/v03-019-native-gallery-surface`，代码终点 `d215431`。实现和同源测试已交付；真实 Core/Explorer、安装包和默认启用由协调线程统一验收，本文不宣布完整 V0.3 完成。

## 交付行为

提供同进程 Win32 子窗口、GDI/AlphaBlend 保比例行对齐图库、列表、96–256 DIP 密度、原生按钮、目录分区、完整文件名可访问语义、键盘范围/多选和宿主回调。极端 512×1/1×512 图按真实比例 contain；WM_PRINTCLIENT 明确裁剪到画布，滚动不会覆盖工具栏。隐藏、Clear、外部 DestroyWindow 和回调删除 Surface 均先清当前名称、选择和像素。

可见文件集合覆盖当前页全部 101 项；16 是 V03-021 请求批次上限，不截断可见集合。每视图仅持有不可变 PBGRA 共享缓冲，按 capacity 第二道限制 16 MiB；预算拒绝将该项置为“预览不可用”，不会永远等待。没有持久 GDI 位图副本，单次绘制临时 DIB 最多 1 MiB。

原生 UIA List/Selection 与完整有界 ListItem/SelectionItem、目录 Invoke、Scroll/ScrollItem 复用同一 AccessibleModel；MSAA 保留兼容。generation/presentation 退役使旧对象不能读取旧页或新页名称。辅助技术 Invoke 只排入本画布下一轮消息，实际回调按页修订验证。加载、访问拒绝、服务不可用、空目录和等待图片有明确无障碍说明。

## 生命周期与复用

窗口/事件执行只临时保留 View；providerLifetimeOwner 为独立 Folder/DLL pin，不能传拥有 Surface 的 View。弱 provider 缓存不构成 State→provider→View 环。每 STA 最多 512 个 live providers；第一个对象暴露前创建一个系统 STATIC 消息窗，退休队列固定 512 槽，无退役分配、无重复入队。先统一失效，后于非 SendMessage 输入同步边界逐对象 UiaDisconnectProvider；每次最多处理 512 个。调度窗自身持独立 Folder pin，摘除 subclass/DestroyWindow 后才放开。自身 canvas 在 WM_DESTROY 明确清理 UIA raised-event map，保存 canvasIdentity 保证显式 Destroy 先清活动窗口后仍能执行。

调度创建/OOM失败拒绝暴露；消息和定时器同时失败或连续八次同步边界重试耗尽，保留有界引用及 DLL pin并报告诊断，不强制卸载、不额外 Release、不调用全局 UIA/COM 清理。事件只发送给本model已经暴露的root，不因其它应用全局listener主动创建provider。native root 失败期间不再返回自定义 MSAA 对象以回避旧桥 fallback。

复用公开 snapshot/PIDL 语义、既有类型及完整名、宿主导航/刷新/上下文菜单回调和生成 Web 主题。没有复制权限、文件身份、网络、解码或业务操作。50 万资产下仍只处理当前 ≤101 项，布局与所有可访问查询最多 O(101)，provider 总数另有硬上限。

## 依赖、安全与风险

仍是 C++17/Win32 系统适配；系统 GDI、msimg32、oleacc、uiautomationcore 按 root 批准的 ADR-0021 使用，不新增第三方框架。root 提供主题生成器提交 8ec1fed/4697374，本分支对应拣选 3e27b0d/9d85ac7，不要向 root 重复合入。生产 CMake/import 白名单、ADR和请求共享契约由 root 所有。

本任务未注册生产 COM、未安装、未操作 Explorer/真实 NAS、未在 Explorer 解码或联网。harness 合成 PBGRA 只验证 UI，不能作为产品数据。未实现原图/1600px预览、小图弹层、标签时间轴或写操作；Space 为选择切换。G4 用户豁免，未做长时间门禁。退役前已被客户端缓存的内容不可撤回，保证的是后续 current 查询不返回旧数据。

## 合并顺序

root 已逐步合入 API、基础控件、主题、窗口/裁剪和 101 可见项修复。当前新增顺序为 `391f3ba` → `62364c2` → `ab50a05` → `124df8e` → `4eede49` → `7446e99`。V03-021 必须设置 providerLifetimeOwner=folder_ 并将 `gallery/Uia.cpp` 与系统 uiautomationcore 加入生产目标。随后 root 跑整包与真实 Explorer/Core；此同源验证不能替代该证据。

详见 tests.md 与 accessibility-investigation.md；result.json 列明边界和验证结果。

## 实际GUI文案复核补充

`d215431` 修正正常非文件项的无障碍描述：Ready 时复用 TypeText(kind)，只有非Ready状态才调用 StatusText。正常资源库/目录/下一页/链接项不再误报后台服务不可用；不改变共享StatusText、实际状态、接口或业务行为。四种类型的HelpText/ItemStatus正控以及AccessDenied负控已通过严格GalleryUiaTests构建与定向CTest。
