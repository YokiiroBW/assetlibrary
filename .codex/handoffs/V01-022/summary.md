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
