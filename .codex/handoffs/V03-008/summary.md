# V03-008 — Web 服务端缩略图与基础图片预览

状态 **ready_for_review**。产品提交4e633e8、d944300、77271e0；最后一项修复真实联调发现的已挂载行键盘焦点不同步。分支`codex/v03-008-web-image-preview`，独立工作区`C:/Users/Administrator/.codex/worktrees/a25c/AssetLibrary`。66个不同浏览器回归、7个工具输入检查通过；真实Core客户端验收由同一fixture的16项图片/错误证据和独立交互收尾共同完成，见[两阶段索引](real-core/acceptance-index.json)。未部署NAS。

## 用户行为与边界

可见目录列表/网格读取真实服务端派生缩略图；单击保留信息，双击/Enter打开图片，Space快速查看不改历史，Escape关闭并返回焦点。真实Core验证JPEG/PNG/WebP、EXIF6方向、透明、中文路径下同内容.dat、SVG/非图片/截断/超限安全L0。图片保持比例；手机暗色、桌面及200%文字可用。没有原图下载、编辑、写回或扩展名判定真实格式。

复用既有React/TypeScript/生成SDK、唯一fetch、Cookie/内存CSRF、原JSON5秒期限、分页/虚拟化、History/Modal和CSS语义颜色。图片20秒涵盖排队、最多2次429重试、正文和解码；并发2，47缩略图+1预览，活动Blob32MiB、解码1200万像素。PNG/MIME/Content-Length/块/尺寸/8位/浏览器解码均校验。离屏/关闭/切导航/身份/后台释放Object URL；pagehide同步卸下图片，前台核验后重新请求。401清会话、403/404（含L0详情）清该库派生内容。核心权限、路径、源身份和缓存仍由服务端负责，没有新前端依赖或业务复制。

50万资产下新图片工作仅与可见范围有关，调度扫描最多48项，每页仍100条；没有全库取图。活动预算不等同浏览器RSS，未宣称50万压力或长时进程内存验证。

## 验证、真实来源和失败保留

[tests.md](tests.md)保留每轮失败、诊断和修正；[build-evidence.json](build-evidence.json)固定产品77271e0。最终JS302093 B/SHA256 b248f3ac02a29f82a795bf4716ca5604f67c5e556089cf2bc08810ce192866ea，CSS27768 B/SHA256 c4b01b9d4517d1e554e5508b0764981a6fe45d30cf622f5fdf195c52a4bdf4a1。格式、TS/build、Web源、架构及协调23ad33d预算门禁通过；未为预算跳过检查。初期verify_repository在旧预算中止的记录保留，失败由原检查定向复验关闭。

真实服务为root管理的Linux Core874fb6a、worker40d2d69、PG和148合成文件，localhost同端口转发；只读TLS先核精确叶SHA、localhost与有效期，浏览器仅使用该SPKI且不忽略全部HTTPS错误/不绕过CSP。实际HTTP JS/CSS哈希与77271e0完全一致。

第一份最终产品receipt完成16项图片/错误检查，但顶层在QuickLook定位处failed，**原文件不改写**。后续interaction_continuation重新核对同一TLS/Webhash/发现，实际Space打开、Escape关闭、历史与原行焦点返回、退出URL清空、普通账号已知ID404/匿名401、CSP/无脚本/无跨源通过，case数组明确为空。合并索引绑定两份原SHA并分别描述范围，不把续验冒充重跑16项。

此前执行环境切换导致首轮无receipt、转发断开导致TLS前置失败，不计产品结论。后续明确修正了runner等待取消Response.finished、浏览器headers属性访问、虚拟列表定位方式；真实键盘焦点问题另以会失败的最小产品回归复现后修复，未用测试放宽掩盖。

真实截图：[桌面](real-core/real-preview-desktop.png)、[手机透明图](real-core/real-preview-mobile-alpha.png)、[200%文字](real-core/real-preview-mobile-text200.png)、[无权限账号](real-core/real-invisible-identity.png)；均为生产Host返回的合成输入，已查看。旧screenshots目录是明确的mock证据。

## 所有权与清理

共享契约38aedca、根预算23ad33d、Core/Worker/fixture均由root单写，本窗口只改Web/tests/web及自身交接。实际权限表/资产内容未由本窗口修改，只用正常公开登录/退出API。root在同一fixture中独立保留旧Webdist备份后切换审查产物；没有NAS更新。

root显式stop完成148原hash/mtime不变、6LOGIN/runtime/Host/PG/HTTPS/容器清理。已只读核对其公开real-core-cleanup.json，SHA751cc658fef1eb608d247f4842be68b359cdf9c10f344b00693ae0b8e2aaa04b；最终索引引用此canonical receipt并标coordinator_verified。本任务SSH20820经PID、创建时间和可执行路径核对后关闭，44147无监听，见[forward-cleanup.json](real-core/forward-cleanup.json)。所有本任务浏览器已关闭，私密connection由root清理，没有复制到交接。

交付顺序以root已合入提交为准：消费者主实现→d944300拒权补充→77271e0键盘修复/产物证据→真实runner修正与最终交接。不重复合root已有共享契约/预算。Provider、Explorer、写入及完整Alpha/V0.3门禁独立，不宣告里程碑完成。
