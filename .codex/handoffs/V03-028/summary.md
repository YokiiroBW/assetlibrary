# V03-028 — Core同机图片socket适配

状态ready_for_review。分支codex/v03-028-nas-local-image-adapter，工作区C:/YOKI/Codex/AssetLibrary-worktrees/V03-028。代码a173210614b283e087200fd85109f3624efbeb5a；root负责Linux/NAS及完整同源容器闭环。

2026-09-13追加开发Linux验证：固定协调源码a87b44c在dev-230独立副本、指定镜像与2GiB/2CPU容器中完整运行预览测试；合并一次补充source-broker后131个唯一用例93通过/0失败/38平台或缺实机夹具未执行。真实Linux SO_PEERCRED UID0正控通过，Core/Host严格构建通过；两个容器已按归属验证后删除。没有修改源码或操作NAS，原始TRX及边界见[Linux证据](linux-tests-a87b44c/README.md)。a87已包含root的生命周期与测试链接整合，不把该源码和本组件初始a173混同。

2026-09-13追加Windows旧路径定向验证：固定协调源码c775b7d独立归档、原win-x64 RID锁、SDK10.0.111与本机MSVC实际NativeAOT发布通过，仅运行既有LPAC真实缩略图测试1/1通过、0skip。原临时profile/日志清理、sandbox读回为空和自有进程0均确认；3564个归档文件未变化。未改安装、注册、用户配置或操作Explorer/NAS，详见[Windows证据](windows-lpac-c775b7d/README.md)。该单项不与此前不同提交的Linux结果合并计数。

2026-09-13追加原Linux seccomp路径：dev-230固定c775镜像，user1654/capdropALL/NNP/readonly/networknone/512MiB，Worker无参数运行JPEG/PNG/WebP各profile0/1共6/6通过。精确Ready、Success、全PNG CRC/尺寸、exit0与六容器清理均验证；原夹具SHA/mtime不变。与final7f09290的14个Worker.cs/.csproj Git blob完全相同，未运行其它probe/Windows套件或操作NAS。见[Linux旧路径证据](linux-seccomp-c775b7d/README.md)。

## 完成与API

ImagePreviewRuntime.Create在末尾增加可选string? socketPath=null，已有调用源兼容。TrialImagePreviewConfiguration只读取ASSETLIBRARY_IMAGE_PREVIEW_SOCKET并传入该参数。仅精确/run/assetlibrary-image/decoder.sock且Linux可选择新UnixSocketImageDecoder；旧WORKER非空时与socket互斥。未知路径、URI、空格或平台不符返回既有Unavailable query，基础浏览保持可用；不更改旧本地进程路径。

UnixSocketImageDecoder实现既有IImageDecoder与IDisposable，单实例SemaphoreSlim(1,1)；等待从同一个8秒linked token开始，承接Core15秒外限。调用者取消保留OperationCanceledException；内部超时映射Timeout；Dispose取消、关闭在途socket并唤醒等待者，不join；所有调用finally释放名额后，最后caller且取消传播结束才Dispose同步对象。无无界服务端队列，外层ImagePreviewService原两项应用许可未修改。

UnixSocketImagePeer在Linux以getsockopt(SOL_SOCKET,SO_PEERCRED)读取12B ucred，要求UID0；不把socket inode UID1654当服务UID，也不要求跨PID namespace可能不可见的peer PID。错误/不支持即失败关闭。路径与peer委托覆盖只在internal构造函数供自有socket测试，生产Runtime使用无参固定构造，不从客户端/环境接收验证策略。

UnixSocketImageTransport复用24B ImageWorkerProtocol和ImageWorkerTransport.ValidatePng。Ready必须status1/profile0/length0/width0/height0；失败只status4..7且其它字段0。源长度1..32MiB，复用ProcessImageSourceLease.CopyToAsync的64KiB流式精确Length和hash复验，不在适配器再复制源/hash规则。发完不半关闭；收到完整有界响应/PNG后关闭连接，不等待EOF。请求profile仅512或1600，结果profile/长度/CRC/尺寸等仍由现有校验约束。当前缓冲已有额外响应字节会拒绝；未来字节不会进入下一请求，因为每连接只处理一次后关闭。

## 集成清单

本任务未修改Application、worker源码、csproj或锁。root已批准在ImagePreviewService.Dispose释放 `(decoder as IDisposable)?.Dispose()`，由root单写；不改IImageDecoder签名。测试项目需增加Compile Include源链接三项：

- Modules/PreviewProvider/Infrastructure/UnixSocketImageDecoder.cs
- Modules/PreviewProvider/Infrastructure/UnixSocketImageTransport.cs
- Modules/PreviewProvider/Infrastructure/UnixSocketImagePeer.cs

新测试SocketImageConfigurationTests.cs、SocketImageDecoderTests.cs、SocketImageTestServer.cs由默认测试glob包含，不改原worker测试。此工作区使用root允许的.runtime/socket-test-links.targets临时源链接验证。

与监督器owner已确认：失败字段全部0；成功发布前已确认decoder实际exit/reap、持久状态更新且decoder slot就绪；Core收完整帧后即可立即关闭。部署/UID1654客户端访问与UID0服务端身份、目录模式和NASMEMLOCK策略由其它owner/root负责。

## 验证与风险

SDK10.0.111 locked restore通过；Core与Host Release编译0warning/0error，新源format verify通过。新socket测试32通过/1 Linux项未执行；最终连同原有ImagePreviewService、AuthorizedImagePreview和PNG验证共45通过/0失败/1未执行，8秒。未把两轮结果重复累加。

Windows真实AF_UNIX测试使用临时路径、合成四字节源和既有安全PNG夹具，peer显式注入；证明不半关、不等EOF、源变化、错误帧、取消、排队时间与Dispose竞态。Linux实际SO_PEERCRED测试在本机明确Inconclusive，不能替代NAS跨UID/namespace、无敏感挂载、进程回收或完整Core授权图片链路。无NAS/VM/GUI/资产操作。

依赖保持Infrastructure→Application端口，Host只配置；原鉴权、缓存、源前后复验和外部AssetLink不改，无新NuGet/语言/框架/数据库/服务端HTTP端点。成本随单图≤32MiB输入及≤12MiB输出线性，与50万目录规模无关。Core没有持有Docker socket，decoder不获得路径、数据库或会话凭据。

建议先合a173210，再合root生命周期/测试链接和监督器/包，最后Linux及NAS受控验收。G4仍豁免；没有宣布图片生产启用或完整V0.3完成。
