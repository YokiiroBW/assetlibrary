# ADR-0022 — Explorer 大图与浏览偏好

- 状态：Accepted，V03-022 协调线程批准，2026-09-13
- 依据：docs/12_UI交互与视觉规范.md、ADR-0019/0021、image-preview-v1

## 决策

原生 Explorer 右侧图库增加当前页单图查看；Space、Enter、双击普通文件打开，Esc/返回关闭，左右键及按钮切换当前授权页内普通文件。目录与分页项不参与切图。Ctrl+Space 保留选择切换。快速查看不增加导航历史；关闭恢复原有图库/列表、选择和滚动位置。首版提供适应窗口显示，不扩展原件打开、编辑、同步或专业播放器。

Surface 只处理呈现与输入。View 持有预览索引、请求序号和授权页；打开/切换立即清旧图，取消旧请求；隐藏、关闭、导航、刷新、权限撤销和销毁均清除。只保留一个最新请求，不预取。预览期间释放该 View 全部缩略图，使用既有 16 MiB 像素准入预算容纳一张最多 10,240,000 B 的 PBGRA；晚到结果须同时匹配 generation、ticket、epoch/node。瞬时管道缓冲和 GDI DIB 独立有界，不宣称包含在持久像素预算内。共享模块两个 I/O worker 和现有任务上限，禁止额外无限队列。

新增 preview-v1 固定规格管道；thumbnail-v1 不变。Host 复用认证、SnapshotStore 映射、HTTP 许可、PNG 验证与隔离 WIC helper，以封闭 Thumbnail512/Preview1600 规格选择现有 HTTP variant。图片合计一个 HTTP 许可，保留目录容量。1600 大图不在 Explorer 内解码。沿用 helper 128 MiB Job、单进程、3 秒；必须实测最大尺寸冷启动，不足时提交修订，不隐式放宽。

浏览偏好为每 Windows 用户应用级默认，不按带 session epoch 的 PIDL 持久化。独立 `HKCU\Software\AssetLibrary\ExplorerPreferences`（64 位视图）下 `BrowseState` 单 REG_BINARY 值：16 字节，小端 magic `0x31504241`（ABP1）、version:u16=1、length:u16=16、mode:u32（Gallery=0/List=1）、densityDip:u32（96..256，默认176）。未知/损坏值整体回默认且不自动覆盖。每次创建 View 读取；显式模式/密度变化一次写完整值；不强推其他窗口，不在 SaveViewState/关闭时写，避免旧窗口覆盖新选择。写失败只在自有摘要提示，当前浏览继续。预览状态、缩放、选择、epoch/node、路径和凭证均不保存。

此兄弟键不属于 Setup 的 WindowsClient 注册事务，升级/回滚/卸载保留，重装可继续使用。禁止修改 WindowsClient 树结构或 Explorer Bags。无新增语言、依赖、框架、服务端端点、业务权限或原文件写入。

## 验证与边界

固定规格协议正反向边界、原 thumbnail 严格限额、取消晚到/快速切图/旧会话、当前页遍历、关闭恢复、UIA 隐藏旧图库条目与焦点返回、偏好损坏/写失败/多窗口、真实 Core→WIC→Explorer 显示均需证据。最大页 101，切图 O(101)，与全库 50 万规模无关。G4 仍按用户要求豁免；本次不宣称 NAS 图片引擎、签名、完整发布或人工认证完成。
