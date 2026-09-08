# Web 图片真实 Core 联合验收

此入口由V03-008准备，连接V03-005已启动并报告READY的同一个生产Core/HTTPS/PostgreSQL临时fixture。只消费既有私密connection.json和协调生成的十项corpus manifest，不创建/修改库、不扫描、不改数据库权限、不停止共享服务。

`real-core-image-preview.mjs` 是独立显式CLI，没有 `.spec` / `.test` 后缀，不进入默认Playwright假数据测试。不设置route、不代换响应/图片、不启用CSP bypass、不使用ignoreHTTPSErrors。没有READY或隔离decoder未可用时不能报真实预览成功。

## 准备

- root共享fixture提交5edf25c，`--image-fixtures`指向带manifest的已校验合成corpus，`--image-preview-worker`指向其批准的真实独立Worker。
- 库内固定 `图片样例/`，10个样例加原138文件；由fixture先注册/扫描，客户端只通过已有登录、浏览、搜索API发现稳定entry ID。搜索可以同时命中物理目录：先校验每条hit的库/entry关联、UUID与wire名称，再只选kind=file且精确位于`图片样例/`前缀内的文件，断言预期10条唯一齐全。`name`是`AssetLinkReadJson.Entry`实际写出的wire字段，不使用UI派生兜底。
- Web产物必须匹配本任务 `build-evidence.json` 中的JS/CSS大小和SHA256，或通过参数显式传入协调审查后的新build evidence。不以旧Web源码跑出图片验收。
- 使用仓库固定Node24.20.0与现有Playwright1.62.1依赖。命令中的connection路径由root就绪消息提供；绝不能把文件内容、密码放入命令行、日志、提交或截图。

## 命令（仓库根）

```text
node --check tests/web/real-core-image-preview.mjs
node --test tests/web/real-core-image-input-check.mjs
node tests/web/real-core-image-preview.mjs --help
node tests/web/real-core-image-preview.mjs --execute --connection <root提供的私密connection.json> --manifest <协调corpus/manifest.json> --evidence .runtime/real-core-preview/<new-run> [--build-evidence <reviewed-build-evidence.json>] [--interaction-only]
```

无`--execute`返回77，未执行不算通过。evidence必须是本worktree `.runtime` 下的新目录，拒绝覆盖。最迟5分钟或服务剩余生命期前10秒结束，浏览器总会关闭；fixture生命周期由root独占，不写其stop文件。必要时先把固定Node目录放到当前PATH；不要调用内部固定旧Node的bundled pnpm.cmd。

`--interaction-only`只用于同一fixture上已完成图片断言后的明确续验，仍重新验证TLS、Web静态hash与真实发现，然后检查QuickLook/历史/回焦、退出清URL和404/401账号隔离。receipt标`scope=interaction_continuation`且cases为空，不能声称执行了前16项；完整结论必须以合并索引绑定原case receipt和continuation的原始SHA，保留原失败顶层不改写。`shared_server_lifecycle=coordinator_pending`，只有root最终清理receipt可关闭共享生命周期。

## TLS、秘密和证据

只允许无userinfo/path/query/fragment的动态端口 `https://localhost`。先进行不发送HTTP/凭据的TLS握手，核对连接文件中的精确叶证书SHA256、localhost主机名及有效期，再计算该证书SPKI。Chromium复用既有真实Web fixture的 `--ignore-certificate-errors-spki-list=<该key>`；`ignoreHTTPSErrors=false`、`bypassCSP=false`。这是临时测试证书的局部信任，不改系统证书、不放宽产品TLS。任何叶指纹/主机名/有效期失败在登录前结束。

密码仅在内存从私密JSON读入和填入真实登录表单。不录制含登录请求的trace/视频，不打印原始异常、DOM或HTTP body。截图只在登录成功后的已知合成资产与空权限工作区阶段保存；公开失败记录只有固定阶段/错误类别。connection JSON不会复制到证据目录。

输出`web-real-image.json`记录实际状态、runner/Web来源、manifest摘要、真实HTTP成功尺寸/字节/PNG摘要、错误码、浏览器版本、CSP与账号隔离、截图和浏览器关闭。HTTP错误/未启用503或预览缺失一律使整体失败，不跳过或折算通过。只有真正在生产Host上完成才能记录相应scope的passed；入口准备和七项输入检查不算真实联调。

Playwright的取消请求不一定完成`Response.finished()`，不能据此无限等待图片证据。主用例先确认真正UI缩略/预览可见、自然尺寸与像素，再在预览打开（已暂停其他缩略图）时依次发同源真实GET，以20秒和编码字节上限核对原始wire PNG/头/摘要；不会向UI注入响应或图像。虚拟列表由真实Home/ArrowRight定位，关闭时原trigger仍在则回原行，响应式重挂卸载trigger则验证逻辑工作区回焦。每阶段写固定安全progress，整体硬截止和关闭确认均有界。

## 验证行为

- 真实Host的img-src允许blob；JS/CSS与审查产物哈希匹配，实际blob图片在未绕过CSP的浏览器中解码。
- JPEG/PNG/WebP派生thumbnail/preview的PNG、长度/MIME/no-store/nosniff/CORP、规格、比例、EXIF6方向、无放大与无源EXIF/text/APNG；透明图经canvas读取角点/半透明中心alpha。
- 中文目录下同JPEG内容的`.dat`仍可显示，派生摘要相同，格式由内容识别。
- SVG、不可信非图片、截断JPEG、超大头安全错误并保留L0；503/504/429不冒充此类成功降级。非图片字节可被服务分类为415不支持或422损坏，实际code/status会写入证据。
- 双击/关闭/焦点、Space不改历史；桌面、手机暗色与200%文字截图。
- 退出撤销所有Object URL；已有普通账号看不到库且使用已知entry ID得到404，退出后同图401，不返回image MIME。客户端不直写DB做即时撤权，真正的同账号动态撤权和资产hash/mtime由root的核心用例及同一生命周期结束核验。

准备阶段不重跑已有65项假数据回归；只检查新增脚本和输入边界。执行必须等root连接就绪，由root最终合并同一服务的浏览器/Android与资源清理证据。
