# V03-024 交接摘要

状态：ready_for_review。分支 `codex/v03-024-large-preview-host`，功能提交 `629e1a099b46e75886417cbb05b7c70e9ac9f804`，最终实现/测试修正提交 `85a13a4c8e4765cddbd28530a5fa3de7a20b2067`，基线 `c10eddf`。仅本任务Host/Core/测试/README与交接文件；无版本、依赖锁、Setup、共享契约、服务端或原件修改。

## 完成内容与接口

- `ClientTransport.PreviewAsync(library, entry, cancellationToken)` 复用现有认证HTTP会话和 `variant=preview`。
- `UserSessionCoordinator.ReadPreviewAsync`、`PreviewPipeServer` 和 `PreviewProtocol` 提供 ALP1 固定1600投影。C#继续复用 `ThumbnailRequest/Response/Pixels/Status` 数据结构；通道入口决定封闭规格。
- `DerivedImageProfile` 只有 Thumbnail512 / Preview1600；私有构造的规格表分别固定2MiB/512与12MiB/1600，调用者不能提供自定义限制。旧ALG1与失败epoch语义保持不变，ALP1所有状态精确回显请求epoch/node。
- `ThumbnailSession` 共享单处理许可和4任务容量；生产两管道使用同一个 `ImageClientCapacity`，合计4个已接纳客户。多出的物理连接立即关闭，不排队、不消耗HTTP；各管道的4个安全监听实例不等于8个已接纳客户。
- `--decode-preview` 复用固定系统PNG/WIC、匿名句柄白名单、清空环境和原128MiB/1进程/3秒Job。旧 `--decode-thumbnail` 仍拒绝大图。不缓存完成图片，不把URL/路径/凭据送入helper或Shell。
- Windows README纠正旧Proof时代说明，引用preview.6权威发布记录；没有声称本轮大图界面或preview.7整包已经实机验收。

## 复用、边界与性能

Host只消费Core已有授权页节点映射和图片端点；没有复制权限、路径、Provider或服务端业务。JSON仍8秒/1MiB；两图通道共享1个HTTP图片许可、HTTP总2，导航保留容量不变。20秒总图片预算包含排队、HTTP、最多2次429重试及解码，首帧500ms，最终排空500ms。客户端断开、会话注销或epoch撤销会取消任务及发布；401/403撤销快照，404保持诚实的可选预览占位。

50万资产下仍只查询当前授权页（最多100普通项及分页入口），图片处理只与当前4个有界请求有关；无全库扫描、图像索引或Host完成缓存。Raw上限10,240,000字节；Host每个已完成但尚未送出的响应最多保留一份像素及其帧副本，客户总4，解码同时1个。Explorer进程内只有经校验像素，仍无网络/解码。

## 验证与风险

最终Windows solution格式/Release构建通过，120个离线模块测试通过；其中43个图片相关用例。依赖审计6项目/31锁定包通过，源码532文件及仓库架构门禁通过。生产Host WinExe配置独立构建通过。最终diff已检查调用方、共享配额、失败身份、取消和旧协议兼容。

最大1600×1600、精确12,582,912字节编码PNG在全新helper内成功，最终父校验加解码573ms；整个测试1.2077539秒（含生成）。128MiB/3秒限制未提高，未采集Job峰值计数；见decode-budget.json。初次972ms指整个测试，不是纯解码。源图均为测试动态生成，不读取真实资产。

真实Core NativeLive已扩为原夹具的一次登录双profile用例，由root在统一整合后执行；本任务没有启动服务、执行GUI、生产pipe或真实NAS。G4用户豁免未测。本机样例成功不意味着NAS预览能力已恢复。没有新语言、框架、包或技术债；保留旧Thumbnail命名的数据类型是显式兼容复用。

## 建议合并顺序

先保留root冻结的ADR-0022/preview-v1与独立向量提交 `9b7fe8c`，再合入实现提交及本交接，随后C++消费者与root统一NativeLive/安装包/Explorer验收。共享preview-vectors仅从root只读复制作本地验证，未纳入本任务提交。changed_files由相对基线的 `git diff --name-only -z` 生成并包含本交接。

## NativeLive调度修正（85a13a4）

Root首次联调为3通过/1失败；Preview1600真实链已通过，Thumbnail512在SignIn收到429，未进入图片管道。实际 `TrialLoginLimiter` 为全服务20次/分钟、并发登录2、排队0；`AssemblyInfo.cs` 按ClassLevel并行，三个NativeLive类同时登录导致第三个被拒。不是图片许可或解码失败，也不能从此推断20次/分钟已用尽。

修正仅为图片NativeLive方法加 `[DoNotParallelize]`，并把两规格放在同一fixture/一次登录内顺序验证，两张样例、两种通道和每次请求关闭仍保留。没有修改Core限流或盲目重试。修正后格式、构建与源码检查通过；Root仅重验此图片NativeLive，其他已通过用例不重复。离线120的源和行为未变，不重复运行。
