# Core Server

模块化业务内核。服务Windows/Linux/Docker发行。包含认证、资源库、资产、元数据、操作、搜索、任务等边界。

`AssetLibrary.CoreServer.csproj` 保持为无入口点的模块化业务内核，`Host/` 是Windows x64、Linux x86-64和Docker共用的唯一进程宿主。默认诊断模式保留 `/healthz`、范围明确的 `/readyz`、`--health-probe` 与 `--build-info`，可显式启用PostgreSQL迁移就绪检查。

V01-015提供显式 `--read-only-trial <private-configuration>`：同源HTTPS Web与认证、资源库登记、首次扫描及浏览/搜索API，PostgreSQL和最小权限迁移就绪是必需条件。使用既有 `--trial-operator` 管理本机授权初始化/恢复，`--trial-health-probe`检查试用就绪。配置和密钥持久化，文件探测/扫描由同一二进制的只读子进程执行。生产文件写入口仍关闭；NAS容器交付见ADR0015和V01-021任务记录。

默认诊断宿主必须显式指定 `Production` 环境和state目录，监听 `127.0.0.1:5080`；旧诊断容器使用8080。试用由私密配置指定HTTPS Origin和监听地址，端口取Origin.Port，容器映射应保持一致。试用健康探针保留公开Host/SNI和精确证书核验，仅把TCP连接路由到当前本机监听，避免依赖NAS公开地址回绕。`--build-info` 的 `source_revision` 只有经发行构建固定后才可作为来源；普通开发构建显示 `unissued`。

模块代码由独立 owner 按 `Modules/<Module>/{Domain,Application,Infrastructure,Contracts}` 添加。平台安装、服务管理和发行脚本只允许位于 `infra/**`，不得进入 Domain/Application。
