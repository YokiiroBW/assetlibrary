# V03-007 图片派生预览提案（等待主协调冻结）

2026-09-08。工作区 `C:/Users/Administrator/.codex/worktrees/021c/AssetLibrary`，分支 `codex/v03-007-image-preview-server`，基线 `70ce45c743ba6d026e695c118d1927d291b37918`。本文是提案，不是已交付能力或解除门禁的证据。

## 1. 可先冻结的客户端契约

`GET /assetlink/v1/libraries/{library_id}/entries/{entry_id}/image?variant=thumbnail|preview`。两个 ID 必须为非空 UUID，variant 必填且仅固定枚举；拒绝额外参数、重复参数和客户端路径。沿用既有 HTTPS、Cookie、Host/Origin/Fetch-Metadata 信任检查，无 CORS、无查询串凭据。GET 不引入新的 CSRF 协议。

- `thumbnail`：最长边 256px；`preview`：最长边 1600px。保持比例，不放大，无裁剪。
- JPEG、PNG、WebP，通过真实字节签名和解码器识别双校验；仅静态单帧。动图/APNG/动画 WebP 明确 415，不将首帧冒充动画支持。SVG、HTML、PDF、文本和其他格式保持 L0；文本/PDF 属于下一阶段。
- 输出统一 `image/png`，透明保留，EXIF 方向归一化，转换为 sRGB 显示代理并剥离源 EXIF/GPS/ICC 文本。不是原文件流，也不是 CSS 缩小原图。
- 源上限 32 MiB、解码像素上限 40,000,000、每边上限 16,384；派生 PNG 上限 thumbnail 512 KiB、preview 12 MiB。安全上限属于服务端实现合同，客户端只按明确错误显示降级。
- 200 为完整有界派生字节，已知 Content-Length；`Cache-Control: private, no-store`、`X-Content-Type-Options: nosniff`、`Cross-Origin-Resource-Policy: same-origin`。首版不返回 ETag/304/Range/重定向/源 hash，避免跨身份缓存语义。不得把资源挂到公开静态目录。
- 错误为既有认证风格 `{code,message}`：400 `invalid_request`，401 沿用，403 信任边界沿用，404 `entry_not_found`（缺失/不可见一致），409 `source_changed`，415 `preview_unsupported`，422 `preview_invalid`/`preview_limit_exceeded`，429 `preview_busy` + Retry-After:1，503 `preview_unavailable`，504 `preview_timeout`。失败绝不返回原图或旧缓存。
- 按需同步请求，无 202/no polling，不挂 durable queue，不全库预生成。客户端只请求当前可见图片和当前详情；离开/身份变化取消，结果代际校验，退出/撤权回收 object URL/bitmap。Web CSP 需准许 `img-src blob:`，由协调集成；跨账号不共享客户端缓存。

## 2. 复用与模块职责

已读真实调用链：`TrialAuthenticationMiddleware` → `ReadOnlyBrowseService.GetEntryAsync` → `IAuthorizedReadModelQuery` 的 PostgreSQL 权限/稳定 ID 查询 → `AuthorizedEntryDetail`；`ILibraryScanTargetQuery.FindAsync` → `LibraryScanTarget` 的受控根与 availability。

PreviewProvider 不引用 GatewayAuth.Application/Infrastructure。建议在 GatewayAuth.Application 增加唯一授权预览编排，复用 GetEntryAsync，再通过 PreviewProvider.Contracts 公开端口请求派生。PreviewProvider 只引用 GatewayAuth.Contracts 的授权事实、LibraryStorage.Contracts 的根查询和 AssetIdentity.Contracts。返回字节之前编排再次 GetEntryAsync，校验身份/库/路径/长度/mtime 与首查一致；Host 同时再次验证活跃会话。缓存查找不能跳过这些步骤。没有跨模块 SQL，没有新表/迁移。

首版全局最多 2 个进行中的源读取/解码任务，立即拒绝溢出，不把等待信号量者积成队列。按真实内容 SHA-256 + decoder 精确版本 + 规格版本缓存；可先用仅内存、256 项/64 MiB 双界限 LRU，重启自然清空。每次请求仍需要稳定读取/核对当前源，命中只节省解码。复杂度 O(受限文件大小 + 路径深度)，缓存常数上限，不扫描 50 万资产。

并发名额必须持续到有界响应发送结束或断开，不能在解码后提前释放、令慢客户端累积无上限的12MiB响应缓冲。拒权先于读取；源检查/授权最终校验的线性化点为发送首字节之前，不声称已发送字节可以撤回。每条路径最多128层，避免不受控句柄数量；Windows逐组件拒绝ADS冒号、设备名/设备路径以及尾随空格/点的别名，不仅依赖现有RelativeAssetPath的遍历检查。

## 3. 安全打开与稳定副本

不能把现有 `SystemReadOnlyFileDiscovery` 的属性预检当成有竞争条件安全的内容打开：它只枚举元数据。新增受控内容源实现，不复制或改动扫描状态机。

- Windows：从卷根/UNC share 到库根再到相对文件的每层目录，逐层 `CreateFileW(FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_BACKUP_SEMANTICS)`，检查打开句柄的 reparse/目录属性；祖先句柄持续持有并拒绝 FILE_SHARE_DELETE/WRITE，从而锁定已验路径。最终文件同样 OPEN_REPARSE_POINT，仅 GENERIC_READ，FILE_SHARE_READ，拒绝目录、设备和 reparse。用句柄取得 volume/file ID、长度、mtime，不能只先检查字符串后普通 File.Open。
- Linux x86-64：从 `/` 目录句柄逐组件 `openat(O_NOFOLLOW|O_DIRECTORY|O_CLOEXEC)`，最终 `O_RDONLY|O_NOFOLLOW|O_NONBLOCK|O_CLOEXEC`，`fstat` 验证 regular file，拒绝 FIFO/device/socket。保持目录和源句柄，以 dev/inode/size/mtime/ctime 比较，受控相对路径绝不交解码器。
- 内容源在固定 64 KiB 缓冲区内复制到本请求私有、有界稳定副本，同时 SHA-256；读取前后句柄状态必须一致且与索引长度/mtime 匹配。Linux 再读 hash 并重新打开路径确认身份，解码结束前再次校验；任何源变化/链接替换/离线均丢弃结果。复制超 32 MiB 立即终止。Windows 的只读共享阻止并发改写/删除；不主动写原文件，不改 hash/mtime。
- 解码器只获得副本字节及固定 variant，不获得库根/源路径、会话、Host配置或数据库参数。副本/缓存只在自有私密临时目录或有界内存；取消、崩溃、空间不足清理副本且保留源。源阻塞 I/O 必须在可回收的内容读取进程内，不能声称 CancellationToken 足以取消不响应的 NAS 系统调用。

## 4. 解码依赖建议

建议精确锁定 **SkiaSharp 4.151.2** + **SkiaSharp.NativeAssets.Linux.NoDependencies 4.151.2**（Windows native 包为其相应传递依赖）。2026-09-08 官方 NuGet 和官方支持表均列 4.151.2 为 stable；3.119 线已标 out of support，不为熟悉旧 API 锁旧版。[NuGet](https://www.nuget.org/packages/SkiaSharp/4.151.2)、[Linux native](https://www.nuget.org/packages/SkiaSharp.NativeAssets.Linux.NoDependencies/4.151.2)、[官方支持表](https://mono.github.io/SkiaSharp/docs/releases/index.html)。

SkiaSharp 是 MIT；需随发行保留 Skia 与捆绑 libjpeg/libpng/libwebp 等第三方 notices，不能只凭顶层 MIT 判断所有 native 资产。NuGet managed 包约 8.9MB，实际 win-x64/linux-x64 发布增量必须 publish 后分别量测；不拿总 NuGet 下载体积当发行体积。[许可证](https://github.com/mono/SkiaSharp/blob/main/LICENSE.txt)。Native 解析仍是不可信数据攻击面，进程外 + 像素上限 + OS资源限制必需；restore NuGet audit 和最终具体 native inventory/advisory 审计缺一项则记缺证据，不能从 advisory 页面为空宣称无漏洞。[官方安全入口](https://github.com/mono/SkiaSharp/security/advisories)。

比较：ImageSharp 最新官方 NuGet 为 4.1.1，纯托管部署较简洁，但 4.x 直接依赖新增构建时许可证要求，且 split license 随使用主体有差异，不建议为这次任务引入授权/秘钥管理。[包](https://www.nuget.org/packages/sixlabors.imagesharp/)、[官方安装说明](https://docs.sixlabors.com/articles/imagesharp/index.html?tabs=tabid-1)。NetVips 3.2.0 + libvips 具有按需处理优势，但 native 及格式依赖面更大、libvips 为 LGPL-2.1-or-later，当前三格式 32MiB 有界目标不足以抵消分发维护成本。[NetVips](https://www.nuget.org/packages/NetVips)、[libvips](https://www.libvips.org/)。不使用 Windows-only System.Drawing 或在线服务。

退出策略：解码仅藏在固定输入/输出内部 Worker 合同之后；替换 decoder 增加版本 namespace，清空可重建缓存，复跑方向/透明/损坏/边界 golden 与源不变测试。没有原文件迁移或数据库迁移。根 PackageVersion、lock、依赖许可政策由协调单一所有者批准写入。

## 5. 隔离裁决（不能省略）

已验证源码事实：`ReadOnlyWorkerProcess` 仅清理环境、限制诊断/协议、超时 kill tree；`WindowsWorkerJob` 仅 KILL_ON_JOB_CLOSE。现有模式没有 CPU/内存硬限制，也没有文件/网络沙箱。复用其进程生命周期思路不能等同于已有安全解码运行时。

建议解码使用**独立 .NET 可执行程序集**，不引用 Host/Core/Npgsql，不加载配置/密钥/连接；核心传递有界字节。Windows 在传输入前加入 Job，内存上限 512 MiB、进程数1、CPU时间3秒、墙钟3秒、父退出kill；Linux 在解析前 setrlimit CPU/输出/内存（须按.NET实际虚拟地址预留验证，不能错误地将低RLIMIT_AS当RSS），配合 cgroup memory.max / pids.max 的真实边界。请求总期限须小于现有认证 middleware 的5秒，并为二次授权/响应留出预算。

完整文件/网络隔离需额外真实证据：Windows 无网络 capability 的 AppContainer/受限目录授予；Linux 单独 mount/network namespace（如受支持的 bubblewrap/systemd/容器 profile），只读映射精确运行时与程序、仅当前副本，**不映射资产根、Host状态和数据库网络**。同uid子进程、清空环境、RestrictedToken/Job本身都不足以保证读不到Host密钥文件或发不出网络请求。

内置受信 Worker 意味着精确固定代码、固定三种格式与无插件装载/manifest/side-loading；不意味输入可信，也不意味着绕过隔离。第三方 Provider 的 M0-007-G1/G2/G3 不改、不关闭。若本轮环境不能证明上述硬隔离，则端点/流水线可实现并在合成fixture验证，但生产解码必须 fail-closed 返回503，交接明确 partial。需要协调裁决该启用条件及新独立 Worker 的目录/接线所有权。

## 6. 请求批准的跨界文件

建议协调拥有 contracts/assetlink 新合同、ADR、生成SDK/stamp、根 slnx/PackageVersion/lock/CI/依赖审计。V03-007请求补充允许：GatewayAuth/Application 的授权编排小文件；Host/Trial/TrialHostFactory.cs、TrialWebEndpoints.cs、Host/Hosting/CoreServerHost.cs 的DI/路由/内部命令接线；独立解码工程建议 services/worker-supervisor/ImagePreview/**（或协调指定目录）；测试可通过项目引用/编译链接复用既有 Core/PG/HTTPS fixture，由协调确定 tests/WebGateway 共享fixture接线。不得在 PreviewProvider 内直接引用 GatewayAuth.Application。

## 7. 验证与当前进度

初始化 result.status 已从无效 in_progress 改为 schema 允许的 partial。2026-09-08 基线 verify_repository 成功（迁移21、架构14及现有静态合同）；发布仍 blocked。默认 PATH 未含 Python，使用桌面已安装 Python 绝对路径运行，未新增解释器。

落地需覆盖真实缩小尺寸、透明/方向/sRGB、同内容复用但鉴权隔离、损坏/超限/不支持、先拒权后不读源、缓存命中撤权、源变化/链接替换、取消/超时/崩溃/孤儿回收、内存/CPU/队列上限、离线/重启、原 hash/mtime 不变。复用既有真实 Core/PostgreSQL/HTTPS fixture，素材只在自有临时sandbox；未运行NAS部署或真实资产扫描。
