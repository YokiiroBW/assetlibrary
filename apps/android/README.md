# Android / Tablet — 原生只读首版

Android 11+ 的 Kotlin/Jetpack Compose 应用。手机顶部切库、底部导航和文件信息抽屉；宽度至少 960 dp 时为库导航、内容、详情三栏。遵循 ADR-0017、docs04/12/20，读取同一份 `packages/ui/workspace-theme.json`，不嵌入 WebView。

## 使用

安装交接记录中的 `AssetLibrary-Android-0.3.0-readonly.1.apk`。它使用明确标识的 Android 调试试用签名，不是商店或正式生产签名。输入与 Web 相同的 HTTPS 根地址和账号。自签 NAS 必须显式填写已核对的叶证书 SHA256；主机名与有效期仍需正确。不会自动接受首次遇到的证书。

已提供库分类/切换、真实目录历史、列表/网格、服务端排序/过滤、范围搜索、文件信息/定位/复制相对路径、扫描状态、退出、前台会话复核与失败恢复。库管理在 Web 进行；内容预览、原文件下载、同步、写入、主备切换和设备配对保留后续门禁。类型图标不代表已生成内容缩略图。

只持久保存 HTTPS 地址和用户配置的公开证书指纹。口令、Cookie、CSRF 和登录状态不保存到磁盘或 Activity SavedState，关闭进程后重新登录。备份/设备迁移均排除应用数据。401/403 清除账号和所有列表；404 清除失效结果；网络错误标记旧快照。前台恢复先验证到期和 GET 会话身份，未确认时遮蔽旧工作区。退出网络失败会明确表示未确认服务器撤销。

## 构建与真实命令

- Temurin JDK `21.0.12+8`，Gradle wrapper `9.3.1`（SHA256 与既有 SDK 一致）。
- AGP `9.1.1`，Kotlin/Compose compiler `2.3.20`，Compose stable BOM `2026.08.00`。
- SDK package `platforms;android-37.0`，Build Tools `36.0.0`；compile 37，target 36，min 30。target 37 行为变化不在此 Android 16 试用验证范围内。
- `ANDROID_HOME` 指向 SDK，`JAVA_HOME` 指向精确 JDK；本地建议 `ANDROID_USER_HOME`、`GRADLE_USER_HOME` 指向本 worktree `.runtime`。

从仓库根执行（Linux 把 `.bat` 换为 `bash apps/android/gradlew`）：

```text
apps/android/gradlew.bat -p apps/android --no-daemon --dependency-verification strict :app:assembleDebug :app:assembleRelease :app:testDebugUnitTest :app:lintDebug :app:dependencyInventory
apps/android/gradlew.bat -p apps/android --no-daemon --dependency-verification strict :app:connectedDebugAndroidTest -Pandroid.testInstrumentationRunnerArguments.class=app.assetlibrary.android.WorkspaceUiTest
```

设置 `ANDROID_SERIAL` 选择唯一测试设备。`WorkspaceUiTest` 使用明确的内存样例检查真实 Compose 控件；真实 Core 联调单独运行 `app.assetlibrary.android.RealCoreUiTest`，需要 `tests/integration/native-clients/serve.py` 提供的隔离 Core/PostgreSQL 实例。先 `adb reverse tcp:<port> tcp:<port>`，将私密连接 JSON 推送到模拟器 `/data/local/tmp/assetlibrary-native-connection.json`；不把账号写入源码、参数或日志。测试后移除该文件和反向端口。仪器截图暂存 `/data/local/tmp/real-core-*.png` 或 `native-*.png`，必须拉取并验证，不以编译代替真实运行。

`app/gradle.lockfile` 和 `buildscript-gradle.lockfile` 固定依赖图，Gradle strict lock 与 `gradle/verification-metadata.xml` 固定完整性。只有批准的依赖更新才运行 `--write-locks --write-verification-metadata sha256 :app:dependencyInventory`，并完成来源/漏洞/许可证评审；普通检查不得更新锁。

`dependencyInventory` 输出完整及 runtime 两份 TSV（仅外部 Maven 坐标），供根级 `scripts/validate_android_dependencies.py` 做许可证、OSV、SHA256 与 APK 体积审计。Compose、AndroidX、Kotlin 均沿官方更新渠道，同一冻结版本线经平台测试升级。HTTP 使用平台 `HttpsURLConnection`，没有新增网络框架；Compose 是唯一 Android UI 框架。生成 Kotlin SDK 通过 sourceSet 直接编译，不复制或编辑 SDK 源码。

## 性能与验证边界

请求每页最多 100 项，界面只保留当前结果页并使用 Lazy 列表/网格；导航最多 64 步、分页回退最多 64 页，不会拉取全库统计。超过回退窗口时可刷新回首屏。服务端对整个查询范围排序/筛选，客户端只展示。响应最多 2 MiB，总请求期限 5 秒，切换查询会取消旧请求；HTTP 拒绝在读取正文之前生效。

JVM 协议测试使用即时生成的 localhost TLS 证书，覆盖错误/过期 pin、主机名错误、未信任证书、跨源重定向、失配请求、超大响应/页、同库越界、取消、错误退出和 uint64 边界；状态测试覆盖取消搜索、失效权限、刷新游标与前台复核。模拟器证据不替代 HyperOS 真机、50 万资产实测、完整同步/预览或正式发行门禁。
