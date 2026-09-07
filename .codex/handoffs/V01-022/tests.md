# V01-022 — 实际验证记录

实施阶段遵守先集中实现后统一验收；根授权后执行以下构建与窄回归。本子任务没有NAS运行测试，不重复根已通过的仓库/架构/.NET/Packaging63/Web40或风险检查25项。

## 首次统一构建和POSIX回归

- exact Git bundle/context经SHA校验后在dev-230构建。官方.NET SDK10.0.111 Linux归档SHA512校验通过，Host/Web生产构建成功；55条Web供应链校验保持启用。
- npm连接重置仅以network-concurrency2、fetch-retries4、最大退避60秒修复失败层；版本与锁不变。sudo导出权限失败改为调用用户打开xb文件接收docker save stdout，成功镜像不重编。
- 从同一server缓存阶段导出的SDK10.0.111执行`dotnet restore tests/dotnet/AssetLibrary.ReadCore.Tests/AssetLibrary.ReadCore.Tests.csproj --locked-mode --disable-build-servers`。
- `dotnet build tests/dotnet/AssetLibrary.ReadCore.Tests/AssetLibrary.ReadCore.Tests.csproj --configuration Release --no-restore --disable-build-servers`：0warning/0error。
- `dotnet test tests/dotnet/AssetLibrary.ReadCore.Tests/AssetLibrary.ReadCore.Tests.csproj --configuration Release --no-build --no-restore --filter FullyQualifiedName~PosixBackslashNamesAreRejectedWithoutChangingPhysicalIdentity`：2passed/0failed/0skipped，63ms；证据posix-two.trx。
- 后续风险网络4秒、setup系统CA均来自根验证修复；仅更新受影响缓存层和离线包，没有重复测试。目标NAS不支持CFS/PID硬配额的结果由根提供，配方改用CPU shares并保留内存/应用上限。

## 最终运行层刷新818d028

1. 干净源码执行`python -I -B scripts/build_nas_deployment.py --context-only --output-root .runtime/sandbox-storage/V01-022/context-runtime-final-01`。source.bundle SHA256 `ab12b80e45332846dc0af6cd7801ac26089509b99798d3ae94bb9826663c443f`；context SHA256 `c8de2f002c8c5569b66e99561ed779450fd5f84212d1e8ce0d2d7194c110fbc4`。
2. dev-230校验bundle，checkout `818d02827ee41f4e7f1e6bbb29d3841b110ab3c8` / tree `85abae49a6c3746bae874f84bf54dbd306aa93ad`；Git全文件差异限于部署文件和交接。Dockerfile精确比对确认fb6fc0a之后只有Core运行时定位RUN改变，所有应用/SQL/锁与setup内容未变。
3. 固定上一正式CA包Core/setup ID与4个base ID，使用现有Docker26 legacy builder按runtime-cache.Dockerfile更新运行层和来源标签，不联网下载、不重新编译Host/Web、不运行测试。第一次构建前的精确比较因脚本预期旧RUN换行形式不符而停止，未创建镜像；核对真实旧行后修正比较，使用新的runtime-final-02目录成功完成。
4. 复用现有save_images_archive流式导出3-image归档；images.tar 754869248 bytes，SHA256 `d4582e7b0894bc9c5f31a8cf18860a24e7214aadbaff6fe55edd69b0ff884336`。原件`sha256sum --check SHA256SUMS`全部通过。
5. 用Python shutil.copytree(copy_function=shutil.copyfile)复制到此前不存在的NAS共享build-818d028目录，重新核验images.tar SHA及全文件SHA256SUMS全部通过。原归档保留。

此次未执行实际NAS load/start/operator/扫描/浏览器/重启。那些属于主协调者V01-021验收，不能从构建或文件hash通过推断完成。
