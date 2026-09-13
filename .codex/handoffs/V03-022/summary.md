# V03-022 — Explorer 大图与浏览偏好交付

状态ready_for_review；V03-023/024/025已并行集成并完成统一验收，主协调任务已完成。安装版0.3.0-preview.7；运行时代码源码addcaf205aa88af19d858eeae9770bb2f5c9b1ef。完整证据见windows-preview7-delivery/README.md与evidence.json。并未宣布完整V0.3完成。

## 交付

原生Explorer右侧Space/Enter/双击打开最长边1600的大图，当前页左右/按钮切图，Esc恢复原选择和浏览位置；不增加导航历史。图库/列表及密度跨目录、分页、新进程记忆。全局应用默认只含mode+density；不按临时PIDL存路径/身份或保存预览状态。

Root负责ADR-0022/preview-v1、View/Requests/C++协议、偏好接入、版本/锁/打包与实机；Surface、Host和偏好组件由三个隔离worktree分工提交。Root审查修复了偏好恢复callout销毁View后继续解引用的问题，并用负控覆盖；thumbnail仍保留精确像素分配，大图独占搬移wire缓冲，避免改变16张最大thumbnail预算。

## 复用和边界

复用Core image-preview-v1的preview端点、原会话/权限/opaque epoch-node映射、相同HTTP许可、固定系统PNG的隔离WIC helper、原生Surface/GDI/UIA模型和退休调度、共享两I/O线程/64任务/每视图16未完成/16MiB像素预算。无数据库、服务端业务、依赖、第二框架或原件写入。大图查看只留一张并清缩略图；临时wire/DIB单独有界，未混称为16MiB持久预算。页内查找O(101)，不扫描50万全库。

新增独立ALP1管道固定1600，ALG1/512边界不变；同SID/session/peer限制、取消/超时和失败无像素继续生效。Host两个图片端点共4客户、图片HTTP共1许可，保留目录响应能力。原helper128MiB/3s/单进程约束下，最大12MiB PNG/1600平方测试父校验加解码573ms；没有把硬限额误报为实测峰值。

偏好为HKCU64 Software\AssetLibrary\ExplorerPreferences/BrowseState，固定16B，坏值整体回默认且不自动覆盖；写失败显示非阻塞摘要。此兄弟键不进入Setup WindowsClient树，升级/卸载保留。测试用fake或独立易失GUID键，不写真实偏好；实机操作按用户意图保存Gallery/200。

## 统一验收

最终包542文件大小/强哈希匹配；新Explorer28748加载正确DLL，17596验证模式/密度跨进程。实际JPG/PNG/WebP、方向修正、透明图、失败清旧图、切图、宽窄、原选择恢复、Space、logout清空均留证。148合成原件hash/mtime不变，6角色/临时Core与PostgreSQL/HTTPS/连接文件/Host/Settings/5自有窗口已清理。原Explorer6212创建时间未变，安装保留。

Windows120、Setup23、Shell13、Gallery12、包装6、锁16、最终CLI8和3个NativeLive方法证据通过；详见tests.md。最初真实测试三类并行登录触发2并发无排队限制，保留失败TRX，修正测试调度/共享登录后图片方法通过；未改安全配置。G4按用户豁免不执行，原G1/G2/G3沿用。

## 风险与后续

NAS图片引擎隔离、缩放/平移、原件编辑、同步/时间轴/全库连续滚动、签名、认证/系统卸载UI、Android真机仍独立；Windows11旧底部计数以自有摘要为准；旧preview.1占用不强删。没有活跃测试夹具或持续后台任务。下一步根据用户选定功能继续，勿重新安排G4。

模块代码顺序与协调提交见各V03-023/024/025交接；共享契约c10eddf，协议9b7fe8c，View f6dd888，发行addcaf2。本任务对main采用最终统一fast-forward，不推远端。
