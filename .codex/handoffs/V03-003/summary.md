# V03-003 — Android 原生只读首版

状态：ready_for_review。实现 commit：`e24331afc7c8fc4806e172ba200342eed2895637`；分支 `codex/v03-003-android-native-read-client`。基于协调提交 `3f791d6`，仅拥有 `apps/android/**` 与本任务/交接文件。

## 交付

交付真正可安装的 Kotlin/Jetpack Compose Android 11+ 应用，手机和平板使用同一个 APK。单 HTTPS 源连接/登录/退出、八类资源库切换、真实目录导航历史、列表/网格、服务端全范围排序/筛选、有界分页、全部库/库内/目录子树搜索、信息详情/物理位置定位/复制相对路径、扫描状态已实现。

手机采用顶部切库、底部导航、详情抽屉；宽屏采用库、内容、详情三栏。浅深色读取 `packages/ui/workspace-theme.json`；200% 字体与表单滚动可操作，Activity 重建保留状态并重检会话与库权限。当前访问显示基线中文档位，表面不采用默认 Material 紫色。

最终试用包：`.runtime/releases/android/AssetLibrary-Android-0.3.0-readonly.1.apk`，30,064,814 字节，SHA256 `AFD7CBD3F387AEBC1F2D00EC34C3F824909673615546404B81AFD2A24A79B8CB`。APK v2 签名验证通过，明确使用 Android **调试试用签名**，不是正式生产/商店签名。Debug 与 Release 构建均通过，Release 原始输出未签名。

## 复用与边界

- 生成 Kotlin AssetLink SDK 按 sourceSet 原样引用；生成一致性通过。没有改 wire schema、服务端或数据库。
- 复用既有 Cookie/CSRF 只读用例和公开查询；UI/状态/HTTPS 适配不复制服务端权限、路径、索引、传输或文件操作规则。
- 地址/公开证书指纹可保存，密码、Cookie、CSRF 和会话仅内存；备份/设备迁移排除应用数据。
- TLS 默认系统验证；显式 pin 只信任指定来源的精确叶证书 SHA256，仍验证有效期与主机名。禁止跨源重定向，HTTP 401/403/404 在正文前生效；前台复核前遮蔽旧内容，授权拒绝清空数据。
- 每页最多 100 项、只保留当前页，虚拟列表/网格；导航最多 64 步、分页回退最多 64 页；服务端执行整个范围的排序/筛选。请求总期限 5 秒，响应上限 2 MiB，切换请求取消旧结果。
- AGP 9.1.1、Kotlin/Compose compiler 2.3.20、Compose BOM 2026.08.00、Gradle 9.3.1；compile 37（SDK 包 android-37.0）、target 36、min 30。依赖锁及 strict verification 已激活。平台 HTTP 使用标准库，没有第二套网络/界面框架。

## 验证与证据

21 个 JVM 测试、两类原生仪器测试共 23 个不同原生测试通过；仓库验证另执行 21 个迁移与 14 个架构回归，总计 58 个不同测试，0 失败、0 跳过。手机普通字体、手机 200% 字体、平板深色三栏分别运行 Compose 控件闭环；真实 Core + PostgreSQL + HTTPS 另行验证登录、100项分页、中文子目录、搜索、详情、账号隔离、Activity 恢复和退出。最后一次实际 Core 测试与 APK 来源一致。完整命令、初期失败如何修正、证据边界见 `tests.md`。

已提交截图：`screenshots/phone-real-core.png`、`phone-detail.png` 为真实 Core 隔离样例；`tablet-dark.png`、`phone-font200.png` 为真实 Android 模拟器上的明确内存样例。没有假缩略图或真实用户素材。

## 风险、技术债与合并

V03-001 协调线程已于 2026-09-08 09:27:05 UTC 完成依赖审计：475 个解析组件的 SHA256 验证元数据、101 个 runtime 依赖的 POM 许可证及在线 OSV 查询、锁、APK 大小和哈希均通过；此次查询未发现已知 advisory。公开报告为 [android-dependency-audit.json](android-dependency-audit.json)。初次大批请求遇到 TLS 重置后，协调线程使用保持 TLS 验证的 Node fetch 每 10 条查询，并验证回复时效、完整查询集合和 inventory SHA256；没有跳过验证。此次收尾只更新交接元数据，APK 保持原哈希。

没有 HyperOS 真机、Android 11 真机或 50 万资产原生端压测证据；API 36 模拟器/WHPX 证据不替代这些门禁。target 37 行为与正式签名待对应任务。内容预览/下载、同步、写操作、设备配对、主备切换未开放，完整 V0.3 与 Alpha 不在本任务宣告完成。

建议在 V03-001 规划/共享主题和 V03-004 测试支架之后合并；与 Windows 实现无文件争用。APK 和操作说明在 `apps/android/README.md`。ADR-0017 中的真实资产写入、Explorer 和完整版本门禁维持不变。
