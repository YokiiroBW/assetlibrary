# V01-022 — 等待统一验收

执行状态：not_executed_per_user_instruction。实施阶段没有运行测试、语法/类型检查、镜像构建、Compose启动或远程部署；没有新增测试框架或大批测试。

## 根协调的真实入口

- 既有 `python -I -B scripts/verify_repository.py`、相关现有发行/集成回归及CI固定命令；不自造测试层级。
- 已干净提交后：`python3 -I -B scripts/build_nas_deployment.py --output-root .runtime/sandbox-storage/V01-022/build-01`。
- 跨机转移可先用同一命令加`--context-only`，校验source.bundle/context哈希，构建机checkout记录commit后构建。
- 交付目录：`./nasctl.sh load` → 编辑settings → `configure` → `initialize` → `operator bootstrap ...` → `start/status`。
- 统一生命周期：真实NAS浏览器登录/登记/首次扫描/只读浏览搜索，按旧回归验证权限/异常；`stop/start`与容器重建后持久性；`down`保留卷；以docker inspect验证两长期服务、cap/非root/只读root、只读资产bind、无DB宿主端口。
- 关键边界：正常源/不存在源/host根别名；错误source/readonly挂载表或部署env拒绝；外部COMPOSE_PROJECT_NAME/ASSETLIBRARY_*不能改变已核验目标；外来volume/network/container标签拒绝；PG VerifyFull错误CA/SAN/非TLS失败；现有LOGIN权限漂移拒绝；初始化重入不替换密钥或已有数据。

上述只是待执行入口，不是通过记录。用户先集中实现再统一验收的指令覆盖了通常的逐任务测试节奏；最终真正结果由根执行后更新。

## 已授权并执行的统一构建/窄回归

- 初始同源bundle由fbfc275生成并校验SHA后传dev-230新自有目录，exact checkout。后续abcb7aa只改失败的pnpm下载RUN（network-concurrency2/fetch-retries4/max-backoff60s），供应链检查与锁不变。
- Docker Core/setup构建通过；打包首次在sudo创建的images.tar读权限失败，c7d6065改为调用用户打开xb文件并接收docker save stdout，成功镜像复用。
- 离线包SHA256SUMS全文件读回通过；image tar753087488 bytes/SHA a257efa6e2ddd11bd9608d489f86650597abc913ce35e9d5945c1c0e32c9e931。
- `dotnet restore tests/dotnet/AssetLibrary.ReadCore.Tests/AssetLibrary.ReadCore.Tests.csproj --locked-mode --disable-build-servers`通过；用同一server阶段SDK10.0.111。
- `dotnet build tests/dotnet/AssetLibrary.ReadCore.Tests/AssetLibrary.ReadCore.Tests.csproj --configuration Release --no-restore --disable-build-servers`通过，0warning/0error。
- `dotnet test tests/dotnet/AssetLibrary.ReadCore.Tests/AssetLibrary.ReadCore.Tests.csproj --configuration Release --no-build --no-restore --filter FullyQualifiedName~PosixBackslashNamesAreRejectedWithoutChangingPhysicalIdentity`：2 passed/0 failed/0 skipped，63ms，TRX为posix-two.trx。
- SDK提取仅复用已完成server层缓存，不重新编译旧阶段；创建的自有SDK提取container按task标签验证后移除，没有运行持久服务。
- 此处没有NAS运行测试。内核资源兼容修正是根据根实际docker info反馈，不能把Compose文件存在写成NAS已通过。

镜像更新source29658e1：只执行Core/setup缓存构建与离线导出，所有原base digest/PG/Web输入保持不变。新images.tar SHA5e68b7e76bb44affe3ec8567532a5d68e98762dc69a85f1be6037445d571de60，753087488 bytes；新bundle SHA256SUMS全部通过。根报告70a01e8的25个Pwned回归已通过，本任务未重复这组或旧测试；NAS不在本子任务执行。

CA修复包fb6fc0a：仅缓存构建Core/setup+离线导出及全文件SHA清单检查，通过。标准CA来自系统ca-certificates包；无TLS验证关闭、无原测试重复、无NAS运行。本包images.tar SHA df894a32ebd169a361f9219ac6d6a2ea6e6ef4119910455ea1a9480404e0192b。
