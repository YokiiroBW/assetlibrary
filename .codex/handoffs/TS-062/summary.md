# TS-062 交接

2026-09-14；`work/ts-062`；基线 `555f337271c3b8690d96a4a5a72d796d1c0a7536`。状态 ready_for_review，仅方案与验证交付。最终提交由协调回报中的精确HEAD定位（此文件不自引用提交哈希）。

已交付 [最小实现包](../../../docs/integrations/tianshu/service-read-plan.md)、[CCP](../../../docs/integrations/tianshu/CCP-TS062-service-read.md)、可执行公开端口验证驱动和脱敏回执。后续至少需要 GatewayAuth 凭据生命周期、LibraryStorage 受控赋权端口、Host 服务认证分流与持久迁移；不是只添加Bearer即可完成。源代码/正式合同/迁移/锁均未改。

复用现有主体、库权限表、ReadOnlyBrowseService、IAuthorizedReadModelQuery、五个AssetLink只读操作，避免第二权限模型。token认证和库权限每请求核验；明确在途事务、分页游标、5秒Host预算、离线旧页不展示、撤权/错误/重连以及平台用户绑定边界。已读 Platform a94d345 的公开文档，未读写其数据库或未提交实现。

验证：仓库准备/架构门禁通过；精确SDK locked restore和相关Release构建通过（0警告/0错误）；8个公开端口测试通过，无失败/跳过。TS060输入未变，其真实HTTPS/清理及64项计划TRX记录沿用，未重跑重型测试。本轮查询和principal使用明确测试替身，不是服务身份或平台联合验收。详见 [tests](tests.md)、[port-evidence](port-evidence.json)。

文件安全：本轮未启动PG、HTTP服务、扫描或真实设备，无资产I/O；仅本任务构建缓存与TRX在.runtime。没有可复用的任务数据库/会话/凭据。首次SDK运行自行生成开发证书，未执行trust；不作为服务证书或生产证据。

下一步由协调者裁决 CCP、Host/CLI精确范围和迁移所有权，再创建内部实现任务；随后平台消费者独立实现，最后做真实两产品联合验收。推荐先集成本方案文档，再串行合同/迁移裁决与实现，未推送/合并/改根板。

仍未完成：服务身份HTTP、真实平台消费者、实时服务凭据/库撤权与重启联合验证、50万规模、B查重/分类、C物理整理/删除/生产发布、真实NAS。能力继续 unavailable，完整Alpha/L0均无完成声明。无新增产品技术债；这些是明确后续工作。
