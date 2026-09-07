# V01-025 Web 导航与产品工作区

状态：ready_for_review。分支 codex/v01-025-web-navigation-and-workspace；产品与测试 commit 46ff8e28a85bc44da2408a2c44c12cb5b6aa3ef4，基于 root c429f66。Web 范围已验收，真实 HTTPS/PostgreSQL 集成与 NAS 更新由协调器继续。

## 完成行为

首页、服务端分类资源库管理、真实目录、明确搜索范围和单库扫描任务页。Native History 与有界相对路径 URL 支持前进/后退/刷新/深链。URL 不保存绝对资源根、凭据、CSRF 或多选数组。搜索来源 from_library/from_path 只恢复导航上下文，all 查询不发送库或目录限制。合法 POSIX 目录 Album:2026 可以深链，绝对盘符路径继续拒绝。

列表/网格共用虚拟化数据与 Ctrl/Shift/方向键/Enter/Escape 选择。文件信息由 entries.get 单独授权读取，定位使用 anchor_entry_id，后续页只发送 cursor。宽屏右侧详情、窄屏可关闭 dialog 抽屉。明确复制相对路径；跨库时同时标注库名，不接管 Ctrl+C。首次扫描只有管理员明确动作才会启动；首页不轮询全库任务，不展示伪造资产数量或缩略图。

登记默认使用 storage_sources.list 的 default_root_path，保留高级范围内子目录输入和 NAS/Windows 帮助。分类修改复用 libraries.update_category 的 expected_category 与幂等请求；冲突后重新读取。

## 边界、复用与安全

仅 apps/web、tests/web、本任务与交接。复用 AssetLinkClient/生成 SDK、统一 5 秒传输期限及取消、SessionGate、内存 CSRF、扫描状态机适配、TanStack 虚拟化与原 CSS 语义 token 源。没有新语言、依赖、框架、数据库或共享契约修改，没有跨模块访问或服务端业务逻辑复制。

会话退出/失效或换身份清空当前查询详情 URL；工作区重挂载清空选择、请求与历史位置缓存。pageshow persisted 重新核验会话。列表/详情/搜索拒权清空对应数据；瞬时分页错误保留已授权页并显示错误。单条/目录响应核对调用范围，搜索条目核对所属库。

所有集合按服务器 cursor 分页，过滤/排序由服务器在完整目录范围内执行。渲染为 O(可见行)，内存 O(已加载条目)；历史位置所有写入入口均维持最多 100 项，仅在本次认证工作区内。返回时重新授权读取原先已加载的页恢复位置，不保存可跨身份复用的资产查询缓存。

## 验收证据

固定 Node 24.20.0 / pnpm 11.19.0，SDK 和 Web frozen/offline install（零下载）通过。format:check、lint、typecheck、生产 build 通过。浏览器最终 43 项全部通过：完整首轮 37/43（45.7 秒），6 个失败集中归因修正，受影响 7/7 通过（9.6 秒，含同一超时 fixture 的 body 例），未重复其他 36 项。

首轮修正包括下拉框精确可访问名称、StrictMode 下共享 dialog 的关闭与焦点恢复、选中项测试作用域、单一用户操作超时 fixture 避免初始挂载取消探针。首轮类型检查唯一错误为 History key 的模板字符串推断，已明确为 string。

生产构建 JS 290.45 kB / gzip 89.55 kB，CSS 25.84 kB / gzip 5.90 kB。查看了桌面列表、首页、网格详情和 390px 暗色手机页面/详情抽屉；未见横向溢出，手机详情未堆到页面底部。截图路径与命令见 tests.md。

## 使用标签与合并

/libraries 的「添加资源库」dialog；标签「存储源」「资源库名称」「资源库分类」，checkbox「高级：指定该范围内的目录」，高级字段「服务器目录」。资产列表 role=listbox / name=资产列表；条目 role=option，名称为「文件名，类型」，目录双击/Enter 进入，文件双击/Enter 查看文件信息。扫描页 /tasks?library=<id>，复用开始首次扫描/取消扫描/重试扫描。详情 dialog/aside 名称「资产详情」，关闭按钮「关闭资产详情」，定位按钮「定位所在目录」。

在 root 的共享合同/查询实现之后合入本分支（checkpoint 3fbbdd9 与修正 46ff8e2），由 root 运行真实 HTTPS/PG E2E 和最终仓库/发布门禁。当前截图与浏览器 fixture 不替代新版 NAS 实际部署证据，本任务不宣布整个里程碑完成。