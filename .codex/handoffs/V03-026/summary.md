# V03-026 同 NAS 图片处理集成

状态：partial，正在开发。用户要求运行时全部留在同一 NAS、同一部署包；外部主机仅用于编译。当前正式 NAS Core/PG 未修改，仍为 61e6c0e。本文件记录进展，不是上线验收。

采用 ADR-0023：同 Compose 的 image 服务、BCL-only NativeAOT PID1 监督器、每次请求独立 UID/GID1655 decoder、固定 Unix socket。Core 保留原权限、源读取、一致性与 PNG 检查，仅替换图片处理端口适配。没有数据库迁移、第三方依赖、其他语言、资产写操作或外部计算服务。

V03-027 实现隔离与监督器，V03-028 实现 Core 适配，V03-029 实现 NAS 打包与生命周期；root 集成共享契约、项目/锁、Dispose 所有权并负责实机。普通合成图片已经在实际 NAS 通过，连续故障停止重试、取消/崩溃/超时、父权限隔离已有实测。有效大图与近32MiB输入边界仍在修复，不能记通过。

监督器保持 CHOWN/SETUID/SETGID/KILL 四项 capability；不同 UID decoder 的所有线程须 cap0/NNP，并受 AS512MiB、CPU3秒、NPROC1、MEMLOCK0 等固定边界。image 无网络、资产、数据库、Core 私密状态或 Docker socket 挂载。命名 IPC 卷保存私密故障计数，最多三次连续基础设施失败，不用重新建卷清零来绕过正式故障。

剩余：边界复测、Linux 适配完整测试、正式四镜像包及临时 Core 端到端验证、冷备后升级现有 NAS、最终清理和交接。G4 按用户要求豁免未执行；本任务不宣布完整 V0.3 完成。

进程、构建与继续入口见 [progress.json](progress.json)，当前已执行验证见 [tests.md](tests.md)。
