# NAS Docker 与第一版 Web

此部署沿用现有只读试用核心：登录、登记物理目录、首次扫描、浏览和名称/路径搜索。Web由同一个Core容器通过HTTPS提供。默认运行Core与PostgreSQL；同一包包含可选的第三个长期服务image，初始化、迁移和管理员操作仍使用一次性setup。image是同NAS的本地图片监督器，不是另一台计算主机；图片后端默认关闭，只有目标平台验收后才显式启用。它不开放资产写入、下载原文件、通用重扫、Windows Service或Explorer功能，也不替代完整Alpha门禁。

构建清单记录镜像来源；目标环境的部署验收见`.codex/handoffs/V01-021/`交接。构建成功本身不代表部署通过。

## 部署前准备

NAS需支持Linux x86-64 Docker与Compose v2；Container Manager终端中的Shell、Docker即可操作，无需另装Python、OpenSSL、PostgreSQL或.NET。这些工具分别位于运行镜像或一次性setup镜像。持久化数据放在Docker的命名卷中，NAS资产使用只读bind mount。

将构建交付目录`assetlibrary-nas/`完整复制到新的、自用部署目录。不要混放资产和其他服务的数据。命令只操作本部署project，并在复用卷、网络或容器前核验deployment标签；出现同名外来资源时停止，不重置它们。镜像归档必须来自可信构建结果。

```sh
cd /your/deployment/assetlibrary-nas
chmod 755 nasctl.sh
./nasctl.sh load
cp settings.example.json settings.json
```

编辑`settings.json`。示例：

```json
{
  "deployment_name": "assetlibrary-nas",
  "public_origin": "https://nas.example.local:5443",
  "bind_address": "192.168.1.100",
  "storage_sources": [
    {"source_key":"photos","display_name":"照片","host_path":"/volume1/photos"}
  ]
}
```

`public_origin`必须是电脑能够访问的NAS HTTPS地址；`bind_address`必须是NAS实际LAN地址，端口为1024以上。宿主发布端口、容器监听端口、Origin端口保持一致。不要把反向代理的443随意映射为另一内部端口。若NAS防火墙不允许该端口，应在NAS管理界面明确配置；本工具不修改防火墙或系统信任。

每个`host_path`必须是已经存在的NAS真实、规范化目录，不能使用链接、相互重叠的路径或不同别名指向同一个物理目录。不要选择系统目录、Docker数据目录或本部署状态。Compose不会自动创建缺失资产目录。`photos`固定映射为容器`/assets/photos`；在Web登记库时填写`/assets/photos`或其子目录，客户端C盘、UNC和NAS宿主路径不会自动出现在容器内。已有库保持首次扫描的已提交快照；本轮没有通用重扫。

## 初始化、证书、管理员

```sh
./nasctl.sh configure
./nasctl.sh initialize
./nasctl.sh certificate > browser-certificate.crt
./nasctl.sh operator bootstrap --account admin --display-name Administrator
./nasctl.sh start
./nasctl.sh status
```

configure生成一次性deployment ID、`deployment.env`和只读挂载表`assets.compose.json`。之后检测settings内容漂移；不要为了更改目录而删除ID、复制另一部署的卷或重新configure。若命令被中断，保留`.nasctl.lock`/pending文件并先确认没有同一部署操作仍在运行，再检查自己的残留；不会自动覆盖。

Core以UID/GID `1654:1654`运行。资产目录还需允许该身份读取文件、列出和穿过目录；只读挂载不会绕过NAS权限。如果需要现有NAS读取组，在configure后编辑`deployment.env`，加入唯一一行`ASSETLIBRARY_ASSET_READ_GROUP=101`。这里的`101`是本次目标NAS已验证的读取组，其他NAS须使用其实际具备读取权限的组号。该可选值只能是一个非零数字组号，省略时默认为`1654`；nasctl从文件读取并校验，仅为Core添加这个附加组，主UID/GID不变。执行`./nasctl.sh start`会按新配置重建Core并保留卷。此设置不修改NAS主机ACL，不让Core以root运行，资产挂载仍为只读。

initialize仅在新建或能识别为自己的卷中准备私密目录和证书，启动自己的PG16.15，然后通过既有migration_tool provision角色、验证备份、应用当前清单中的前进迁移，最后调用Host初始化授权密钥。V01-024的清单包含21条迁移。重入不更换密码、部署ID或授权密钥；半写入的PKI和身份不一致会被保留并拒绝覆盖。

升级现有部署前先正常停止Core、image（如存在）和PG，冷备四个既有专用卷、已存在的image-ipc运行卷及原部署配置，保留混合UID/GID、mode、ACL/xattr和链接。只替换并校验新包的公共交付文件，保留settings、deployment ID、挂载表和密钥，再initialize/start；不要重新configure或bootstrap。旧Host要求其匹配的迁移清单，回退必须同时恢复同一次冷备的数据库/私密状态与旧配置、镜像，不能只换旧镜像。软件备份不包含资产原文件。

首次HTTPS证书是90天、自签名、SAN匹配配置Origin的RSA3072证书。将导出的**公有证书**通过可信方式交给使用者，核对指纹后由使用者手动配置浏览器/系统的当前用户信任，或采用匹配主机名的可信RSA证书。不要跳过TLS校验。本工具不导入CA到宿主信任库。数据库另有独立私有CA，服务证书SAN为`postgres`；Core与setup的跨容器连接均使用VerifyFull及该CA，数据库端口不发布到NAS。

operator在一次性容器中隐藏读取口令，再以UID1654调用同一个Host带外入口；口令不进入命令行、Compose环境或持久文件。首次bootstrap/recover仍需真实联网风险筛查。Core/operator有单独出站网络，PG仅接内部网络；网络故障不能变成跳过检查。已存在账号的正常登录和只读查询不新增风险源依赖。

失败重试保留同一authorization ID、operation ID和10分钟有效期，并输入同一口令。开始不同操作或过期后，显式加`--new-attempt`。受控自动化可加`--password-stdin`，将口令传入标准输入并关闭输入；不要在Shell历史、进程参数或日志中放入口令。

```sh
./nasctl.sh operator recover --account admin --new-attempt
./nasctl.sh operator rotate-key
```

## 停止、保留和恢复

图片预览有两个互斥的受控后端。常规seccomp平台仍保留既有包内worker；本NAS的同机容器路径按ADR-0023使用同一Compose的 `image` 服务，PID1为BCL-only NativeAOT监督器，每请求一个UID/GID1655、cap0的短命decoder。Core仅把已授权图片字节发送到固定本地socket；监督器/decoder没有资产、PG、Core私密状态、用户会话或Docker socket挂载，也不发布网络端口。

目标平台的UID/namespace/MEMLOCK/NPROC/AS/CPU/memcg/取消/回收和真实Core出图证据通过后，在 `deployment.env` 添加唯一一行，保留其他身份与设置：

```text
ASSETLIBRARY_IMAGE_PREVIEW_SOCKET=/run/assetlibrary-image/decoder.sock
```

随后 `./nasctl.sh start` 会保持这个显式选择；旧部署没有该键时仍默认关闭。也可只为一次启动传入相同环境变量。命令环境显式值优先于文件，包括空值用于关闭：

```sh
ASSETLIBRARY_IMAGE_PREVIEW_SOCKET= ./nasctl.sh start
```

空值覆盖只针对本次重建；若需长期关闭，移除/清空deployment.env中的同一行。只有空值或上述固定socket路径被接受；重复键、任意路径或同时设置WORKER和SOCKET均拒绝。不要重新configure或重建deployment ID。旧常规worker仅允许固定 `/app/workers/image-preview/AssetLibrary.ImagePreview.Worker`，没有跳过seccomp的新开关。

image使用profile显式启动；只读镜像、network none、私有PID/IPC/mount、NNP、memory512MiB、cpu_shares256，可信PID1保持CHOWN/SETUID/SETGID/KILL四项能力。CHOWN用于socket所有权；不把一次线程capset声称为全.NET进程撤权。解码前所有decoder线程均为1655/cap0并受其固定限额；Root平台证据独立于Compose声明。

新卷 `deployment-name-image-ipc` 仅由Core只读和image读写挂载，根目录root:1654/0710、socket1654/0600；Root的state/lock/atomic-next文件为私密运行计数，不含图片。initialize/start用同一image创建一个从不启动的临时容器完成空named-volume的目录元数据copy-up，随后只移除该临时容器；不清已有state/lock/fuse。已有卷标签/本地driver/无host-path别名会检查，不复用外来资源。

启动前校验实际镜像ID、源码/部署标签、PID1入口、无init wrapper、能力、网络/IPC/端口、唯一IPC挂载、环境和512MiB/CPU shares配置；--health只读核验PID1/线程权限、socket/state/fuse及实际memory cgroup，不连接socket或启动decoder。完整内核隔离仍要目标NAS证据。健康/配置失败会停止本image服务并明确返回“Core正在运行、图片不可用”，不会停止基础浏览，也不会自动删除熔断状态或创建新卷绕过它。

重启策略为unless-stopped，正常NAS/Docker重启可恢复既有已启用服务；持久最多三次连续基础设施/清理失败由监督器熔断，初始化失败不得无限创建decoder。到达熔断时保留卷并检查健康/日志，由协调者受控维修，不以反复重建容器/删除计数代替清理验证。

```sh
./nasctl.sh stop
./nasctl.sh start
./nasctl.sh down
```

stop先正常停止Core，再停止image（10秒宽限），最后停止PG，Core/PG最多60秒；down移除本project的容器和网络，**保留所有持久卷与资产**。没有清空卷或自动删除旧部署的入口。机器重启后已启用的长期服务按unless-stopped恢复。非root运行、只读根文件系统、最小capabilities、no-new-privileges、内存硬限和受限tmpfs均在Compose中声明；资产只读挂载不因容器重启而改变。

目标NAS内核不支持CFS CPU硬配额或cgroup PID控制；因此使用Core1024、PG512、image256、setup256的相对CPU shares，不声称它们是CPU百分比上限，也没有PID硬限。内存上限、Core请求/Worker并发界限及PG60连接上限保留。此部署不修改NAS内核、不施加影响其他服务的全局nproc，也不把文件句柄限额当成PID隔离的替代。

四个原卷分别保存Core私密state、PG数据、PG TLS材料、setup控制数据/数据库口令/迁移备份，身份/用途不变；新增image-ipc只保存socket与监督器私密计数/锁。它们不能与资产混放。备份时先停止Core/image/PG，对自己四个原卷及已存在的image-ipc卷和原始部署目录做受限备份；软件备份不包含资产。迁移工具的每次前进迁移都会先生成并验证数据库备份，但这不等于持续软件备份功能已经实现。保留部署ID、原证书/口令、授权密钥与DP目录，不能通过删除它们“重置管理员”。

证书续期必须使用含RSA私钥≥2048的PKCS#12；新服务器证书及历史DP解密证书均不支持ECDSA-only。停止后保留旧证书和口令，将新文件放入Core私密state，并按Host的`decryption_certificates`（最多3张）保留旧DP解密材料，再更新`tls_certificate_file`及`tls_certificate_password_file`。所有文件保持UID1654、0600，目录0700；验证原账号与密钥可用后再继续运行。没有自动重加密历史密钥、自动CA轮换或跨版本升级承诺。

## 构建与来源

在有Docker、Python3.12+的受控构建机上，从已提交且干净的源码执行：

```sh
python3 -I -B scripts/build_nas_deployment.py --output-root .runtime/sandbox-storage/V01-022/build-01
```

构建工具复用现有Git快照/路径边界/流式hash工具。Base images在本次构建前解析为不可变digest，Core/setup/image使用同一source revision；.NET10.0.111 SDK由官方Linux归档及固定SHA512获取，避免依赖不存在的MCR feature-band镜像标签。Runtime来自该固定SDK的shared目录；Node24.20.0、pnpm11.19.0、PG16.15不降级。Python仅为3.12+的一次性工具，当前选择3.13 bookworm并记录具体digest。

独立图片构建层复用同一 SDK 和已提交的 linux-x64 AOT 发布锁，仅在构建层增加 clang/zlib 开发工具。Core 运行层仅复制图片可执行文件、`libSkiaSharp.so`、MIT 与第三方声明、`SHA256SUMS` 和 `SOURCE_REVISION`，不包含编译器或符号。打包工具创建一个从不启动的临时检查容器，提取这六个文件；校验完整文件集合、提交一致、SHA256、Linux x86-64 ELF 格式和 Linux 可执行权限，随后移除所属检查容器。检查失败不会生成交付归档。`build-manifest.json` 的 `image_preview` 记录实际镜像 ID、提交、RID、发布锁哈希、文件长度/哈希、默认关闭及平台尚未执行状态。

需要把同一源码送到另一台构建机时可先执行`--context-only`，输出Git source-context.tar和source.bundle及SHA256。验证传输hash后，构建机从bundle克隆并checkout记录的source commit，再执行同一build命令；不要在解包源码上伪造新的源码commit或改版本以利用缓存。

交付包包含Core/setup/PostgreSQL/image四个image的离线`images.tar`、镜像ID/源commit/tree/base digest清单、Compose/CLI/示例/说明与SHA256SUMS。镜像构建成功仅记录built_not_deployed；真实NAS权限、只读资产、TLS、浏览器、数据库持久性与重启必须在最终统一验收中另行确认。原V01-008 Docker诊断入口和Windows交付保持独立。

新增ImageSupervisor NativeAOT层复用相同SDK/AOT工具与已提交的ImageSupervisor/locks/linux-x64发布锁，不引入新NuGet。运行image仅复制BCL监督器、既有worker/Skia与许可证；没有SDK/Python/PG客户端。监督器单独包含固定SDK的.NET许可证/第三方声明、SOURCE_REVISION和SHA256SUMS。构建工具提取并验证监督器及image中的decoder，后者必须与Core中保留的旧worker逐文件一致；build-manifest记录两份镜像ID、锁哈希和文件完整性，仍标明未执行目标平台验证。

nasctl在启动等操作前重新检查小型公共控制文件SHA256，images.tar的大文件校验保留在load，避免每次status重新读完整归档。校验绑定交付来源，不是签名或内核验收；公共文件变更必须换用完整已验证包，settings/deployment.env/挂载表和四旧卷保持原部署身份。回滚时停止新增image，保留image-ipc（包括失败计数）；恢复匹配备份的四旧卷、旧配置和旧镜像，不能用删除IPC状态重置失败上限。
