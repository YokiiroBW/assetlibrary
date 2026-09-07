# Docker / Compose 发行候选

构建前必须把 `ASSETLIBRARY_SOURCE_REVISION` 设置为 40 位 issuance commit；Compose 使用必填展开，缺失时会在创建任何容器前失败。构建上下文通过 `Dockerfile.dockerignore` 排除 Git、任务运行时、环境文件和编译缓存。

V01-008 先验证干净发行输入并从选定 issuance commit 创建 Git 快照，再以该快照根作为构建上下文，从唯一的
`services/core-server/Host/AssetLibrary.CoreServer.Host.csproj` 构建宿主。最终镜像：

- 固定 .NET SDK `10.0.111` 与 ASP.NET Runtime `10.0.11`；
- 以 `1654:1654` 非 root 身份运行；
- root filesystem 只读，只有 `assetlibrary-state` volume 与 16 MiB `/tmp` 可写；
- 丢弃全部 capabilities，并设置 `no-new-privileges`；
- 默认只把容器 8080 映射到宿主 loopback；
- 使用宿主内建、有界的 `--health-probe`。

当前 Compose 只证明 CoreServer 宿主包装，不包含 PostgreSQL，也不开放业务 API 或生产文件写。
数据库、认证与业务入口由 V01-009 集成；不得把本 Compose 描述为可发布 Alpha。

静态验证不能关闭 Docker gate。只有在真实 daemon 上执行
`tests/release/run_docker_evidence.py` 的 build/up/health/restart/read-only/non-root/down-v
闭环并得到零残留证据后，协调器才可审查 M0-004-G2。

默认运行 evidence 工具只会报告 daemon/授权状态，不创建资源。隔离 runner 上的真实周期必须
显式运行 `python -I -B tests/release/run_docker_evidence.py --execute`；工具会选择临时 loopback 端口，
拒绝同名既有资源，并在结束时核对容器、网络、volume 和测试镜像数量全部为零。

NAS与第一版Web部署另见[nas/README.md](nas/README.md)。该入口沿用同一Host并组合PostgreSQL、同源Web和一次性部署工具；本页的V01-008诊断Compose及证据合同保持不变。NAS真实验收状态由V01-021协调，不从静态文件或构建成功推断。
