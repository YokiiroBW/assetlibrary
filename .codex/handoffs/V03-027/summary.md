# V03-027 交接摘要

状态：partial。最新实现 `8cd23ea39c1d170fe579dcd28f1e81b82bd39855`，分支 `codex/v03-027-nas-image-supervisor`，基线 `9c76f66`。实现严格限制在本任务独立worktree；没有连接NAS/VM、改根级依赖锁/共享协议、操作真实资产或其他服务。

## 完成内容与接线

- 新BCL-only `services/worker-supervisor/ImageSupervisor/AssetLibrary.ImageSupervisor.csproj`，可执行名 `AssetLibrary.ImageSupervisor`；不引用Core或Skia程序集。
- 生产PID1无参数入口，固定子程序 `/app/workers/image-preview/AssetLibrary.ImagePreview.Worker --container-decoder`。新增四个固定 `--probe-container-isolation/memory/threads/cpu` 通过同一PID1启动器运行；`--health`只读且不连接socket、不启动decoder。
- 复用ImageWorkerProtocol24B、Core已有BCL PNG validator、既有StaticImageDecoder。普通Linux无参数路径仍需原seccomp，Windows路径保持LPAC检查；没有通用跳过隔离开关。
- 固定IPC目录 `/run/assetlibrary-image` root:1654/0710，socket1654/0600；核Core peer1654，服务peer0。Root最终批准监督器稳态CHOWN/SETUID/SETGID/KILL四cap，避免把线程级capset误当全进程清除；decoder经glibc setresgid/setresuid永久1655、补充组空、cap0。
- 预热前拒绝异常SecureBits，预热后逐线程核Uid/Gid含fsuid、CapEff/Prm/Inh0及NNP1（最多256线程、每份status8KiB）。AS512MiB、CPU3秒、FSIZE0、CORE0、NPROC1、MEMLOCK0均软硬限；实际内存cgroup必须不高于512MiB，有效ring创建必须ENOMEM12，无继承socket/ring。
- 单decoder、至多两个有界exchange（前一份输出及下一decoder），没有服务端任务队列；额外连接status7。Source最多32MiB按16KiB缓冲转送，派生PNG最多12MiB有界收集、验证、确认子进程成功退出/reap后才发送完整成功帧。

## 取消、故障与持久恢复

8秒为服务端工作总预算，正常取消与超时后的实际清理仍保持占位。pidfd指向原子进程身份；迟到启动先有界拿回并kill/reap，无法确认时退出PID1清该private namespace。Ready前Peek只观察断开而不消费请求字节，完整输入后另一个1字节read观察EOF/额外输入。

持久状态仅8字节计数：root0600 `supervisor-state.bin`，另有root0600 lifetime flock `supervisor-lock.bin` 与原子替换临时 `supervisor-state.next`。每次spawn前先写入、fsync并原子替换；成功终态且实际清理后才清计数。PID1骤死保留预记账，3次后不再spawn；正常重启不删除预算。永久配置/状态错误保持PID1 unhealthy而不接单，不形成无界重启创建worker。

22ac562明确区分取消原因：已观察client EOF/额外字节/读取断连，或独立operator stopping令牌，可在确认清理后清计数；自身8秒deadline而客户端仍在线则保留失败计数。监控read都join后才判断，不能将任意OCE当正常取消。

## 验证与实测范围

本地最终34个纯逻辑用例全部通过；此前相邻预览测试54通过、3个真实夹具/平台条件缺失跳过，见tests.md；两个实际项目严格Release build/format通过，源码策略552文件与仓库架构gate通过。测试入口临时加入root批准的4条Compile Link，已按原始字节恢复，未提交共享测试csproj。

Root首次同码NAS AOT（7255b45及root集成）已实测：隔离/线程/native分配3探针passed，CPU退出137且OOMfalse；10合成文件×2profile共20项通过，覆盖JPEG/PNG/WebP、方向/alpha、中文路径相同内容和4类坏图；Core1654连接见peer0，全部自有容器已清理。证据为root V03-026 `.runtime/first-native-probe.log`、`.runtime/nas-first-cases.log`，镜像sha256:604db1ba80499a00d21dc248c002b9ed775879dcec16efbf996085fd226763af。本任务只读核验了这些记录，未把Windows编译当Linux通过。

Root已补齐22ac562故障与父访问实测：父mem拒读、ptrace EPERM；早取消/Ready后/partial取消均回0且UID1655进程清零；上传期kill child保留计数1，保持连接并SIGSTOP child约8.29秒达到自身deadline保留计数2；kill PID1后重启计数3、Ready7、不再创建decoder。对应second-native-isolation.log与nas-fault-cases-second.log均已只读核验，自有资源清理标记齐全。

最大输入仍未通过：合法40MP小编码及40MP+exact32MiB PNG返回Limit6，1×1填充31/32MiB返回Unavailable7。不能据此声称完整最大边界已满足。Root正在使用实际stage+exit继续定位，没有调整资源限额。

## 架构与安全影响

Core权限、稳定源读取/复验、缓存仍由现有IImageDecoder/IImageSourceLease编排；监督器只有固定字节协议与进程生命周期，未复制业务。没有新包、语言、框架或数据库；新增项目沿用.NET/NativeAOT，root拥有solution/locks/发布入口。Root还负责父marker排除ImageSupervisor源码glob与真正测试项目接线。

50万资产下不会扫描全库，单任务只收已有授权源的有界快照。除私有计数/归属文件外不持久化媒体；没有资产/Host/DB/Docker socket挂载，图片留在同一NAS运行。3秒CPU只指decoder全线程累计CPU，不宣称瞬时占比或所有内核工作；没有CFS/PID cgroup可用性假设。G4豁免未安排，完整V0.3及生产NAS启用由root单独验收。

## 合并顺序

`2ea1190` → `7255b45` → `a2b7d8a` → `22ac562` → `0063769` → `8cd23ea` → 本交接。Root先整合ADR0023最终四cap修订与根项目/锁/测试接线，再合入V03-028 Core适配器与V03-029同源包，最终执行同NAS闭环与回滚验证。已知平台待证项见上；没有为将来功能引入扩展接口或额外技术债。

## 最大边界后续修正与当前暂停点

0063769仅在解码完成后对source bitmap调用SetImmutable、canvas销毁后对target调用SetImmutable。官方允许共享immutable且shareable的pixels；没有后续像素写入，using生命周期覆盖SKImage。Windows真实Skia4.151.2验证mutable对照确有复制、immutable指针相同；18个三格式/双规格/三尺寸case与旧实现PNG逐字节一致。此结果不等于NAS512MiB最大边界通过。

8cd23ea只在已实际reap且基础设施失败时记录监督器固定stage ready/input/result与真实child.ExitCode。不转发子stderr、路径、文件名或正文，不在正常成功刷日志。Root将两提交一次AOT，待实际stage+exit决定下一步。按root指令暂停源码变更；没有扩大GC/AS/memcg/Job预算，没有引入SKData流入改造。
