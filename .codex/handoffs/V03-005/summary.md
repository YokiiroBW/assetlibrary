# V03-005 — 并行浏览与预览协调

状态 **partial / 统一集成进行中**。用户要求的四个独立窗口已实际开发；Web、Android和Windows诊断已合入协调分支 `codex/v03-005-parallel-browse-preview-integration`，当前检查点3890793。没有宣称整批、Windows入口或完整版本完成。

## 已冻结与已集成

- 38aedca冻结 `contracts/assetlink/image-preview-v1.md`、ADR0019与独立decoder的SkiaSharp4.151.2精确版本；既有生成器更新SDK指纹，无手改生成执行代码。共享端口/Host/读取broker的细项授权见decisions.md。
- Android实现1838b96/交接815e2f6已集成。手机/平板Compose图片预览、2并发、可见项/内存边界、20秒总期限和身份/前后台清理已交付可审查代码，原窗口77项不同测试通过；真实Core图片用例尚未执行。
- Web实现4e633e8、拒权补充d944300、准备238c61c与交接d11085e已集成。原窗口65项不同浏览器用例通过，最终302081B JS/27768B CSS在批准预算内；CSP和真实Core图片联调尚未执行。
- Windows453e10b/ba9fcbb/046dbde诊断及证据已集成。旧DLL在隔离加载矩阵4项通过；实际Explorer入口未完成。Session2的WTSDisconnected解释当前UI无法操作，不能作为历史加载失败根因。
- 5edf25c扩展既有真实Core/PostgreSQL/HTTPS测试入口：仅显式开启10项合成图片和真实已发布worker；默认138文件与连接JSON不变，无decoder/隔离测试绕过。Python8、.NET6及locked restore/format/Release通过。

协调已审查请求、取消、身份代际、图片内存、平台生命周期与既有调用方，实看Android手机缩略/平板深色预览和Web桌面/200%文字截图，合并无冲突。消费者源码与各自验收提交一致；未重复运行未改动的客户端套件。各任务准确范围、测试/失败修正和截图保留在自身handoff。

## 当前工作和边界

V03-007专注Core有界源broker、缓存/lease与精确HTTP接口，本地真实Core/PG的权限/禁用语义已取得阶段证据，待稳定提交。Windows隔离收尾已按57d1578/ca1d235精确转交V03-006单写，保留原Explorer目标。Linux默认worker40d2d69已在开发机完成10图片+4隔离/资源+1父退出共15实际场景，完整notices/锁/产物hash可追溯；没有GC环境覆盖或Docker外层替代。此前Console fd、VM超限与内存配置失败/修正完整记录在linux-decoder-progress/default.json。开发机内核6.14不能代替NAS5.10.55。

Web独立真实Core浏览器runner及7项输入检查已准备并集成；Android真实Core图片仪器用例已编译，二者等待同一私密连接文件，未执行不记通过。自有模拟器关闭、无转发残留。root已备好仅复用缓存的Linux Core工具镜像（Python3.13.15/.NET10.0.111/PG16.15）和核hash的Web dist。Core接线提交后在独立临时源码启动真实服务，保持localhost身份供两端共用，最终核验源hash/mtime、进程/端口/账号/私密目录清理，不触碰NAS生产。

Windows需要用户恢复原RDP Session2并保持解锁，之后仅在新鲜窗口所有权和实际观测基础上继续一次原生入口验证；不改系统策略、重启用户Explorer或用独立客户端替代。当前无Windows安装包。生产Shell和全部既有平台/Provider/写入/版本门禁保持开放，现有NAS和原Android包保留。

实际线程、分支、目录见windows.json与注册表，所有写实现仍在自己的worktree；共享契约、根依赖/CI、最终集成及部署由root单写。复用这些窗口，从上述未完成项接续，不重复初始化或将等待联调记录成开发已完成。
