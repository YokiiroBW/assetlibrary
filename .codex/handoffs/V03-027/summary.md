# V03-027 交接摘要

状态：ready_for_review。实现提交 `972795a9f8dbec3ae887612b5e4c29b9995bfb7b`，分支 `codex/v03-027-nas-image-supervisor`，基线9c76f66。最终同源NAS镜像已经通过本任务的元数据边界、正常大输入和隔离回归；Core部署闭环由root单一所有者继续。

## 完成内容

- 新BCL-only ImageSupervisor，PID1无参数服务，固定调用 `/app/workers/image-preview/AssetLibrary.ImagePreview.Worker --container-decoder`；没有Core/Skia程序集依赖、没有任意路径/URL/命令入口。
- 固定socket及SO_PEERCRED：目录root:1654/0710、socket1654/0600，Core peer1654、服务peer0。Root最终批准监督器四cap CHOWN/SETUID/SETGID/KILL，decoder经正常SecureBits、清组、glibc setresgid/setresuid成为1655/cap0，逐线程核Uid/Gid含fsuid/caps/NNP。
- 普通Linux仍原seccomp，Windows仍LPAC。容器档位要求实际memcg≤512MiB、AS512MiB、CPU3秒、NPROC1、MEMLOCK0、FSIZE0/CORE0、有效ring创建ENOMEM；不继承socket/ring，不增加网络或资产/Host/DB/Docker socket挂载。
- 单decoder、最多两份有界exchange，额外请求status7且不排队；source32MiB流式转送，派生PNG≤12MiB验证完且child实际成功exit/reap后才发布完整Success和接纳下一任务。
- 8秒总期限，pidfd针对原child；真实回收前不归还名额。Ready前Peek观察早取消，完整输入后1字节read观察EOF/多余字节。仅已观测client取消或独立operator stop在回收后清计数，自身deadline保留预记账。
- root0600计数/归属文件位于唯一IPC卷：supervisor-state.bin、lifetime flock supervisor-lock.bin、原子替换supervisor-state.next。spawn前fsync预留attempt，3次异常持久熔断；永久配置/状态错误保持unhealthy不接单，清理不确定退出PID1终止private namespace。
- `--health`只读核state/socket监听/PID1身份与全部线程cap/UID/NNP及真实内存controller，不connect、不spawn。四个固定probe使用同一个PID1和child启动路径。

## 最大输入修复

首次NAS实测把两个问题区分开：40MP输入因默认GC地址预留挤占AS空间返回Limit；巨大单块PNG metadata累计CPU接近3秒后退出137，memcg未OOM。

0063769仅在源bitmap解码完成和target canvas销毁后各SetImmutable，避免不必要像素副本；Windows真实指针共享及18个before/after PNG逐字节等价通过。81a6d52把worker的GC RegionRange设为128MiB，Heap64MiB及全部OS/container硬限不变。实际pre VmSize从424164降到227360KiB，40MP小编码p0/p1均成功。

972795a在Skia前限制非IDAT单chunk payload≤4MiB，IDAT仍受总32MiB/40MP限制；不剥ICC/EXIF、不特判某私有tag。超限返回正常typed Limit6并在回收后清计数。8cd23ea仅为基础设施失败记录固定ready/input/result stage及reap后的真实退出码，不输出子stderr、路径或正文。

## 验证结论

本地38个纯逻辑用例通过，两个项目strict build/format通过，源码策略553文件通过，仓库架构gate通过。此前相邻预览54通过、3条件缺失跳过，未记作平台通过；测试csproj临时注入root授权BCL源链接后已按原字节恢复，未提交共享项目或锁。

Root在22ac562同码NAS已通过20图片case、resource probes、父mem/ptrace拒绝和6项取消/超时/熔断/PID1死亡case。本任务后获授权，仅用自建V03-027 label/独立临时卷和只读合成corpus做真实指标对照，未碰production或root acceptance项目。

最终正式镜像 `assetlibrary/nas-image:c775b7d2b221479510f958f7e302a676b024e778`，ID `sha256:76b3ecd8512a8d0e173166208e438e7c89124cdbadaca8a5d7b2fd6e47900c76`：10个最终case全部符合预期；4MiB-1/=单块成功，+1/31MiB/32MiB超大单块Limit6/no body，8个限内块组成exact32MiB正常1×1，代表性exact32MiB像素数据p0/p1正常512×73/1600×229，坏图邻接普通图正常。每个终态ledger0、child0、oom_kill0/failcnt0；最终isolation全部passed。V03-027所有容器和卷独立查验为空。

注意：40MP小编码与exact32MiB像素数据是两个控制，后者为7600×1092，不能写成40MP+32MiB同时出图；巨大单块31/32MiB按新契约明确拒绝，不能冒称成功。采样CPU峰值是观测值，不是精确总CPU；缺样本用null记录。详见tests.md和nas-validation.json。

## 架构、影响及交付边界

复用原ImageWorkerProtocol、ImageInputPolicy/StaticImageDecoder、BCL PNG validator；Core权限、稳定源、缓存和源复验仍留在Core，未复制业务。没有新语言/框架/重大依赖、DB迁移或共享契约文件改写，根solution/锁/测试入口/发布由root负责。50万资产下仍只处理当前授权源，source32MiB/40MP和输出512/1600固定，任务、缓冲及失败恢复有界，不持久保存图片。

候选代码与最终元数据守卫已经合入root正式同源包；本任务仅交付本模块及验证，不宣称production启用或完整V0.3完成。G4豁免未安排，其他客户端/签名门禁保持独立。建议root在Core同NAS闭环后沿既有备份/回滚流程部署；没有遗留本任务临时资源。
