# V01-022 — NAS Docker实施候选

状态partial：连贯实现已提交供主协调者审查；按用户“集中实现后统一验收”的要求，本任务没有执行构建、测试或部署，不宣称可用性、NAS验收或Docker门禁通过。

Owner server-packaging-owner；分支codex/v01-022-nas-docker-deployment；工作区C:/YOKI/Codex/AssetLibrary-worktrees/V01-022。已合入根8270475，包含NAS健康探针loopback传输、Web路径/快照说明及POSIX路径边界修正。未改Host/Web/SQL/根政策/依赖锁；原Windows成果与V01-008 Docker证据入口保留。

## 实现

- infra/docker/nas/Dockerfile：同源Host+Web；官方SDK10.0.111 Linux归档以根提供的SHA512固定，避免依赖不存在的MCR feature-band tag；Node24.20.0/pnpm11.19.0/PG16.15不降级。Core不带Python/PG部署工具；独立setup镜像仅作一次性作业。
- compose.yaml：Core与PG两个长期服务；PG不发布端口，仅internal网络；Core/operator另有egress保持真实风险筛查。资产由生成挂载表只读bind到/assets/<source_key>，缺失源不自动创建。Core UID1654、PG UID999，read_only根、cap_drop/no-new-privileges、资源限额、60秒正常停止。
- bootstrap.py：仅初始化自己的4个卷，持久deployment/集群身份、0700/0600/分属UID；内部PG CA+SAN postgres+VerifyFull；Web SAN匹配Origin；调用原migration_tool provision/验证备份/18迁移；六个不同NOINHERIT LOGIN及独立migrator；Host带外operator降权执行，口令有界stdin、同逻辑attempt重试，无风险/TLS绕过。
- nasctl.sh：NAS宿主只需Docker/Compose和Shell；镜像ID、resource owner、完整参数与挂载重生成比对后再操作。Compose显式project-name且只导出已核验的工具变量，不source env，不受外部COMPOSE_PROJECT_NAME/ASSETLIBRARY_*覆盖。普通操作不反复hash大images.tar；load首次校验归档。down保留卷，无删卷入口。
- build_nas_deployment.py：复用release Git快照/边界/流式hash；固定base image digest后构建，保存离线3-image归档/ID/SHA及source commit/tree。支持仅准备可校验Git bundle/context以送至构建机。构建本身不冒称部署验收。

## 复用与影响

复用现有TrialConfiguration/Host CLI/认证状态机/私密state/物理根与扫描核心、migration_tool及build_server_release基础工具；没有复制权限、扫描、资产身份等业务规则。NAS新增部分只是Docker生命周期、PKI和部署LOGIN配置。没有新主语言、应用框架、数据库或持久业务服务。

新增CLI/4个named volumes/内部DB服务名为本部署接口；/assets/<source_key>已和V01-023对齐。与资产规模有关的处理仍在既有核心；本工具只处理≤32个source与固定部署文件，构建/导出按字节流式工作，不声称50万资产性能已通过。

## 待统一验收

所有实际构建/测试/部署均未执行。根将使用dev-230构建、NAS原生Docker24/Compose2.20.1部署，并统一核对镜像可用性/工具依赖、PG第一次启动SCRAM与TLS、初始化重入、非root和只读挂载、真实bootstrap/recover、浏览器/持久性/重启、前后台与资源归属边界。NAS目标Origin/端口由根配置，示例文件不硬编码真实资产路径。

候选风险：官方base tag/digest解析及Python3.13跨同Debian层的依赖闭包待真实构建；PG entrypoint与只读root/tmpfs/SCRAM配置待实际启动；用户证书信任必须手动，PKI半完成拒绝覆盖；NAS主机路径需根验证真实/非链接且非Docker状态；最终通用发布门禁仍由根裁决。

建议先合此候选及根8270475，再统一验收与收口证据。不要把0已执行测试理解成通过；本子任务没有触碰远程其他容器、NAS既有资源、真实资产或Windows交付。

可审查实现commit：e676f09c1d5618488b01d36fed9c38ed03d82e83（主候选4c96349加双斜杠根拒绝）。根要求的环境优先级封闭、挂载表精确重生成比对、host_path双斜杠根拒绝均包含。工作区Git clean；仍未运行构建/测试，等待唯一统一验收阶段。

## 统一构建阶段结果

原实施阶段遵守不构建/不测试要求；根统一静态与Host验收完成后，授权本任务在dev-230执行镜像/离线包构建及新增2个POSIX参数用例。以下为实际结果，不覆盖或冒称NAS运行验收：

- 镜像源commit `abcb7aaa0de46a1ed1d3d653002fb86b024f1e4e`，tree `9dde5f7b6107ff2acbcd32001c3c1f15d4cd1e5a`；基于根fbfc275，仅为npm连接重置增加低并发/有界重试。Node/.NET/PG/锁/attestation不变。
- Core与setup镜像真实构建完成；离线images.tar共753,087,488 bytes，SHA256 `a257efa6e2ddd11bd9608d489f86650597abc913ce35e9d5945c1c0e32c9e931`。导出工具commit `c7d606558d95fa0e8e38279ac738b144db454816`。通过调用用户先打开文件、docker save流式stdout写入，避免sudo生成不可读归档；没有重编成功镜像。
- Docker26构建器没有buildx，使用已有legacy layer cache；一次网络失败发生在Web依赖RUN，55条供应链校验保持通过，降低并发后该层成功。官方SDK10.0.111 Linux归档SHA512在镜像层校验通过，Host/Web生产构建完成。
- 同一server缓存阶段导出Linux SDK10.0.111，仅构建目标ReadCore测试项目，并一次执行 `FullyQualifiedName~PosixBackslashNamesAreRejectedWithoutChangingPhysicalIdentity`：2通过/0失败/0跳过，63ms。未重复旧大套、Web40或Packaging63。
- 根在NAS实测cfs_quota/cfs_period和PidsLimit不支持；Recipe改CPU shares(core1024/PG512/setup256)、移除pids_limit，保留内存及其他安全/应用上限。该Recipe commit `70b5048566c8660b6036d979bb32c1129251947e`，原镜像未修改。
- 可加载Recipe目录：`/home/yokiirobw/.cache/assetlibrary/V01-021/nas-fbfc275-b18a9d/checkout/.runtime/sandbox-storage/V01-022/recipe-01/assetlibrary-nas`。其build-manifest记录image/export-tool/deployment-recipe三个实际commit；镜像tar复用已验证文件，SHA保持相同。NAS上传/启动及正式资产验收由根负责。

镜像ID/base digests与可定位证据见build-evidence.json和远端build-manifest.json。当前状态仍partial，等待根NAS验收；没有对其他服务、内核、全局nproc或正式资产执行变更。

## NAS风险网络适配后的镜像更新

根提供70a01e8：真实风险网络上限2→4秒、管理员总截止仍5秒，TLS/body/cache/fail-closed不变；25个风险回归由根通过。合入后的镜像source为29658e1791198e6c84810dc0baa486d100598e91，tree25da4d6fba1f984563370bad744ea8fef21d754e。

本任务只借已有缓存更新Core/setup并重导bundle；PG保持原abcb标签/同一ID，4个base digest完全不变，未重跑其他测试或NAS运行。新包位于dev-230 `/home/yokiirobw/.cache/assetlibrary/V01-021/nas-fbfc275-b18a9d/checkout/.runtime/sandbox-storage/V01-022/risk-update-01/assetlibrary-nas`。images.tar 753087488 bytes，SHA256 5e68b7e76bb44affe3ec8567532a5d68e98762dc69a85f1be6037445d571de60；manifest SHA c8474d435556f8e8574de59cac0e1b657fa91b2fc588e126ca8e6ac779c3c356。SHA256SUMS全文件验证通过。

Core ID sha256:1b733a68908c79539ece4f5c309e9317076afc02f84f9b35087968deffdd5938；setup ID sha256:e31289237cc27a988ff66ae85fa0cdb2d1ad55e27cf6d5657928f5c1a7f41341；PG ID仍sha256:5f71c21b69a7977b82247582e2e731ed76bdebaadb7dd7945ed76bcc9ed06632。setup仅透传根显式配置的HTTPS_PROXY，排除postgres/core/localhost/loopback；没有在NAS新增长期业务服务或关闭TLS。NAS实际操作及最终验收仍由根执行。

## 正式setup系统CA修复包

根真实诊断确认旧setup缺少标准CA根（BCL报告UntrustedRoot），同一链在Core正常；根4d17af4仅加入ca-certificates，不关闭证书验证。合入后同源镜像commit fb6fc0a4dc44f4ad0d7fb69d1d9fd90dd95a5bbd / tree795efe55befd4413df8eb486b16c8d8719fd0455。

Core/setup借已有缓存完成更新，PG与四个base digest不变；没有重复测试或操作NAS。正式交付目录dev-230 `/home/yokiirobw/.cache/assetlibrary/V01-021/nas-fbfc275-b18a9d/checkout/.runtime/sandbox-storage/V01-022/ca-update-01/assetlibrary-nas`。images.tar754859520 bytes/SHA256 df894a32ebd169a361f9219ac6d6a2ea6e6ef4119910455ea1a9480404e0192b；manifest SHA fb09b40ff2ef2e4d7f64017ccee94be67ef8733469f5b032d857abbb92bb102e；SHA256SUMS全部通过。Core ID7d5e945e9b7738f524bafa3c81678634a331dee130d838f8d0242ec5f8da76e3，setup IDb19b6d248f0fca6dd844661125bc300d3ab9ffbee58240aebe0fad5ef9b758bc。此正式setup替代旧临时公开CA只读挂载，实际NAS替换和验收由根完成。
