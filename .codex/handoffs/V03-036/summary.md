# V03-036 跨页图片测试夹具交接

状态ready_for_review。分支codex/v03-036-explorer-page-fixture；代码afdc0df；worktree C:/YOKI/Codex/AssetLibrary-worktrees/V03-036。已交root代码供统一实机，本任务未启动服务或操作GUI/安装/NAS。

Python与C#显式图片上限由32改128，单文件32MiB/合计64MiB不变。Python保留实际流量计数与manifest路径/hash限制；C#从File.Copy改为64KiB流式复制，独立按实际单文件/合计字节检查，并读回复核SHA256，不能经直接调用绕过Python总量。原138文件与原件hash/mtime检查保留，零图片模式不变。

新增标准库make_page_corpus.py固定复制仓库image-preview-v1的landscape.jpg、landscape.png、transparent.png，各40份。120份均为真实合成图片，非占位数据；名称001..120零补齐、格式后缀保留，100.jpg→101.png易辨别。输出只准自有.runtime/系统临时目录，拒绝链接/覆盖，manifest最后写出。没有新图片库、解码器、运行依赖或120个二进制提交。

## 可复用产物

- 实际corpus：`C:/YOKI/Codex/AssetLibrary-worktrees/V03-036/.runtime/corpus`，120文件/5,183,400B，manifest SHA256 `eededaeab8163866abad4d3265475827aa6dd9ca76e43a7f878c7f264c314048`。与原138合计258，图片样例目录120项。
- 完整Release：`tests/dotnet/AssetLibrary.WebGateway.Tests/bin/Release/net10.0/AssetLibrary.WebGateway.Tests.dll`，SHA256 `4a048a5746ccab669e317b0b2f6ed3538be68c009cdaff5afca606e8f42376bb`。
- 现有依赖Host同轮构建：`services/core-server/Host/bin/Release/net10.0/AssetLibrary.CoreServer.Host.dll`，SHA256 `ae13e4b3a6c78dbc30e4205d754c5e8ae65c298fe4d38ad12460bd3ea59f8371`；EXE/deps/runtimeconfig、CoreServer、AssetLink、Npgsql、WindowsServices/ServiceController均存在。可从本worktree的serve.py用--no-build运行，不需重建旧夹具。
- Root指定已验图片worker：`C:/YOKI/Codex/AssetLibrary-worktrees/V03-005/.runtime/gallery-server-20260913/worker/AssetLibrary.ImagePreview.Worker.exe`，旁libSkiaSharp.dll需保持完整（12,274,488B）；不要选bin/native裸EXE。

## 验证与边界

Python13/13、C#NativeClientImageFixtureTests 9/9、locked restore、严格Release build（0警告/错误）、定向format、verify_repository通过。128/129、32MiB+1、64MiB精确/加1、零长度、路径越界/hash变化、生成器120真实字节/覆盖拒绝、原138与原件mtime均覆盖。初次构建与format被MSTest要求专用比较断言拦下，已修并保留日志；未抑制分析器。所有锁/产品/版本/契约不变。

仅修改测试适配层和任务交接；复用既有生产Core fixture、staging/清理与合成输入，不复制权限/分页/预览业务。输入最多128与64MiB、每次缓冲64KiB，时间O(输入字节+文件数)，不把50万资产或G4性能门禁纳入本夹具。失败保留源数据，部分副本只在测试runtime由既有生命周期清理。

Root合afdc0df再合交接提交；实际Core启动、120图片跨页、图片解码、Windows展示/注销/原件与服务清理由root统一验收。生成/组件测试通过不代表上述实机通过；无新增技术债或产品开关。
