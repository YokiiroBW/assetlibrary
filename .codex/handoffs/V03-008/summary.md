# V03-008 — Web 服务端缩略图与基础图片预览

实现提交 **4e633e8 + d944300**；分支 `codex/v03-008-web-image-preview`，独立工作区 `C:/Users/Administrator/.codex/worktrees/a25c/AssetLibrary`。状态 **ready_for_review**：客户端实现/构建及65个不同浏览器用例通过；根级产物预算已由协调23ad33d修正并定向复验通过。真实后端联调由V03-005统一执行，未部署NAS。

## 已实现的用户行为

真实目录列表/网格仅为可见普通文件读取服务器派生缩略图；单击保留信息，双击/Enter进入图片预览，Space快速查看不改变URL/history。Escape、Tab/Shift+Tab焦点循环、前进/后退及回到条目焦点均已验证。预览保持图片比例、透明、长文件名、手机/桌面、浅深色与200%文字可操作。

图片格式由服务端内容识别，前端不以扩展名断言格式，也不取原图。目录/重解析项不取图片；图片服务离线/未启用、不支持、损坏、超限、源变化和失败保留明确L0信息与安全提示。服务端GET即使404也不尝试其他原图端点。PNG代理最长边1600，不宣称原图色彩/分辨率无损。

## 复用、安全和资源边界

消费协调冻结的 `contracts/assetlink/image-preview-v1.md` / ADR-0019；38aedca在本分支cherry-pick为3f3cfba。该共享基线的契约、SDK来源摘要及Skia中央pins均由协调单写；本窗口仅修改Web、Web测试和自身交接，没有手改共享文件。

复用现有React/TypeScript、生成SDK、同源Cookie/内存CSRF、统一fetch、JSON5秒总期限、分页/虚拟化、History、Modal和CSS语义颜色。图片单独使用20秒期限、2并发、47缩略图及1预览租约；排队/重试/正文/解码共享总期限，入队与启动时都检查过期。仅429最多2次按Retry-After重试，其余不自动循环。

有界流读取校验Content-Length/MIME、PNG签名/块/动画标记/8位/尺寸/像素，再核对浏览器解码结果。活动Blob预算32MiB、解码像素1200万；离屏/关闭/导航/身份变化释放URL。失焦/后台/pagehide同步卸下画面并释放租约，重新前台会话核验后重新取图，不用旧缓存跳过服务器授权与源核对。401清空会话，403/404清该库派生图片并核对库权限，显式刷新解除失败状态。没有持久图片缓存、原资产写操作或新增前端依赖。业务权限、路径与源一致性仍由服务端负责。

新图片工作与总库规模无关：每页仍100条，浏览器只请求当前可见项，队列/扫描集合至多48项；不拉全库、不预生成全库缩略图。浏览器自身解码临时副本/进程峰值不等同活动像素预算，50万资产或浏览器长时内存压力未在本窗口宣称通过。

## 验证与证据边界

[测试记录](tests.md)与[产物证据](build-evidence.json)。22个新增图片回归和既有43个Web回归全部各自有通过证据；不同阶段的重复用例不重复求和。最终格式、TS编译/生产构建、Web源码边界通过；SDK生成/源码/依赖及架构检查通过。verify_repository曾在旧Web预算处中止，该失败已通过协调修正及原检查定向复验关闭；没有再次执行未变的全部聚合检查或浏览器用例。最终调用方审查补齐L0详情403/404也清空缩略图的共享清理路径，d944300的新增例与原图片拒权共4/4通过。

最终JS302081 B（gzip约93.64kB）、CSS27768 B（gzip约6.28kB）。V01-024不可变证据的既有290457/25942 B已超过旧阈值，本轮最终增量11624/1826 B且无新依赖。协调23ad33d将policy限值改为JS327680/CSS32768/合计360448，本分支合为f6fecae；`validate_web_dependencies.py --require-build-artifacts`通过。原产物检查未跳过，未通过改变产物规避预算。

已查看合成样例截图：[桌面浅色](screenshots/preview-desktop.png)、[手机深色](screenshots/preview-mobile-dark.png)、[手机200%文字](screenshots/preview-mobile-text200.png)。它们是Playwright fixture，不是NAS或真实Core验收。自有临时HTTP/Vite/Chromium已结束，4173无监听；未读个人账号/资产或改NAS。

## 合并与后续

真实Core浏览器验收入口已另在9bf074e准备：`tests/web/real-core-image-preview.mjs`及说明/支持模块/4项输入检查，严格在tests/web范围内。复用5edf25c共享fixture的私密connection.json和十项corpus，不改共享fixture、不设置route、不混入默认Playwright自测。先校验localhost/叶指纹/有效期，再使用特定SPKI启动未绕过CSP的Chromium，匹配真实Host上的审查产物；原生测试账号登录/发现entry均走生产API。覆盖真实派生PNG/方向/透明/中文.dat、错误L0、截图和普通账号404/匿名401。脚本/格式/4项输入边界通过，默认仍收集65项原自测；**真实服务运行待root READY，没有真实联调通过声明**。无需重复65项未变用例。

先合root38aedca与23ad33d，准备文档238c61c可合，再合本实现4e633e8、拒权补充d944300及最终交接提交。不要重复合3f3cfba/f6fecae（对应同一协调基线）。由主协调统一真实Core/PG/HTTPS与跨端合成资产验收、包来源和部署。Provider、文件写入、Explorer、完整Alpha/V0.3门禁均保持独立。
