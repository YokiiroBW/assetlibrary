# V03-007 当前交接

状态 partial。安全源基础已完成Windows窄回归，root另取得Linux9/9证据；新增独立Skia解码、BCL私有帧协议、Linux NativeAOT/seccomp和Windows LPAC/Job启动代码作为可编译技术验证点，尚未接入HTTP或启用生产。Windows真实启动仍在ready前失败，完整隔离和跨端联调未通过。接口/依赖已按协调38aedca批准，生产 Provider 门禁保持原状。

工作区 `C:/Users/Administrator/.codex/worktrees/021c/AssetLibrary`，分支 `codex/v03-007-image-preview-server`，起点 `70ce45c743ba6d026e695c118d1927d291b37918`。复用现有路径值对象、安全Worker错误、MSTest、RepositorySandbox和目录junction fixture。读取实现逐层固定no-follow句柄，双读SHA256并核对源身份/mtime，Windows拒绝共享写/删、ADS/设备别名；Linux openat/statx拒绝特殊文件，代码已编译但尚无Linux运行证据。没有共享表、数据库、真实资产或部署修改。

下一步实现已获准独立decoder、AppContainer/seccomp与核心授权/HTTP接线；没有把只清空环境变量的子进程等同于文件/网络沙箱。8个读取边界用例通过，1个文件symlink因Windows创建权限不足缺证据；目录junction真实通过。当前库权限、原文件安全、时间/内存/兼容最终验收尚未完成。

当前技术验证点：NativeAOT win-x64编译成功；exe1,466,880 bytes、Skia DLL12,274,488 bytes（随后源码有变化，最终包须重新量测）。非隔离启动只处理内置可信1px warmup，返回24字节Unavailable并退出1，不接受图片输入。LPAC启动初始错误203在补LOCALAPPDATA/WINDIR后消失；目前CreateProcess成功但进程ready前退出0xFFFFFFFF、stderr空。没有撤销LPAC、零cap、JOB_LIST/512MiB/CPU3秒/active1限制。仍须完善早期取消、原始错误保留和profile失败/崩溃恢复。

新增GatewayAuth授权编排及获准PreviewProvider公开port，source DTO仅依赖LibraryStorage/AssetIdentity公共事实，避免模块循环；Core仅编译链接BCL协议，不引用Skia worker程序集。图片CLI目前无测试旁路且仅无参数读取二进制帧；Linux同代码fault probe入口和Host内容broker/缓存/HTTP接线仍待完成。普通/AOT锁正按既有AssetLibraryReleaseLockRoot分离，发布notices验证已写，候选包重新发布与审计待完成。

提案提交 `37ef0df74d8bca568a0d962627932240761588d1`。验证见 [tests.md](tests.md)。提案不引入运行时性能或兼容变化；代码/根依赖/迁移尚未修改。建议先合入协调合同/ADR/依赖准入，后合入服务端与客户端实现。
