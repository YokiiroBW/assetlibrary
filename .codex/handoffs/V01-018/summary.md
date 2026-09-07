# V01-018 — Web只读试用流程交接

状态：ready_for_review。实现 commit：`d5ac7f69df48f35cdf4db48ad1d2dd91da42ed50`；branch：`codex/v01-018-web-read-only-trial-workflow`；worktree：`C:\YOKI\Codex\AssetLibrary-worktrees\V01-018`。

## 完成内容

- 初次登录、登录失败/限流、会话探测、绝对到期、重新认证、退出和退出失败后重试。认证服务暂不可用不冒充退出；明确401/403/404清除对应旧数据。
- CSRF只保留在会话内存，所有control请求携带固定header。原生BroadcastChannel只发送会话变更提示，focus重新核验；主体、CSRF或管理员状态变化重建工作区，清除旧库、搜索、选择、详情和游标。
- 管理员从最多32个部署存储源中选择，文本输入真实服务器目录；登记完成只选中新库，不自动扫描。不提供任意目录枚举接口，普通用户不显示管理入口。
- 首次扫描真实状态/计数、独立取消、未完成后重试、页面刷新后重新读取任务。未确认的提交重用request/idempotency UUID；已成功的初始快照不提供伪重扫。
- 活动扫描2秒轮询，暂时失败5秒重试，隐藏页停止、切库/退出中止旧请求；源离线保留上次索引，可刷新或由服务端重新探测后重试。未扫描、失败及取消不会显示为成功空目录。
- 保留现有虚拟浏览、搜索、5秒完整响应截止、取消与畸形HTTP拒绝回归。登记弹窗支持原生模态、首尾Tab循环、Escape和焦点返回；登录允许密码管理器和粘贴，390/1440布局及深浅色有效。

## 模块边界与复用

只修改Web入口、其浏览器测试和本任务记录。复用冻结生成SDK、AssetLinkClient、queryState、分页hooks、VirtualEntryList和现有CSS令牌；从旧App拆出ReadOnlyWorkspace与会话/登记/扫描组件。仅一个集中fetch调用，所有I/O都使用同一有界传输。没有复制服务端的权限、路径规范化、重叠检查、任务状态机或存储规则。

消费已批准的ADR-0014和read-only-trial-v1应用合同；不改变canonical envelope、SDK、服务端、SQL、根依赖、版本、权限策略或发布门禁。无新语言、框架、运行时、依赖、公共契约或迁移；Web role/can_cancel/can_retry只影响展示。

## 验证与影响

最终73项自动测试通过：39 Chromium（原22项+新增17项）、14架构、6Web仓库、14SDK生成/政策。格式、lint、类型、生产构建、真实架构扫描130 inputs、源码/生成/依赖许可/预算与漏洞审计通过。详见tests.md。

生产产物仍在原预算：JS 250286 bytes，CSS 11507 bytes，总261793 bytes。新会话/扫描状态为O(1)，存储源上限32；列表仍按100项分页且窗口化，不新增50万资产全量载入或精确计数请求。本证据不包含真实NAS/50万P95验收。

测试只在本worktree的开发服务器/fixture和系统临时测试目录运行；不访问真实资产、数据库、凭据、TLS信任、注册表或系统服务。原文件权限与磁盘状态机仍由核心负责。

## 联调入口与剩余边界

`apps/web/dist`是实际构建产物，可交由同源HTTPS Host静态托管。开发和fixture命令见apps/web/README.md；没有产品认证绕过开关。

所有auth响应按snake_case JSON，登录200、session200/401、退出204。control遵循冻结operation/body，注册成功只返回library_id。扫描get仅管理员查询；已登记实例在library列表可见，浏览响应保留现有availability字段。

建议在V01-016认证及V01-017库/扫描提交之后集成本分支。V01-015仍须完成真实HTTPS+PostgreSQL+隔离物理目录的端到端与发行包验收；本任务的路由fixture成功不替代该证据，不声明试用里程碑或完整Alpha发布完成。没有为本Web组件留未实现TODO或引入额外技术债。
