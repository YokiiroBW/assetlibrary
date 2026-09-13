# 原生客户端真实 Core 临时联调

这个测试入口启动生产 `TrialHostFactory`、Kestrel HTTPS、持久认证、21 个生产迁移、6 个独立 NOINHERIT LOGIN 和真实扫描子进程。数据库为本次创建的 loopback PostgreSQL 16.15 集群，数据与账号来自隔离合成样例，不需要或接受 NAS 账号。它没有假后端或生产认证绕过开关。

Windows 进程外客户端/Host 与 Android 可使用同一服务器。测试服务器运行期间，普通客户端只能通过生产 HTTP 会话/查询访问，不能直接访问测试数据库。HIBP 的 HTTP 响应仅在已有测试程序集内替换；真实口令风险解析、operator bootstrap、密钥、Cookie、会话及数据库状态机继续运行。

## 准备与启动

使用 `global.json` 固定的 .NET SDK 10.0.111，先执行：

```text
dotnet restore tests/dotnet/AssetLibrary.WebGateway.Tests/AssetLibrary.WebGateway.Tests.csproj --locked-mode
dotnet build tests/dotnet/AssetLibrary.WebGateway.Tests/AssetLibrary.WebGateway.Tests.csproj --configuration Release --no-restore
python -I -B -m unittest discover -s tests/integration/native-clients -p test_serve.py -v
dotnet test tests/dotnet/AssetLibrary.WebGateway.Tests/AssetLibrary.WebGateway.Tests.csproj --configuration Release --no-build --no-restore --filter FullyQualifiedName~NativeClientTlsFixtureTests
dotnet test tests/dotnet/AssetLibrary.WebGateway.Tests/AssetLibrary.WebGateway.Tests.csproj --configuration Release --no-build --no-restore --filter FullyQualifiedName~NativeClientImageFixtureTests
```

提供可启动的 PostgreSQL 16.15 `bin` 目录与真实 Web `dist`。Web 只是生产 Host 所需静态目录，此入口不重新验证 Web。Linux 在能运行 initdb 的非 root 账号下执行；不支持外部/生产 PostgreSQL。

```text
python -I -B tests/integration/native-clients/serve.py --execute --dotnet <dotnet.exe> --postgres-bin <PostgreSQL16.15/bin> --web-root <built-Web/dist> --lifetime-seconds 7200 --evidence .runtime/native-clients-evidence
```

缺少 `--execute` 返回 77 / `not_executed`。有效期必须为 1..7200 秒；默认两小时。每次运行独立的证据目录。仅 `NATIVE_CLIENT_READY` 出现后客户端才能连接；输出包含非敏感 Origin、叶证书 SHA256、私密会合文件路径、stop 文件路径和服务器进程 ID。不要将 `connection.json` 内容复制进日志、提交、截图或报告。

私密 `connection.json` 包含合成管理员登录、没有库访问权的普通账号、库 ID 和预计文件数。临时目录内的样例有 138 个文件，覆盖 100 条分页边界、中文及嵌套目录、空文件。通过 Native 客户端读取它们，不为 UI 伪造缩略图或内容预览。

Android 设备/模拟器保留与服务端完全相同的 localhost 身份：

```text
adb -s <device-id> reverse tcp:<port> tcp:<port>
```

然后在应用内配置会合文件的 `https://localhost:<port>` 和精确叶证书 SHA256。不要把 URL 改成 `10.0.2.2`：真实 Host/Origin 和证书 SAN 都绑定 localhost。设备反向映射由设置映射的客户端任务在结束时用 `adb ... reverse --remove tcp:<port>` 删除。

## 结束与验收

在 `NATIVE_CLIENT_READY` 返回的私密 `stop_file` 路径创建空文件，或等待指定生命期截止。正常退出会核验合成原件 SHA256/mtime、Host Dispose、证书私钥容器释放、HTTPS 监听关闭、Host/PostgreSQL 进程退出、6 个 LOGIN 删除和私密临时目录删除，之后才写 `acceptance.json`。进程异常、启动失败、跳过测试、清理失败均不产生通过证据。Ctrl+C 会尝试请求有界正常退出，但中断本身不记为验收通过。

CI 可将 `--lifetime-seconds` 设为 2 验证截止与清理；交互运行用明确 stop 文件覆盖另一条路径。最终验收必须等 `NATIVE_CLIENT_CLEANUP verified`，不能只因 READY 就算通过。

本入口只证明真实服务器及隔离生命周期，原生客户端的真实请求、错误处理、UI、TLS 拒绝、模拟器/设备证据由对应任务分别记录。临时 PostgreSQL 沿用已有 fixture 的 fsync 等测试优化，不作为断电耐久、50 万资产性能或真实 NAS 证据。

## 图片预览联合验收

V03-005 提供两个显式可选参数，默认行为和原138文件样例保持不变：

```text
--image-fixtures <synthetic-preview-corpus>
--image-preview-worker <absolute-published-worker-executable>
```

图片目录必须带 `kind=synthetic_preview_integration_inputs` 的生成manifest，最多128个文件，单文件最多32MiB、合计最多64MiB；Python staging与C#夹具分别限制实际复制字节数，不能绕过Python取消C#总量限制。逐文件长度/强hash验证后才复制到本次私密runtime。图片进入同一个被扫描库的 `图片样例/` 目录，客户端可通过既有搜索得到稳定UUID，不需要改变认证连接JSON。不得将个人素材目录作为此参数。默认清除从外部继承的图片模式环境变量，只有显式参数才启用。

可复现的10项合成输入随仓库保存在 `tests/integration/native-clients/fixtures/image-preview-v1`，总文件内容约357KB，直接作为image-fixtures参数即可，不需要安装图片生成库。图片由本项目绘制：四色与白边的JPEG/PNG/WebP、方向6的JPEG、透明椭圆PNG、中文目录内改名的相同JPEG，以及4项主动/损坏/超大头拒绝输入。manifest的mtime仅记录生成时来源；实际只读验证使用本次私密样例的真实mtime。预期失败文件不能当正常图片展示，也不能据样例存在宣称decoder通过。

worker参数只传给真实Host的 `ASSETLIBRARY_IMAGE_PREVIEW_WORKER`，必须是已发布、已验证的独立decoder，仍走生产相同的隔离/ready检查；没有fixture bypass或假服务注入。没有设置worker或隔离不成立时，图片端点应返回契约503，而不能算真实图片验收通过。

原生跨100项分页测试可生成120张真实合成图片，无需重新绘制或增加图片库：

```text
python -I -B tests/integration/native-clients/make_page_corpus.py --output .runtime/corpus
```

脚本固定逐字节复用上述已登记的landscape.jpg、landscape.png、transparent.png，各40份，命名001..120并按原格式保留后缀；读回强hash后输出相同kind的manifest。输出只能位于当前worktree的.runtime或系统临时目录，必须不存在，不覆盖旧证据；120份不提交二进制。将该目录传给 `--image-fixtures` 后，原138文件不变，总数258、图片样例目录120项。相同内容不同物理路径是有意副本，名称只是为测试排序；生成成功不代表解码、Core或Explorer跨页操作已通过，真实验收由调用线程完成。

合成图片的原hash/mtime继续参加既有结束核验；任何图片模式运行仍须等待Host、数据库/角色、私密临时目录与监听器完整清理。生成样例、启动服务与UI fixture通过都不能替代真实派生内容验收。

## 必测客户端边界

- 所有 POST 发精确 Origin；非 login POST 加同源 Cookie 与内存 CSRF。默认系统 TLS；显式叶证书 SHA256 仍要求主机名和有效期。
- 控制层早期认证/Origin 拒绝可能带 `request_id: "unknown"`。HTTP 401/403/404 即使 JSON 错误、请求 ID 不符、响应体卡住，也必须清除失去权限的数据；暂时 503/504 明确标记旧快照。
- 成功 envelope 校验 request_id、message_type、ok；详情校验 library/entry，浏览校验 library/parent/items，搜索校验固定范围。生成 SDK 仅保留 envelope，不替代这些关联校验。
- 查询最多每页 100；切库、改排序筛选/搜索、身份变化时取消旧请求并清空游标；游标和 anchor 不能一起发送。取消覆盖读取响应体，晚到响应不能覆盖新会话或新导航。
- 退出失败不能显示服务端撤销成功；本机清除和服务端确认明确区分。
