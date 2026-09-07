# Core Server

模块化业务内核。服务Windows/Linux/Docker发行。包含认证、资源库、资产、元数据、操作、搜索、任务等边界。

`AssetLibrary.CoreServer.csproj` 保持为无入口点的模块化业务内核。V01-008 在 `Host/` 增加唯一跨平台进程宿主，供 Windows x64、Linux x86-64 和 Docker 共同使用；宿主只暴露 `/healthz`、范围明确的 `/readyz`、`--health-probe` 与 `--build-info`。V01-010 增加显式启用的 PostgreSQL 只读连接与迁移就绪检查；认证后的业务 API、业务数据库组合及生产文件写入口仍未开放。

启动宿主必须显式指定 `Production` 环境，并提供已存在、绝对、非根且可写的 state 目录。默认仅监听 `127.0.0.1:5080`；容器发行定义才显式改为 `0.0.0.0:8080`。`--build-info` 的 `source_revision` 只有经发行构建脚本固定后才是可引用的提交；普通开发构建显示 `unissued`。

模块代码由独立 owner 按 `Modules/<Module>/{Domain,Application,Infrastructure,Contracts}` 添加。平台安装、服务管理和发行脚本只允许位于 `infra/**`，不得进入 Domain/Application。
