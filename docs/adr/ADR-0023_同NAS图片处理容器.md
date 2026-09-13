# ADR-0023 — 同 NAS 图片处理容器

- 状态：Accepted for implementation；生产启用须完成下述目标平台证据。
- 日期：2026-09-13；协调任务 V03-026。
- 用户约束：图片功能必须在同一环境使用；解释为同一 NAS、同一部署包，不增加另一计算主机。
- 修改 ADR-0019 的 Linux 部署选项；Windows 与原 seccomp 隔离路径不变。外部 image-preview-v1 帧与错误语义不变。

## 依据

现有 NAS 5.10.55+ 未提供 seccomp，原进程内过滤器不能启动。真实 Emby/Jellyfin 可以运行不证明具有本项目相同隔离。本轮只读确认 Docker 可使用、AppArmor 可用；不复制 Emby 的 privileged 配置。非特权 user namespace 调用 EINVAL，不能依赖嵌套 user namespace。

有效 io_uring_setup(entries=1,120B参数)在该NAS成功；同一非特权测试进程将 RLIMIT_MEMLOCK 软/硬限均置0后返回 ENOMEM12。此行为对应上游5.10.55在建立ring之前的锁页额度检查，不是以无效参数伪装隔离。它只修改短命测试进程，未改宿主策略。

## 决策

同一 Compose 新增一个图片监督容器，以本地 pathname Unix socket 连接 Core，不发布网络端口。Core继续拥有权限、稳定文件读取、缓存和源复验；仅发送已授权的有界图片字节。监督器和decoder不获得原路径、资产挂载、Core状态、用户会话、数据库或Docker socket。

- 容器：独立PID/net/mount/IPC，network_mode:none，只读镜像，NNP，memory512MiB，cpu_shares256。不使用privileged/unconfined、不修改宿主内核或其他服务，不假设CFS/PID cgroup限额可用。
- 可信监督器：不加载Skia，作为PID1运行。明确保留CHOWN/SETUID/SETGID/KILL四项能力、ambient0和NNP；CHOWN仅由固定代码管理唯一IPC卷的socket归属。Linux capset是线程级，不能以单次调用宣称CLR全进程已撤销CHOWN；不引入复杂重执行/FD传递来制造这种声明。解码子进程仍全线程降UID并清能力。每次创建短命decoder，三根stdio管道，环境清空到明确运行时白名单。只有一个decoder在途，无服务端排队；额外请求立即不可用。
- Core的socket适配器：保留现有IImageDecoder签名与两项应用层许可，适配器内部一个可取消串行许可，使同一个Core的第二项请求在既有总预算内等待；不新增无界队列。
- decoder：显式固定 `--container-decoder` 入口，受信启动阶段先清补充组并降为UID/GID1655、cap0；预热之后设置AS512MiB、CPU3秒、FSIZE0、CORE0、NPROC软/硬1、MEMLOCK软/硬0和NNP。在读攻击输入前验证全部约束及有效io_uring创建确实被拒；成功创建或不明确结果则不可用。不继承ring、listener、凭据或源fd，不传SCM_RIGHTS。
- 图片Worker的GC提交硬限仍为64MiB，地址预留明确为128MiB；默认5倍预留原先占用320MiB，挤占AS512MiB内原生像素空间。实际NAS预热VM由424164KiB降至227360KiB，40MP及32MiB像素数据的两种输出均已成功；这不是增加内存限额。
- PNG每个非IDAT块最多4MiB，在进入Skia前检查，超出返回正常Limit6/HTTP422而非基础设施Unavailable。Skia/libpng渐进读取巨大附加块会反复分配并拷贝累积数据；不删改ICC/EXIF、颜色或方向语义，也不专门识别测试标签。IDAT仍受总源32MiB限制，不套4MiB附加块上限。巨大元数据的拒绝与正常32MiB像素数据的成功分别留证；CPU3秒与既有输出上限不增加。
- 该容器档位不声称禁止每个syscall；文件/网络/跨进程隔离来自独立namespace、无敏感挂载、不同UID/组、cap0与目录权限。旧普通worker仍必须通过seccomp，禁止通用“跳过隔离”开关。
- IPC卷：同部署专用，只有Core与监督器挂载；Core只读。目录root:1654且仅Core组可穿过，socket由Core UID可连接，decoder UID不可达。核验SO_PEERCRED，生产路径固定 `/run/assetlibrary-image/decoder.sock`。路径、环境、任意URL不可由客户端指定。
- 监督器8秒总墙钟预算含启动/收发/解码，断开/超时即取消并终止、wait/reap；实际退出未确认不释放名额。PID1退出由内核清其PID namespace。CPU3秒只描述decoder所有线程累计CPU，不声称瞬时百分比或内核全局工作CPU。
- 连续基础设施/清理异常须有持久有界熔断，最多3次自动恢复；清理失败可以退出PID1，但不能用无限Docker重启放大残留。正常应用重启不遗留图片输入或孤儿任务。持久运行状态仅计数/归属，不含图片或凭据。

## 复用及依赖

保留IImageDecoder、IImageSourceLease、ImageWorkerProtocol24B、ImageWorkerTransport PNG验证、Core授权与缓存。监督器新BCL-only .NET/NativeAOT程序集；沿用已批准C#语言/.NET主运行时，无新NuGet或第三方解码库。Compose增加第四发行镜像、一个IPC运行卷，仍是同项目一次部署；不新增数据库/消息系统/第二业务服务。Linux构建可以使用开发主机，交付运行不依赖该主机。

## 准入证据

必须在自有容器/合成夹具证明：只读/无资产与凭据挂载、socket访问正反控、不同UID的文件/信号/ptrace拒绝、private namespace和外部网络不可达；MEMLOCK0前后有效ring正反控且无继承ring；NPROC1之后完整三格式、40MP/32MiB像素数据边界解码成功、非IDAT4MiB边界与超限拒绝符合预期且fork/thread创建失败；真实memcg/AS/CPU限制；正常/错误/取消/杀decoder/杀监督器/重启的旧容器实际回收与熔断，不能以新cgroup为零冒充旧资源释放。旧seccomp与Windows回归保持通过。

完成后以最终同源NAS包部署，保留原部署ID/四卷/资产只读与强哈希，备份/回滚和实际Core→图片→客户端闭环留证。外部请求契约仍512/2MiB、1600/12MiB、源32MiB/40MP，15秒Core预算；目录规模50万不增加单请求复杂度。

PNG耗时依据已追溯至锁定的SkiaSharp4.151.2 → Skia098aeb5 → libpng3061454（1.6.58）。[Skia按4096字节提交渐进输入](https://github.com/mono/skia/blob/098aeb5e4aa2e7f74e120e78200bbe0412ed57c5/src/codec/SkPngCodec.cpp#L138)，[libpng对未完整的非IDAT块扩容并复制旧缓冲](https://github.com/pnggroup/libpng/blob/3061454d980de7d53608f594194cfac722721d2a/pngpread.c#L339)。约32MiB单块的累计复制量估算约128GiB，符合NAS实际2.97秒CPU后退出且无OOM的现象；未声称采样剖析已测出每个函数占比。其默认8000000字节chunk-handler限制发生在该缓冲之后，不能替代4MiB前置准入。4MiB会明确拒绝单块超限但格式合法的ICC/EXIF等数据；没有修改原件或静默丢弃元数据。

参考：[Linux5.10.55 io_uring](https://raw.githubusercontent.com/gregkh/linux/v5.10.55/fs/io_uring.c)、[PID namespace退出](https://man7.org/linux/man-pages/man7/pid_namespaces.7.html)、[Unix socket权限](https://man7.org/linux/man-pages/man7/unix.7.html)、[Docker none网络](https://docs.docker.com/engine/network/drivers/none/)。G4用户豁免继续有效，不把同NAS验证当完整V0.3发布。
