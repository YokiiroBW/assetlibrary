# V03-009 测试记录

独立worktree：`C:/Users/Administrator/.codex/worktrees/85ca/AssetLibrary`；冻结契约基线38aedca。工具为既有Temurin21.0.12+8、Gradle9.3.1、AGP9.1.1、Kotlin2.3.20、Compose BOM2026.08.00，SDK37 revision2/BuildTools36.0.0、API36 google_apis x86_64/emulator37.1.11/WHPX。所有状态、AVD与产物在本任务.runtime，旧JDK/SDK只读使用。

## 构建、静态与JVM

设置本任务JAVA_HOME、ANDROID_HOME、ANDROID_USER_HOME、GRADLE_USER_HOME后执行既有真实入口：

```text
apps/android/gradlew.bat -p apps/android --no-daemon --dependency-verification strict :app:assembleDebug :app:assembleRelease :app:testDebugUnitTest :app:lintDebug :app:dependencyInventory
```

成功。后续完整代码审查补齐排队预算、大图关闭释放、立即Main取消与迟到拒权隔离、可访问平移、最近任务隐私后，重新执行实际变化涉及的Debug/Release/JVM/Lint与仪器任务，最终日志 `.runtime/evidence/verified-native-phone.log` 成功。普通验证不更新锁；严格依赖锁与SHA256验证保持启用。Gradle内部NO-SOURCE/UP-TO-DATE/预构建标记不折算成测试跳过。

最终JVM共37项，失败0/跳过0：ProtocolTest20、ImagePayloadTest3、WorkspaceImagesTest4、WorkspaceModelTest10。覆盖TLS/主机名/pin/账号、UUID精确路由和无图片CSRF、早期401/403/404、重定向/MIME/长度/JSON冒充PNG、20秒实时时限和429最多两次、PNG CRC/像素/动画/源元数据、2并发/32可见/取消与迟到拒权、前后台/账号/导航/返回与原有浏览搜索分页。

## Android真实控件与解码

```text
apps/android/gradlew.bat -p apps/android --no-daemon --dependency-verification strict :app:connectedDebugAndroidTest -Pandroid.testInstrumentationRunnerArguments.class=app.assetlibrary.android.WorkspaceUiTest
```

`ANDROID_SERIAL=emulator-5584`；以下每个环境均实际执行5项，零失败/跳过：

- 手机1080×2400px、density420、默认字体/浅色：`.runtime/evidence/phone/test-results`。
- 平板2560×1600px、density320（1280×800dp）、深色：`.runtime/evidence/tablet-dark/test-results`。
- 手机相同尺寸、font_scale=2.0、浅色：`.runtime/evidence/phone-font200/test-results`。

5项分别检查原有登录/目录/搜索/详情/退出，预览缩放/双击/关闭/后台，256px采样与alpha/每次缓存命中先重新请求/后台换代，Main.immediate两个在途任务安全取消，八类预览失败L0降级及403清工作区。同一用例在三个视口不重复计入不同测试数。布局6张截图已拉取并逐张检查，位于handoff screenshots/。测试图片由Android平台绘制明确几何样例并编码PNG，不能代替真实Core派生图证据。

初轮视觉截图发现Dialog全局安全标记导致黑屏截图；改为Android33+的最近任务截图禁用与前后台清图，Android30..32保留安全窗口。没有修改测试预期或关闭生产授权；新预览截图可检查，最近任务隐私仍启用。avdmanager对旧SDK缺devices.xml输出诊断但实际生成完整Pixel6 AVD，随后WHPX开机、安装与全部仪器执行成功；不将诊断自身记作平台失败或通过。

## 仓库、依赖和包

```text
python -I -B scripts/verify_repository.py
python -B tests/architecture/check_release_gates.py --target v0.1-start
python -I -B scripts/validate_android_dependencies.py --inventory apps/android/app/build/reports/runtime-dependency-inventory.tsv --gradle-cache .runtime/gradle-home --apk apps/android/app/build/outputs/apk/debug/app-debug.apk --audit-output .codex/handoffs/V03-009/android-dependency-audit.json --osv-response C:/YOKI/Codex/AssetLibrary-worktrees/V03-001/.runtime/android-osv-response.json
```

Bundled Python3.12使用绝对入口。最终仓库验证通过（日志`.runtime/evidence/repository-final.log`），包含21迁移+14架构回归、生成SDK/主题、依赖/语言/模块/handoff检查；Alpha合法blocked、v0.1-start允许。新增预览contract只由协调提交合入，本窗口未编辑。

完整依赖审计通过：475验证组件，101 runtime许可与OSV，无新增依赖。OSV回执来自同日V03-001真实TLS查询，现有验证器再次核验24小时时效、精确inventory SHA256与完整查询集合；无已报告advisory。APK30,278,429 bytes，SHA256 `8461631cdd24fa077abe35794a92ab599cdff1036cfff7aa4cd743595fea158b`；复制交付副本后重读一致。apksigner verify --verbose通过v2签名，明确为前序调试试用签名。Release unsigned23,486,407 bytes。

不同已通过用例总计79（37 JVM+5原生样例仪器+2真实Core仪器+35仓库回归），不重复累计基线/视口/重跑。其他脚本按通过命令记录，不混算测试数。

## 真实Core图片联调与清理（2026-09-08补充）

主协调READY实例：真实Linux Core874fb6a/Worker40d2d69、PostgreSQL、148个合成文件、固定仓库fixtures/image-preview-v1。经协调SSH转发与本设备adb reverse连接同一localhost HTTPS，仍执行精确叶证书/主机名/有效期校验。连接JSON只在模拟器临时私密路径，未打印内容、写源码或命令参数；未改共享样例/服务器。

```text
apps/android/gradlew.bat -p apps/android --no-daemon --dependency-verification strict :app:connectedDebugAndroidTest -Pandroid.testInstrumentationRunnerArguments.class=app.assetlibrary.android.RealCoreUiTest,app.assetlibrary.android.RealCoreImageUiTest
apps/android/gradlew.bat -p apps/android --no-daemon --dependency-verification strict :app:connectedDebugAndroidTest -Pandroid.testInstrumentationRunnerArguments.class=app.assetlibrary.android.RealCoreImageUiTest
```

第一项在手机实际font_scale=2.0、1080×2400px/density420/浅色执行2/2通过，第二项在2560×1600px/density320（1280×800dp）/字体1.0/深色执行1/1通过，全部零失败/错误/跳过。Gradle分别65秒和33秒，源码/APK相关任务UP-TO-DATE；没有重跑37JVM/5样例仪器等未变自测。原始结果在`.runtime/evidence/real-core-phone/test-results`和`real-core-tablet/test-results`，公开摘要、类名与报告SHA256见handoff `real-core-integration.json`。

实际网络PNG检查：JPEG/PNG/WebP的两个variant、EXIF6方向、alpha、中文.dat同内容派生字节一致；SVG、非图PNG、超大头PNG、截断JPEG返回415/422；不可见账号对已知图片返回404。真实控件完成登录/分页/目录/搜索/详情/退出、实际缩略图/全屏图、放大/复位/关闭以及Activity.recreate后的重新会话/库权限验证和图片加载。4张截图 `screenshots/real-core-*` 已逐张实看。源码/APK完全未修改，SHA仍为8461631cdd24fa077abe35794a92ab599cdff1036cfff7aa4cd743595fea158b。

平板测试成功后、清理前模拟器与adb连接意外消失；未把当次失败清理记作成功。仅重启本任务AVD恢复截图与私密文件清理，2026-09-08T15:57:10Z确认设备connection文件不存在、reverse列表为空，再显式emu kill。15:57:29Z确认adb devices为空及5584/5585监听为0。退出原因未确定，不猜作产品问题或已验证系统稳定；没有关闭共享Core/SSH或改样例，root负责服务器侧原文件hash/mtime和资源最终清理。

本次仅增真实联调证据与handoff状态为ready_for_review，已通过的源码自测/构建不重复。缺HyperOS/Android11实机、50万资产压力、正式签名与完整V0.3发布证据；这些门禁不由本任务解除。

## 图片404兼容降级修订（2026-09-09）

源码f143292仅修改图片专用404文案，另新增已载rows/session保留与恢复L0状态用例、增强现有UI“查看文件信息”点击断言。11项WorkspaceModelTest及1项相关原生UI在自有headless手机默认视口通过，0失败/跳过；Lint、strict lock/verification通过。先提交修复与测试结果，再执行Debug/Release候选构建通过。命令/原始JUnit摘要与哈希详见image-404-fallback.md和image-404-candidate.json。

新候选30,425,593 bytes、SHA256 `6d959b5ac000b6a7a932c9d50298b51a25c9992d92ed748c9371e7b710444ddb`，APK v2调试试用签名通过；versionCode=2和versionName=0.3.0-preview.1保持不变。101 runtime/475组件及候选体积哈希审计见android-dependency-audit-404-fallback.json，复用仍有效的同inventory OSV回执；原1838b96 APK与真实Core874/Worker40d2d69验收证据均保留，未重复执行不变全套或真实Linux图片/未做NAS实测。

累计不同用例80（此前79+新增状态用例1）；当前修订实际执行12项（含11个既有逻辑用例），不把累计数说成此候选全量重跑。自有AVD已关闭，01:13:11Z确认adb设备为空/5584和5585监听0，本次未创建私密连接文件或reverse，未触碰Explorer桌面或外部服务。
