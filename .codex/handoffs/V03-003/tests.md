# V03-003 验证记录

## 环境

独立 worktree `C:/YOKI/Codex/AssetLibrary-worktrees/V03-003`；Temurin 21.0.12+8 从既有 V01-002 工具链只读复用。便携 SDK/Gradle/AVD 位于本任务 `.runtime`，无系统 PATH、服务或注册表变更。SDK 平台 android-37.0 revision 2、Build Tools 36.0.0；API36 google_apis x86_64 revision7、emulator37.1.11、WHPX可用。仅合成数据与隔离 Core/PostgreSQL fixture，未访问/扫描 NAS 用户素材。

调试签名最初使用工具默认本机 debug keystore，之后复制到本任务 Android user home 并在后续构建保持一致；它只用于试用 APK。不要将其误认为正式签名。

## 原生构建、静态与单元

设置本任务 `JAVA_HOME`、`GRADLE_USER_HOME`、`ANDROID_HOME`、`ANDROID_USER_HOME` 后，从根执行：

```text
apps/android/gradlew.bat -p apps/android --no-daemon --dependency-verification strict :app:assembleDebug :app:assembleRelease :app:testDebugUnitTest :app:lintDebug :app:dependencyInventory
```

结果通过。`app/gradle.lockfile` 固定全部真实解析配置，包含 AGP 延迟创建的 androidApis 文件配置空锁；`buildscript-gradle.lockfile` 固定插件 classpath；普通验证使用 LockMode.STRICT 与 SHA256 verification，不更新锁。首次锁建立执行 --write-locks 后重新使用 strict 验证。Debug APK 30,064,814 字节；Release unsigned 23,404,487 字节；最终试用包 apksigner verify 返回通过。

JVM：`ProtocolTest` 13 passed，`WorkspaceModelTest` 8 passed，0 failed/skipped。覆盖真实本机 TLS/Origin/Cookie/CSRF、未知 envelope/请求ID、401/403/404停滞正文优先拒绝、跨源重定向、响应/页上限、同库目录范围逃逸、断流取消、精确/错误/过期 pin、主机名错误、自签未信任、失败退出清会话、uint64边界、搜索取消、分页替换、服务端排序参数、授权失效清数据、离线旧快照、404清除、源切换、刷新回首屏以及有效账号但撤销库权限的前台重检。

初期失败已修正：SDK37依赖元数据与 AGP Kotlin sourceSet 接口；Android35才新增的 List.removeLast 改为兼容调用；备份排除；JDK测试实现限制 Origin 的差异只在测试 JVM 显式配置；Compose1.12使用v2仪器规则；调试签名路径统一。没有用测试跳过、忽略安全分析或改预期掩盖实现错误。CustomX509TrustManager 的局部 lint 说明对应 ADR0017 的严格 leaf pin，专门验证错误/过期/主机名不匹配时密码不会发送。OldTargetApi 仅说明试用 target36，非安全检查豁免。

## 真实 Android UI 与后端

```text
apps/android/gradlew.bat -p apps/android --no-daemon --dependency-verification strict :app:connectedDebugAndroidTest -Pandroid.testInstrumentationRunnerArguments.class=app.assetlibrary.android.WorkspaceUiTest
apps/android/gradlew.bat -p apps/android --no-daemon --dependency-verification strict :app:connectedDebugAndroidTest -Pandroid.testInstrumentationRunnerArguments.class=app.assetlibrary.android.RealCoreUiTest
```

选择 `ANDROID_SERIAL=emulator-5580`。WorkspaceUiTest 在手机默认、平板1280×800dp深色、手机200%字体分别通过原生登录/库/目录/详情/定位/搜索/退出操作；截图经过视觉检查。大字体表单和详情可以滚动，分页文本正常换行，操作仍可达。平板仍是三栏，未使用Web页面假冒原生运行。

RealCoreUiTest 单独对 V03-004 创建的真实 HTTPS + Core + PostgreSQL 隔离服务运行通过：实际账号登录/安全Cookie、库与100项首页/后页去重、中文子目录、库内名称搜索、详情、不可见普通账号空库、APP登录/翻页/详情/搜索、Activity.recreate后GET会话及权限查询、退出。测试配置只由临时私密JSON送入模拟器测试进程，没有写入源码或参数。测试完成已删除模拟器私密文件并移除 adb reverse，通知 V03-004 统一释放服务/数据库。

不同测试共23个（21 JVM + 2仪器）；同一UI测试的不同视口/重复验证不增加独立测试计数。初期仪器支架失败（UTP安装清理导致私密文件不可用、fixture中文目录在第二页）仅纠正测试部署/选取方式；最终两个仪器类均实际执行并通过。

非秘密证据：`.runtime/evidence/phone/final-real-core-results`，`.runtime/evidence/tablet-dark/test-results`，`.runtime/evidence/phone-font200/verified-results`。四张代表截图复制到本 handoff `screenshots/`，不依赖临时路径即可审查布局。原始 JVM/JUnit、Lint 报告仍在 `apps/android/app/build/reports` / `build/test-results`。

## 仓库与依赖

`python -I -B scripts/generate_assetlink_sdks.py --check`、`python -I -B scripts/export_native_theme.py --check`、`python -I -B scripts/validate_architecture_baseline.py` 与完整 `python -I -B scripts/verify_repository.py` 通过；使用 bundled Python 的绝对入口，未安装全局Python。仓库验证另执行21个迁移、14个架构回归（全部通过），因此含原生23项后为58个不同测试。Alpha审计保持合法blocked，不表示版本已发布。

V03-001 协调 owner 于 2026-09-08 09:27:05 UTC 完成根级依赖审计：475 个解析组件的 SHA256 metadata、101 个实际 runtime 依赖的 POM 许可证和在线 OSV、锁与 30,064,814 字节 APK 大小/哈希均通过，查询未发现已知 advisory。公开结果复制为本目录 `android-dependency-audit.json`。

初次 100 条批量 TLS 被重置，协调 owner 改用保持 TLS 验证的 Node fetch 每 10 条查询，保存完整 queries/results、UTC 和 inventory SHA256 到其 `.runtime/android-osv-response.json`。协调验证脚本的 `--osv-response` 检查时效不超过 24 小时、查询集合完整及 inventory 哈希一致，5 个拒绝异常输入的脚本测试由协调线程通过；它们不重复计入本任务 58 个测试。没有绕过 TLS 或将缺失查询当作通过。本次只补公开审计报告与交接元数据，未重建 APK 或重跑已通过测试。

无 HyperOS/Android11 真机、50万资产客户端压力或正式签名证据；生产预览/同步/文件写入不在可用能力中。平台缺证据与全版本发布仍保留开放状态。
