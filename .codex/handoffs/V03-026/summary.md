# V03-026 同 NAS 图片交付

状态：ready_for_review，已经完成原NAS冷备升级和实测。最终运行包源码7f092904dbadcf1276bb9daf5b565946ecccbdad；V03-027/028/029已集成，主协调分支codex/v03-026-nas-local-image-integration。完整来源、测试、备份与清理见[same-nas-delivery](same-nas-delivery/README.md)。

原地址https://192.168.31.210:5443保持；Core、PostgreSQL、image同一NAS/Compose运行。原账号、部署ID、2份库及195792项完成扫描保留，Core仍1654和读取组101、资产只读。上线后原库两张真实图片的512/1600共4次请求成功，未保存其图片、名称或路径。首次软件冷备含四旧卷和原配置5个归档，逐项tar compare/hash，回滚材料保留。

复用IImageDecoder/IImageSourceLease、原授权与源复验/缓存、24B图片协议和PNG验证。新BCL-only NativeAOT监督器负责独立UID1655/cap0 decoder与生命周期；Core仅以固定Unix socket发送授权字节，图像服务无网络/资产/数据库/Core私密状态/Docker socket挂载。监督器稳态四项能力明确记录，未声称单线程capset撤掉全CLR能力。没有新语言、第三方依赖、数据库迁移或跨模块写表；外部图片协议不变。

GC提交64MiB不变、地址预留固定128MiB，避免挤占AS512MiB原生像素空间；PNG非IDAT块前置4MiB上限阻止libpng渐进累积拷贝，超限正常422，不导致熔断。总源32MiB/40MP、CPU3秒/内存512MiB等上限未提高。40MP与真正32MiB像素数据均出图。修正Compose create参数兼容问题，以及Web已加载缩略图按原始高度撑开网格后被裁空的问题；复用同一个图片尺寸规则，无新布局框架。

普通.NET全套428过/52平台或夹具跳过，追加4项新输入政策用例通过（38项受影响集合）。Linux Preview去重93过/38未执行；Windows旧LPAC1项、Linux旧seccomp6项真实出图通过。Web66项全过。最终NAS Core38图片请求、桌面/手机真实渲染、中断/恢复通过；20合成文件hash/mtime/大小不变；3测试容器、5卷、2网络、合成目录和原型IPC/镜像清理核实。平台缺项不当作通过，G4按用户要求豁免未执行。

文件处理复杂度仍O(有界源字节+像素数)，列表/查询索引不改；50万资产不新增全库遍历。只新增一个同机图片服务与IPC卷，源及输出有界、解码单并发，等待计入原8/15秒预算。停止后持久故障计数保留，实际reap前不发布完整成功。

风险/后续：首版静态JPEG/8位PNG/WebP，单个超大元数据块会明确拒绝；NAS新建/恢复管理员的官方风险服务直连仍不稳定，本轮临时TLS转发只用于合成管理员初始化并已关闭，现有登录与图片不依赖它。未执行整机重启或备份回退演练，不宣布完整V0.3/Android真机/签名/缩放与原件编辑/同步完成。没有本任务遗留实现TODO。

建议合并：此协调分支整体fast-forward到main；不重复cherry-pick子任务。后续文档提交不改变已部署7f09290二进制来源。交付说明见[docs/releases/NAS_IMAGE_PREVIEW.md](../../../docs/releases/NAS_IMAGE_PREVIEW.md)。
