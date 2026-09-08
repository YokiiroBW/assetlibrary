# V03-007 — 服务端真实派生图片（partial）

工作区 C:/Users/Administrator/.codex/worktrees/021c/AssetLibrary，分支 codex/v03-007-image-preview-server。Core审查点0a746e2，严格sBIT修正4275815，源清理/名额隔离a277178，嵌套迟到清理de83a3c，Windows异步启动适配25a7bed。Windows清理源码与测试链接已成组，真实出图已通过；当前等待Windows剩余原生故障矩阵及root打包/最终集成，不宣告完整V0.3或NAS发布。

## 已实现

- 冻结GET按稳定库/entry UUID和固定variant返回服务端重编码PNG：thumbnail最长边512/2MiB，preview1600/12MiB，无放大、方向归一、保留alpha。JPEG/PNG/WebP静态图；动画、16bit PNG、SVG/其他格式诚实降级。
- GatewayAuth初始/末次库授权、Host末次session复核、写首字节前源核验；不可见与缺失统一404。精确图片endpoint metadata为15秒，旧JSON仍5秒；图片401为unauthenticated，旧JSON保持authentication_required。
- 独立可回收source broker逐层no-follow句柄、流式稳定副本、strong hash与当前文件身份/mtime复核。Windows拒绝ADS/设备别名/重解析，Linux openat/statx拒绝链接和特殊文件。仅持久观察按Npgsql微秒编码比较，当前stamp仍全精度，原文件不改。
- Core不引用Skia程序集；独立NativeAOT只接有界稳定字节。源32MiB/40MP，GC64MiB单一配置。内容hash+renderer/规格缓存64MiB/256项；2名额保持到响应及实际清理结束，迟到/未reap或清理失败隔离名额，不积无限worker/queue。
- 完整PNG每块CRC、单一首IHDR、固定8bit RGB/RGBA、至少IDAT、IEND恰好EOF；只允许已实测sRGB与固定每通道8的sBIT，拒绝文本/EXIF/ICC/动画/未知块，Host不做媒体解码。

## 验证与真实范围

本地真实Core/PostgreSQL/HTTPS→source broker→LPAC NativeAOT→PNG校验的图片与信任2/2通过，PREVIEW_EXPECT_AVAILABLE=1：实际PNG缩略图、JPEG/WebP/EXIF6/透明预览、损坏/超限/非图降级，401差异、Origin/POST-CSRF和invisible404。没有以503或假服务算出图。显式stop后148合成原件hash/mtime、Host/PG/角色/证书/临时目录按原fixture清理verified。见core-integration.md和tests.md。

真实PG精度1/1、真实Host source进程1/1、PNG及变异3/3、Core容量/清理7/7、前后授权3/3均有独立结果。Windows源组原8/9及新微秒例通过，叶symlink本机缺权限；root在开发Linux对老源组9/9补证。Host/Auth包62/63，当时唯一失败是共享DatabaseReadinessTests硬编码18而生产manifest21；root后以66b25ce修正陈旧断言并验证5/5，本分支合入为f9b7eb7，数据库/迁移未改。历史失败证据保留。

root在dev-230非root、cap0、无GC覆盖的默认Linux NativeAOT验证完整corpus、TSYNC文件/网络/跨进程/exec拒绝、native内存拒绝、CPU约3秒内核终止；既有低权限身份实际创建253线程后NPROC256封顶并回收。这是开发Linux证据，不替代NAS目标内核。root另已完成Linux真实Core图片2/2，并进行Web/Android联合验收；统一服务清理由root持有。

## 依赖、边界与后续

SkiaSharp及Linux.NoDependencies固定4.151.2，正常lock与两RID发布lock分开。4个正常NuGet包官方签名/内容hash、现有许可证政策和transitive已知漏洞查询通过；完整139775B native notices与MIT随Worker发布且固定hash验证。详见decoder-dependencies.json；NuGet查询不等于证明所有native漏洞不存在。

Windows在ca1d235后交V03-006单写，已提供真实LPAC出图guard；追加journal/取消/恢复3文件已以a21a145交付，本分支合为0e6027d并与本任务csproj链接合组。共享Startup Task/清理名额语义由V03-007负责。无生产新语言/第二框架/数据库或跨模块写表；获准临时C++ COM观察器仅作平台定位，不进入产品。

StartAsync组装、format/affected checks与实际HTTPS已验证；末阶段仍需V03-006原生故障/资源/COM矩阵、root最终平台/包/客户端验收。Provider、Explorer、资产写入和完整发布门禁保持原状。合并顺序：root wire/pins/ADR → 本Core/Worker/共享清理 → Windows专属收尾 → root跨端与目标平台证据。

2026-09-09后续审查：de83a3c修复延迟startup本身抛出嵌套cleanup-pending时外层reaper提前失败的问题；现在等待内层实际Completion，成功才能归还名额，真实清理失败继续隔离。新增2项回归，Core Release零警告构建通过；root以433ad01集成此最小提交后实际编译并执行2/2通过（27ms、0skip），未引入尚缺源的Windows链接。没有变更公开接口、wire或依赖。

Windows完整源组装后，本地format通过、Preview/Core/Host/WebGateway Release零告警；核心/授权/PNG/reaper/Windows生命周期及真实LPAC共21/21，HTTPS实际2/2、0skip。新fixture停止后CLEANUP verified。不同逻辑用例更新为132通过/0失败/1本机叶symlink缺证据，Windows新5生命周期及1直接AOT用例计入，其余重复运行不重复计数。

只读发行检查后，root通过f673a12精确授权NAS的5文件接线，已由d18b224实现。通用Docker/原生/Windows trial的独立Worker打包仍未修改。NAS同提交镜像构建和目标平台验收仍交root；本任务没有部署或启用生产NAS。最初检查及后续范围见packaging-review.md。

NAS打包d18b224：现有server构建层单独发布linux-x64 AOT，使用已提交RID锁和同一SDK/source；仅构建层加入clang/zlib。运行镜像固定6个图片产物（exe、Skia、2声明、checksum、source revision），按0555/0444提供nonroot读/执行。Compose仅透传显式变量，默认空。打包工具创建从不启动的所属检查容器，提取并严格检查文件集合、commit/SHA/ELF/权限，finally删除检查容器；清单绑定image ID、commit、锁与文件hash，平台状态仍not_executed。新增打包组本机9过/2POSIX未执行，发行整组47过/0失败/5明确环境缺口。当前任务逻辑汇总141过/0失败/3本机平台缺证据；其他既有发行测试单独记录不重复累计。

Windows owner的53bc455继承Modify目录兼容修复已合为39ce06d；其原始7/7结果由V03-006持有，不重复计入本任务本地总数。

root开发Linux检查点0f4d0c68cf693a854b4dde1932cbe1c623b1b6c2已补新NAS包11/11（含POSIX2项、38ms、0skip）；当前143过/0失败/1本机源叶symlink缺证据。真实同提交Docker builder正在运行，不能把测试通过当作镜像或NAS目标验收完成。
