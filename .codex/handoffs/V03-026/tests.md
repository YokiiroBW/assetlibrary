# V03-026 验证进展（尚未统一验收）

Windows 使用锁定 .NET10.0.111；Linux 仅在 dev-230 编译，运行时测试均在原 NAS agent-210、5.10.55+、Docker24.0.2，专用合成目录和标签容器，不操作原件或其他应用。

- root 29c9c12：solution locked restore/build 通过；受影响 Preview 测试77通过、1项Linux SO_PEERCRED在Windows跳过。日志 .runtime/affected-tests.log 和 .runtime/tests/nas-adapter-affected.trx。最终源验证待后续修正稳定。
- 首轮 NAS NativeAOT：10份合成文件×512/1600两种profile共20用例符合预期，三种格式、方向6、透明、中文改名重复文件及4类拒绝路径。.runtime/nas-first-cases.log。
- 第二轮1809630：所有decoder线程身份、父内存读取拒绝、ptrace/信号拒绝、IPC状态不可读、有效io_uring由可创建变为ENOMEM12、NPROC线程/派生进程拒绝，实际memcg512MiB。.runtime/second-native-isolation.log。
- 第二轮故障：Ready前断开、Ready后断开、部分输入断开均清理且不计基础设施失败；上传时kill child计1、SIGSTOP触发自身8秒超时计2、kill PID1保留计3且重启后拒绝创建下一decoder。.runtime/nas-fault-cases-second.log。需补旧宿主PID消失核验；不以新命名空间空进程替代旧进程证据。
- 第三轮a87b44c：不可变像素共享优化已合，普通图片仍通过；40MP PNG仍Limit6，1×1且31/32MiB PNG仍Unavailable7，监督器固定诊断为result/exit137。.runtime/nas-third-boundaries.log、.runtime/nas-third-diagnostics.log 为观察记录，不能将其脚本末尾历史 native_cases_passed 字样当边界成功。

前三轮均未增加AS512MiB、memcg512MiB、CPU3秒或managed heap64MiB。第三轮失败仍在诊断，不替换生产镜像。先前所有root临时容器已清，专用IPC卷/合成输入保留供最终复验；不声称全部运行资源已清。

追加验证：a87b44c完整solution的locked restore/format/Release build通过，428个用例通过、52项平台/夹具缺失跳过，无失败；原始TRX在.runtime/tests/solution-a87。Linux独立副本同一源131个唯一Preview用例93通过、38未执行，无失败；包括真实SO_PEERCRED UID0和补跑的真实source broker，证据已合入V03-028/linux-tests-a87b44c。

第三轮父退出补测在NAS记录旧宿主decoder PID14683/start_tick137809832后kill PID1，1.414秒内旧宿主PID消失，预扣故障1保留；只清本测试资源。.runtime/nas-parent-host-proof.log。升级前真实HTTPS登录/注销、两库及原scan195792观察/提交计数已记录，不含原路径或凭据；.runtime/nas-live-baseline.json。

打包新增16项契约用例在集成目录通过（19.1秒），旧server packaging6项在-I下通过。根CI原非-I的repository discovery会触发既有release工具的隔离导入保护；改为python -I -B后与ci-tiers保持一致，未删保护。全-I仓库95项结果由V03-029交接提供。

V03-027第三轮资源采样：40MP输入0.47秒Limit且memcg不足20MiB、无OOM；31MiB输入实际CPU累计2.97秒、wall3.57秒、exit137、memcg峰98.7MB和oom_kill0。2ac4bdc候选将GC地址预留由默认5倍改为明确128MiB，Heap64MiB及内核上限不变；第四轮AOT和目标NAS对照进行中。

完整 package、同NAS Core真实授权API、备份升级/回滚、最终artifact哈希和清理尚未完成。各子任务的静态/纯测试记录分别见V03-027/028/029，不与目标NAS平台证据混算。
