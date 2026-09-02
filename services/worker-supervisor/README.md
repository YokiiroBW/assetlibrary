# Worker Supervisor

隔离Provider与重型任务，负责进程生命周期、资源限制、超时、健康和回滚。

V01-001 仅激活 `AssetLibrary.WorkerSupervisor.csproj` 的可编译程序集标记与 .NET 10 framework reference；没有进程启动、Provider 加载、文件/网络访问或业务实现。Windows/Linux 硬隔离和 RPC cancel 门禁仍保持关闭。
