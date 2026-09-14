# Contract Change Proposal：TS062 服务只读身份

状态：proposed，未批准、未发布、未实现。请求 GatewayAuth / LibraryStorage / AssetLink 单一负责人及平台消费者共同裁决，协调者给出精确源码范围后才实现。

问题：现有 Subject + authorized read 查询可以复用，但入口为浏览器 Session，缺少服务 token 生命周期与库权限管理公开用例。dialogue origin 不携带 AssetLibrary 库授权。

建议批准以下不可分割的最小切片：

1. GatewayAuth 新内部 ServiceReadCredential 合同：签发、验证、撤销、轮换；验证输出受信 AuthenticatedSubject 或明确拒绝/不可用，不把 token/ACL放入现有 read DTO。主体必须独立且非管理员，摘要/到期/撤销持久化；每次请求在线验证。
2. LibraryStorage 新公开只读权限管理用例，受信操作者只可向现有 principal/library 赋 read_only 或撤销；模块拥有数据库写函数，GatewayAuth 仅通过公开端口调用。保留既有 permission 模型，不设计总权限系统。签发协调失败不产生带权限的成功结果。
3. AssetLink 认证附录：原 control 路径增加严格的 HTTPS Bearer 服务分支；拒绝 Cookie/Origin/Sec-Fetch 混用与多个 Authorization；只允许五个只读操作。默认关闭、单独配置，不改变浏览器登录与 CSRF。现有 schema、请求 body、响应和错误码不变，认证前 unknown 关联语义明确记载。
4. 新持久凭据表与受控管理函数需要迁移，沿用现有 PostgreSQL与模块角色。迁移负责人安排编号、最小 EXECUTE 授权、回滚/恢复验证；不允许平台数据库访问或部署时临时 SQL 灌权限代替用例。

合成示例（描述场景，不是可调用的新 wire schema）：操作者为 `service:tianshu-assets-test-a` 创建非管理员主体，授予合成A库 read_only，生成一次性可见 token。平台后台用此 token 请求现有 libraries.list，仅得到A。删除该库权限后，以相同 token 发起的新查询不得返回A；撤销 token 后认证401，存储失联503。subject/library 权限不能由请求 body 自报。

兼容：五操作及生成 SDK不变；浏览器原路径回归必须通过；新增管理命令只有本地受信操作者，不公开远程通用授权API。凭据必须有上限有效期（建议24小时），轮换与撤销独立；配置上限由负责人批准。无长期离线 token 容错。

拒绝的捷径：管理员Cookie、平台origin兑换权限、在token中缓存库ACL、GatewayAuth跨模块写表、重建权限数据库、直接开放现有全部control调度器。

验收与风险见 [service-read-plan.md](service-read-plan.md)。准入要求真实平台/真实Core联合HTTPS、撤权及重启验证，不能以现有浏览器HTTP或fake query测试关闭。批准本提案也不批准生产部署、真实NAS或文件操作。

待裁决记录：负责人须填写批准提交、精确Host/CLI挂载文件、迁移所有者/编号、有效期与消费者预算。本文给出可审查方案，本任务到交接即结束，不把待批准实现伪报完成。
