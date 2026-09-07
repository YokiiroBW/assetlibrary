# NAS Docker 与第一版 Web

此部署沿用现有只读试用核心：登录、登记物理目录、首次扫描、浏览和名称/路径搜索。Web由同一个Core容器通过HTTPS提供。只运行Core与PostgreSQL两个长期服务；初始化、迁移和管理员操作使用执行完即退出的setup容器。它不开放资产写入、预览、下载原文件、通用重扫、Windows Service或Explorer功能，也不替代完整Alpha门禁。

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

initialize仅在新建或能识别为自己的卷中准备私密目录和证书，启动自己的PG16.15，然后通过既有migration_tool provision角色、验证备份、应用18个前进迁移，最后调用Host初始化授权密钥。重入不更换密码、部署ID或授权密钥；半写入的PKI和身份不一致会被保留并拒绝覆盖。

首次HTTPS证书是90天、自签名、SAN匹配配置Origin的RSA3072证书。将导出的**公有证书**通过可信方式交给使用者，核对指纹后由使用者手动配置浏览器/系统的当前用户信任，或采用匹配主机名的可信RSA证书。不要跳过TLS校验。本工具不导入CA到宿主信任库。数据库另有独立私有CA，服务证书SAN为`postgres`；Core与setup的跨容器连接均使用VerifyFull及该CA，数据库端口不发布到NAS。

operator在一次性容器中隐藏读取口令，再以UID1654调用同一个Host带外入口；口令不进入命令行、Compose环境或持久文件。首次bootstrap/recover仍需真实联网风险筛查。Core/operator有单独出站网络，PG仅接内部网络；网络故障不能变成跳过检查。已存在账号的正常登录和只读查询不新增风险源依赖。

失败重试保留同一authorization ID、operation ID和10分钟有效期，并输入同一口令。开始不同操作或过期后，显式加`--new-attempt`。受控自动化可加`--password-stdin`，将口令传入标准输入并关闭输入；不要在Shell历史、进程参数或日志中放入口令。

```sh
./nasctl.sh operator recover --account admin --new-attempt
./nasctl.sh operator rotate-key
```

## 停止、保留和恢复

```sh
./nasctl.sh stop
./nasctl.sh start
./nasctl.sh down
```

stop先正常停止Core，再停止PG，等待最多60秒；down移除本project的容器和网络，**保留所有持久卷与资产**。没有清空卷或自动删除旧部署的入口。机器重启后Compose的两个长期服务按unless-stopped恢复。非root运行、只读根文件系统、最小capabilities、no-new-privileges、内存硬限和受限tmpfs均在Compose中声明；资产只读挂载不因容器重启而改变。

目标NAS内核不支持CFS CPU硬配额或cgroup PID控制；因此使用Core1024、PG512、setup256的相对CPU shares，不声称它们是CPU百分比上限，也没有PID硬限。内存上限、Core请求/Worker并发界限及PG60连接上限保留。此部署不修改NAS内核、不施加影响其他服务的全局nproc，也不把文件句柄限额当成PID隔离的替代。

四个卷分别保存Core私密state、PG数据、PG TLS材料、setup控制数据/数据库口令/迁移备份。它们不能与资产混放。备份时先停止两服务，对自己这四个卷和原始部署目录做受限备份；软件备份不包含资产。迁移工具的每次前进迁移都会先生成并验证数据库备份，但这不等于持续软件备份功能已经实现。保留部署ID、原证书/口令、授权密钥与DP目录，不能通过删除它们“重置管理员”。

证书续期必须使用含RSA私钥≥2048的PKCS#12；新服务器证书及历史DP解密证书均不支持ECDSA-only。停止后保留旧证书和口令，将新文件放入Core私密state，并按Host的`decryption_certificates`（最多3张）保留旧DP解密材料，再更新`tls_certificate_file`及`tls_certificate_password_file`。所有文件保持UID1654、0600，目录0700；验证原账号与密钥可用后再继续运行。没有自动重加密历史密钥、自动CA轮换或跨版本升级承诺。

## 构建与来源

在有Docker、Python3.12+的受控构建机上，从已提交且干净的源码执行：

```sh
python3 -I -B scripts/build_nas_deployment.py --output-root .runtime/sandbox-storage/V01-022/build-01
```

构建工具复用现有Git快照/路径边界/流式hash工具。Base images在本次构建前解析为不可变digest，Core/setup使用同一source revision；.NET10.0.111 SDK由官方Linux归档及固定SHA512获取，避免依赖不存在的MCR feature-band镜像标签。Runtime来自该固定SDK的shared目录；Node24.20.0、pnpm11.19.0、PG16.15不降级。Python仅为3.12+的一次性工具，当前选择3.13 bookworm并记录具体digest。

需要把同一源码送到另一台构建机时可先执行`--context-only`，输出Git source-context.tar和source.bundle及SHA256。验证传输hash后，构建机从bundle克隆并checkout记录的source commit，再执行同一build命令；不要在解包源码上伪造新的源码commit或改版本以利用缓存。

交付包包含三个image的离线`images.tar`、镜像ID/源commit/tree/base digest清单、Compose/CLI/示例/说明与SHA256SUMS。镜像构建成功仅记录built_not_deployed；真实NAS权限、只读资产、TLS、浏览器、数据库持久性与重启必须在最终统一验收中另行确认。原V01-008 Docker诊断入口和Windows交付保持独立。
