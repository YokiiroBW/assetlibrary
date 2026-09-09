# V03-009 — 图片404兼容降级修正

旧Core61e6c0在有效会话下没有image GET路由，返回404。Android图片404本来只更新图片状态，不清空合法L0列表/会话；问题是复用了“条目已不可用”的全局提示。该结论来自固定旧Git路由/中间件与客户端调用链审查，不是新做的NAS实测。

生产变更仅 `protocol/Images.kt` 的图片专用404文案：`图片预览不可用，可查看文件信息`。401/403、全局ApiFailure及全部JSON404语义保持原样；没有猜Core版本、永久禁用图片源、改版本号或依赖。

新增状态回归先载入真实模型的rows/session，再分别触发缩略图和大图404，断言rows/libraries/session保留、提示文本准确、clearSession未执行，最后通过select恢复L0详情。复用并增强既有UI错误用例，在404页实际点击“查看文件信息”，检查详情中的相对路径与有效会话，再保留原403清会话检查。

2026-09-09针对检查通过：

```text
apps/android/gradlew.bat -p apps/android --no-daemon --dependency-verification strict :app:testDebugUnitTest --tests app.assetlibrary.android.workspace.WorkspaceModelTest :app:lintDebug
apps/android/gradlew.bat -p apps/android --no-daemon --dependency-verification strict :app:connectedDebugAndroidTest -Pandroid.testInstrumentationRunnerArguments.class=app.assetlibrary.android.WorkspaceUiTest#previewFailuresRetainFileInformationAndDeniedSessionClearsWorkspace
```

11项WorkspaceModelTest及1项原生UI通过，零失败/跳过；Lint与严格锁/完整性验证通过。原生设备为自有headless emulator-5584（API36，1080×2400px/density420、字体1.0/浅色）。只运行受影响范围，未重跑之前不变的37JVM/5UI全体或真实Linux图片联调，没有访问NAS。新增状态用例断言新文案，在未修正代码上会失败；其余保留性断言保护已有正确行为。

日志：`.runtime/evidence/image-404-targeted.log`、`image-404-ui.log`；原始JUnit归档`.runtime/evidence/image-404-regression/`。后续候选构建、哈希与来源会写入result.json及候选证据；旧1838b96 APK与真实Core874/Worker40d2d69证据单独保留，不将旧实测描述成新候选重新实测。
