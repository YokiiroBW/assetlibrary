# V01-022 — NAS Docker交付包

状态ready_for_review：部署实现和最终离线包已交付；本子任务未运行NAS验收，也不宣布整个里程碑或完整Alpha发布通过。目标环境结果见V01-021交接。

Owner server-packaging-owner；分支codex/v01-022-nas-docker-deployment；工作区C:/YOKI/Codex/AssetLibrary-worktrees/V01-022。根ce9388c已合入；本次源码commit `818d02827ee41f4e7f1e6bbb29d3841b110ab3c8`，tree `85abae49a6c3746bae874f84bf54dbd306aa93ad`。后续仅交接记录提交，不改此不可变包。

## 交付与来源

- NAS共享副本：`/volume1/Documents/Codex-Workspaces/AssetLibrary/.runtime/releases/V01-021/build-818d028`；dev-230对应挂载`/mnt/astrbot-documents/Codex-Workspaces/AssetLibrary/.runtime/releases/V01-021/build-818d028`。目标此前不存在，copytree(copyfile)复制后全文件SHA256SUMS通过，旧包保留。
- 构建原件：dev-230 `/home/yokiirobw/.cache/assetlibrary/V01-021/nas-fbfc275-b18a9d/checkout/.runtime/sandbox-storage/V01-022/runtime-final-02/assetlibrary-nas`。
- images.tar：754869248 bytes；SHA256 `d4582e7b0894bc9c5f31a8cf18860a24e7214aadbaff6fe55edd69b0ff884336`。manifest SHA256 `f513a304072dc6cd989f6ad149af4cffdf4cdbfd7ede27dafd2bc6a727a2c9d2`。
- Core ID `sha256:085c7fe51c68bf7a4528d9e44f2629fd2b0a3e5c6eb7f8482eb2a3434dac477d`；setup ID `sha256:aed6cc7d538823e3f32f03115f01df35e99904abc541a81dd20260167a31b2ff`。两标签后缀均为源码完整commit。
- PG保留`assetlibrary/nas-postgres:abcb7aaa0de46a1ed1d3d653002fb86b024f1e4e`；ID `sha256:5f71c21b69a7977b82247582e2e731ed76bdebaadb7dd7945ed76bcc9ed06632`。四个base digest保持不变，详见build-evidence.json。
- 应用产物复用已验证CA包source `fb6fc0a4dc44f4ad0d7fb69d1d9fd90dd95a5bbd` / tree `795efe55befd4413df8eb486b16c8d8719fd0455`。Git全部变化仅为部署文件和交接；Dockerfile精确比较确认唯一内容变化是Core运行时定位文件。Core添加`/etc/dotnet/install_location_x64=/opt/dotnet`；setup原文件内容和CA包不变，只更新来源标签。manifest显式区分部署源码与application_payload，未将旧嵌入源码版本冒称为新编译。

实际增量构建配方保存为runtime-cache.Dockerfile，SHA256 `b93e358c3b755f6b215aa1e7fb3c01a399534e061f059d42722cfa9a6a8bde9e`；构建记录在dev-230上述任务根目录runtime-final-build.log。不拉新base、不重新编译Host/Web、不重复测试。

## 实现、复用与边界

仅Core+PG两个长期服务；Web同源HTTPS；一次性setup使用Python/PG/OpenSSL，NAS宿主只需Shell、Docker和Compose。保留Windows成果和旧V01-008入口。复用既有Host配置/带外operator/认证与扫描核心、migration_tool角色/备份/18迁移，以及build_server_release快照和流式hash；没有复制权限、业务SQL或扫描逻辑。

四个私密命名卷；内部PG VerifyFull+SAN postgres+独立CA、不发布PG端口；六个分离NOINHERIT LOGIN。Core非root UID/GID1654，资产只读bind至/assets/<source_key>，私密文件umask0077。可选读取组从deployment.env校验，默认1654；根实测目标NAS仅附加101即可，README已说明不改变主机ACL或只读挂载。清空环境的隔离Worker通过系统定位文件找到运行时。

nasctl核验镜像ID、project/资源归属、settings与重生成挂载表；不source环境文件，不让外部Compose变量改变已核验目标。down保留卷。Core/operator保留风险检查所需出站网络，setup含系统标准CA；根批准网络上限4秒、管理员整体5秒，保持TLS/body/cache/fail-closed。NAS只有相对CPU shares和内存硬限，无CFS/PID硬配额；不改内核或全局nproc。

部署工具只处理固定状态文件与至多32个source，构建/归档流式处理；资产规模处理仍在原核心，不宣称50万资产性能已验证。本子任务没有操作NAS运行容器、正式资产、其他服务、信任库或防火墙。

## 验证与交接

根统一仓库/架构/.NET格式与Release、Packaging63、Web40已通过；本任务此前仅运行新增POSIX两用例并保留TRX。最终刷新只做来源比对、两镜像增量构建、归档及源/共享副本全文件hash核验。完整命令和实际范围见tests.md。

建议主协调者合并本分支交接提交，保留已有部署卷后使用新包继续登记/扫描、浏览器、持久性与重启验收；NAS结果由V01-021记录。风险仍为目标NAS读取权限与实际运行闭环，通用重扫、资产写入及完整Alpha门禁未扩展。
