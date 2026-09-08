# Core图片接线验证点

当前为partial。生产Host已接GET图片路由、精确端点metadata预算（图片15秒，旧JSON5秒）、GatewayAuth初始/末次库授权、末次session与紧邻写入的源核验。PreviewProvider只调用既有LibraryStorage公开根查询；没有数据库迁移/跨表访问。缓存64MiB/256项，2个响应lease持有名额至发送/清理完成。缓存命中仍获取稳定源与strong hash，不作为授权。

源读取使用同Host的独立 `--read-only-worker preview-source`，在任何Host/DB配置前分派。固定JSON事实请求由已授权核心生成；原图只读逐层no-follow，副本在本请求local temp，Linux即刻unlink、Windowsdelete-on-close。复制/再次hash/文件身份和完整当前mtime复核后才发布内部ready。broker只把稳定字节交独立decoder；Linuxsource进程有parent-death SIGKILL，父取消回收进程。源消失/替换/链接变化按SourceChanged失败，物理读权限或服务失败保持Unavailable。

隔离decoder只接固定profile+有界字节；Core校验完整输出PNG：尺寸/字节、单一IHDR、至少IDAT、IEND正好EOF、每块CRC、8bit RGB/RGBA/非交错。允许的ancillary仅已实测的单一sRGB，拒绝文本/EXIF/ICC/动画/未知块；Host不做媒体解码。源与输出校验完成前不会缓存或返回派生字节。

## 实际已通过

- Host与Core Release零警告编译；源策略和架构基线通过。
- Windows源/服务/cache/容量14项中13通过、1文件symlink因权限缺本机证据；root另以Linux真实源码测试补9/9（老源组），不重复计数。
- 实际Host子进程source broker的复制/strong hash/复核/句柄释放1/1通过。
- PNG输出结构/CRC/截尾/追加/text/animation/重复IHDR负例2/2通过。
- 本机同serve.py真实Core/PG/HTTPS LiveImageEndpointTests 2/2：图片401 `unauthenticated`、旧JSON401 `authentication_required`、query/Origin/POST-CSRF、invisible404及引擎disabled503。没有假服务，没有把503算出图。
- 同真实PG的timestamptz往返1/1（2000前后、100ns余数）通过。只量化持久观察，当前stamp仍全精度。
- 此次fixture显式stop后 `NATIVE_CLIENT_CLEANUP verified`；148合成原件hash/mtime、Host/PG、角色、证书、临时目录全部按既有fixture完成核验。证据 `.runtime/preview-live-evidence/20260908T144356Z-6ac46cc6`，用例TRX在 `.runtime/preview-tests/live`。

## root复用步骤

使用已批准的serve.py参数启动同一生产Host，不改测试fixture：

```text
python -I -B tests/integration/native-clients/serve.py --execute --dotnet <pinned-dotnet> --postgres-bin <PG16.15-bin> --web-root <reviewed-dist> --lifetime-seconds <bounded> --evidence <owned-evidence> --image-fixtures <synthetic-manifest-directory> --image-preview-worker <actual-isolated-worker>
```

收到READY后，把其中的私有connection_file只作为测试进程环境 `ASSETLIBRARY_PREVIEW_CONNECTION_FILE`（禁止打印JSON）。当且仅当期望真实解码可用，设置 `ASSETLIBRARY_PREVIEW_EXPECT_AVAILABLE=1`，运行：

```text
dotnet test tests/dotnet/AssetLibrary.Preview.Tests/AssetLibrary.Preview.Tests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~LiveImageEndpointTests --logger "trx;LogFileName=live-images.trx" --results-directory <owned-results>
```

可用性为1时必须200实际PNG且检查方向尺寸/字节/格式/no-store，验证JPEG/WebP/旋转/透明与损坏/超限拒绝；缺worker不得伪装通过。未设worker的兼容fixture可用性为0，只验证正确503。

PostgreSQL时间精度独立测试复用原 `ASSETLIBRARY_TRIAL_TEST_CONNECTION`（仅本次临时PG角色/连接），filter `ImageTimestampIntegrationTests`。source broker用 `ASSETLIBRARY_TEST_DOTNET` 与 `ASSETLIBRARY_TEST_HOST_DLL`，filter `ImageSourceBrokerTests`。

## 尚待完成

Linux真实HTTP与跨端200、缓存撤权/源变化/断流/取消/启动故障的集成补证；原生worker默认资源/隔离已由root在开发Linux证明，仍非NAS核。Windows由V03-006接手status7和隔离/清理。Windows无取消native启动的有界detached清理与名额保留正在配合实现，不能声称已满足硬启动截止。常规/auth相关回归、最终全diff/门禁及标准handoff仍待完成，完整V0.3和Provider门禁不关闭。
