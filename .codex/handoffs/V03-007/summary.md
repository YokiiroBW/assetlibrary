# V03-007 — 服务端真实派生图片（partial）

工作区 C:/Users/Administrator/.codex/worktrees/021c/AssetLibrary，分支 codex/v03-007-image-preview-server。Core审查点0a746e2，严格sBIT修正4275815，源清理/名额隔离a277178。真实出图已通过；当前等待Windows清理源码与其测试链接成组完成、共享基线测试修正和root最终集成，不宣告完整V0.3或NAS发布。

## 已实现

- 冻结GET按稳定库/entry UUID和固定variant返回服务端重编码PNG：thumbnail最长边512/2MiB，preview1600/12MiB，无放大、方向归一、保留alpha。JPEG/PNG/WebP静态图；动画、16bit PNG、SVG/其他格式诚实降级。
- GatewayAuth初始/末次库授权、Host末次session复核、写首字节前源核验；不可见与缺失统一404。精确图片endpoint metadata为15秒，旧JSON仍5秒；图片401为unauthenticated，旧JSON保持authentication_required。
- 独立可回收source broker逐层no-follow句柄、流式稳定副本、strong hash与当前文件身份/mtime复核。Windows拒绝ADS/设备别名/重解析，Linux openat/statx拒绝链接和特殊文件。仅持久观察按Npgsql微秒编码比较，当前stamp仍全精度，原文件不改。
- Core不引用Skia程序集；独立NativeAOT只接有界稳定字节。源32MiB/40MP，GC64MiB单一配置。内容hash+renderer/规格缓存64MiB/256项；2名额保持到响应及实际清理结束，迟到/未reap或清理失败隔离名额，不积无限worker/queue。
- 完整PNG每块CRC、单一首IHDR、固定8bit RGB/RGBA、至少IDAT、IEND恰好EOF；只允许已实测sRGB与固定每通道8的sBIT，拒绝文本/EXIF/ICC/动画/未知块，Host不做媒体解码。

## 验证与真实范围

本地真实Core/PostgreSQL/HTTPS→source broker→LPAC NativeAOT→PNG校验的图片与信任2/2通过，PREVIEW_EXPECT_AVAILABLE=1：实际PNG缩略图、JPEG/WebP/EXIF6/透明预览、损坏/超限/非图降级，401差异、Origin/POST-CSRF和invisible404。没有以503或假服务算出图。显式stop后148合成原件hash/mtime、Host/PG/角色/证书/临时目录按原fixture清理verified。见core-integration.md和tests.md。

真实PG精度1/1、真实Host source进程1/1、PNG及变异3/3、Core容量/清理7/7、前后授权3/3均有独立结果。Windows源组原8/9及新微秒例通过，叶symlink本机缺权限；root在开发Linux对老源组9/9补证。Host/Auth包62/63，唯一现存失败是共享DatabaseReadinessTests硬编码18而生产manifest21；未改数据库/迁移来迁就断言。

root在dev-230非root、cap0、无GC覆盖的默认Linux NativeAOT验证完整corpus、TSYNC文件/网络/跨进程/exec拒绝、native内存拒绝、CPU约3秒内核终止；既有低权限身份实际创建253线程后NPROC256封顶并回收。这是开发Linux证据，不替代NAS目标内核。root另已完成Linux真实Core图片2/2，并进行Web/Android联合验收；统一服务清理由root持有。

## 依赖、边界与后续

SkiaSharp及Linux.NoDependencies固定4.151.2，正常lock与两RID发布lock分开。4个正常NuGet包官方签名/内容hash、现有许可证政策和transitive已知漏洞查询通过；完整139775B native notices与MIT随Worker发布且固定hash验证。详见decoder-dependencies.json；NuGet查询不等于证明所有native漏洞不存在。

Windows在ca1d235后交V03-006单写，已提供真实LPAC出图guard；追加journal/取消/恢复3文件待与本任务csproj链接合组。共享Startup Task/清理名额语义由V03-007负责。无生产新语言/第二框架/数据库或跨模块写表；获准临时C++ COM观察器仅作平台定位，不进入产品。

末阶段仍需转交源码组合后的StartAsync/故障回归、最终format/affected checks、root修正共享18断言及最终平台/包/客户端验收。Provider、Explorer、资产写入和完整发布门禁保持原状。合并顺序：root wire/pins/ADR → 本Core/Worker/共享清理 → Windows专属收尾 → root跨端与目标平台证据。
