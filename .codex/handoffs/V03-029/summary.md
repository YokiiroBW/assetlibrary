# V03-029 同NAS图片容器打包交接

状态：ready_for_review。分支codex/v03-029-nas-image-package，工作树C:/YOKI/Codex/AssetLibrary-worktrees/V03-029，基线9c76f66。提交顺序7cb403d（实现）→be992f7（临时容器完整ID清理）→9874114（CI隔离测试入口）。

## 完成内容

同一Compose新增可选image服务、第四发行镜像和唯一image-ipc卷，仍在同一NAS/部署包。Core只读挂IPC；image仅此卷RW，network none/private IPC/私有PID、只读root、NNP、512MiB/cpu_shares256、无端口/设备/敏感挂载/init wrapper。监督器PID1固定/app/image-supervisor/AssetLibrary.ImageSupervisor；worker固定/app/workers/image-preview/AssetLibrary.ImagePreview.Worker。最新root裁决为可信Supervisor启动/稳态都保留CHOWN/SETUID/SETGID/KILL，child仍1655/cap0，未再声称单线程capset撤掉全进程CHOWN。

Dockerfile复用既有SDK10.0.111与image-preview AOT构建层，新增BCL-only Supervisor AOT输出层；运行image不复制SDK、Python或PG工具。监督器.NET许可证/第三方声明、旧worker/Skia声明、SOURCE_REVISION和SHA256SUMS都进入镜像。构建器复用现有源码快照/边界/流式hash与Native ELF校验，记录Supervisor锁哈希与文件哈希；image中的decoder必须与Core保留的旧worker逐文件一致。images.tar包含Core/setup/PG/image四镜像，不执行decoder或将build标为平台通过。

## 部署与失败边界

ASSETLIBRARY_IMAGE_PREVIEW_SOCKET在deployment.env中可持久显式启用，只允许空或/run/assetlibrary-image/decoder.sock；命令环境显式值（含空）覆盖文件。重复键即使均为空也拒绝；与WORKER互斥；无键仍disabled，不改settings/deploymentID。image profile没有Core健康依赖，失败时基础Core仍运行，并停止本image/返回明确不可用，不用重建卷或删fuse伪造恢复。

initialize/start复用同deployment专用image-ipc卷，校验已有标签/local driver/无host或remote别名。用同一不可变image创建一个从不启动的临时容器进行空volume copy-up，目录root:1654/0710，随后只删除返回的完整64位容器ID；未挂其他卷，也不由setup接触IPC。没有删除socket/state/lock/atomic-next的循环。四旧卷用途、部署ID、密钥、证书、迁移和备份规则保留；stop顺序Core→image→PG，down保留所有卷。

nasctl重验小型公共控制文件hash，再验证实际imageID/源码与部署标签、PID1入口、能力、namespace/端口、环境/挂载与资源配置；启动前先检查配置，启动后检查健康。--health由Supervisor实现，读取实际PID1各线程4cap/UID/NNP、socket/state/fuse和实际memcg，不连接socket/启动decoder。完整隔离与旧资源回收仍需root目标NAS实证，不以Ready或Health代替全部平台证据。

## 复用、安全与依赖方向

这里只做发行/部署适配，不复制Core权限、IImageDecoder、流协议、解码器、限额或三次持久熔断业务。没有新增NuGet/语言/框架、SQL迁移、共享契约、根版本/锁或其他模块写入。Root-owned Supervisor项目/RID锁、Core socket适配器须先合入最终source，再执行正式builder；此子分支不含它们，不假称独立四镜像实际构建完成。50万资产不改变包装行为或单图上限，现有只读资产挂载不增加新消费者。

测试为本机Python/真实POSIX shell加严格假Docker，没有连接NAS。按root新增授权只读检查dev-230工具/镜像元数据，未pull/build/create/exec容器，详情和精确命令见build-host.md。开发机工具缓存不代表NAS运行依赖。

## 验证与剩余工作

新增16个NAS包装/配置/CLI负控已通过；旧release包验证10过、2个POSIX权限用例因Windows跳过；sh -n通过。非-I实际CI discovery通过load_tests在真正-I子解释器运行此suite，没有改根CI或伪造隔离flag。仓库快门禁通过，隔离模式完整repository95/95通过；原非隔离总入口的既有release工具导入错误由root统一调整CI命令，详见tests.md。

根任务负责最终LinuxAOT/四镜像构建、同NAS权限/资源/取消/熔断/真实Core出图、冷备、部署和回滚验收。本任务未改变任何线上容器，未读原件，G4豁免未执行，完整V0.3仍独立。
