# V03-007 当前交接

状态 partial。已交付内部安全源读取基础并完成Windows窄回归；尚未接入图片解码或HTTP。接口/依赖已按协调38aedca批准，完整平台计划见 [isolation-plan.md](isolation-plan.md)。生产 Provider 门禁保持原状。

工作区 `C:/Users/Administrator/.codex/worktrees/021c/AssetLibrary`，分支 `codex/v03-007-image-preview-server`，起点 `70ce45c743ba6d026e695c118d1927d291b37918`。复用现有路径值对象、安全Worker错误、MSTest、RepositorySandbox和目录junction fixture。读取实现逐层固定no-follow句柄，双读SHA256并核对源身份/mtime，Windows拒绝共享写/删、ADS/设备别名；Linux openat/statx拒绝特殊文件，代码已编译但尚无Linux运行证据。没有共享表、数据库、真实资产或部署修改。

下一步实现已获准独立decoder、AppContainer/seccomp与核心授权/HTTP接线；没有把只清空环境变量的子进程等同于文件/网络沙箱。8个读取边界用例通过，1个文件symlink因Windows创建权限不足缺证据；目录junction真实通过。当前库权限、原文件安全、时间/内存/兼容最终验收尚未完成。

提案提交 `37ef0df74d8bca568a0d962627932240761588d1`。验证见 [tests.md](tests.md)。提案不引入运行时性能或兼容变化；代码/根依赖/迁移尚未修改。建议先合入协调合同/ADR/依赖准入，后合入服务端与客户端实现。
