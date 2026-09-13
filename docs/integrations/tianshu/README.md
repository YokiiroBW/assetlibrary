# TS-060 资产集成候选

2026-09-14；AssetLibrary 基线 `99e79f2f5a6d3b84ff2faaa9640752569796eb69`；平台公开文档基线 `a5ee59ff67a2de7a7e0d8328ea3c0f43e1d6a209`。状态：**候选与隔离验证，未冻结、未接入平台、未开放生产写入**。

本次只写任务分配的文档和测试目录。根协调契约的唯一发布者仍为协调者；下列 schema 和实例不可直接加入能力注册表。AssetLibrary 内部 AssetLink、权限、错误码、数据库及发布门禁的所有者不变。

## 实际能力盘点

| 能力 | 本基线真实接点 | 可复用范围 / 缺口 |
| --- | --- | --- |
| 库与已索引文件发现 | `POST /assetlink/v1/control`：`libraries.list/get`、`entries.browse/get` | 当前授权查询；稳定 `library_id/entry_id`、相对路径、大小字符串、mtime、库在线状态。详情读索引，不证明文件此刻存在；没有完整哈希或内容版本回执。 |
| 搜索 | 同一 control 的 `assets.search` | 授权范围内名称/路径搜索；非语义分类、身份匹配或查重。默认最大100条，游标绑定查询/库/目录/排序。 |
| 首次扫描 | `library_scans.start/get/cancel`；公开 `IInitialScanCoordinator` | 管理员显式启动，源由部署白名单登记；真实只读 Worker。成功扫描之后再次 initial start 返回 `already_indexed`，不是通用重扫。原件不变，但任务和索引由 AssetLibrary 写入。 |
| 库分类 | `libraries.update_category` | 管理员显式分类与 CAS；**不等于文件内容分类**，不作为本集成的自动整理入口。 |
| 图片派生预览 | `GET /assetlink/v1/libraries/{library_id}/entries/{entry_id}/image` | 已有授权与受限派生协议；需要实际可用的隔离解码器。本任务没有启动解码器，不声称图片分析验收。 |
| 整理计划 | `OperationPlanService.PrepareAsync` | 真实应用编排，绑定预检、确认摘要和有效期；没有生产 HTTP 计划入口。 `IOperationPlanStore/Preflight/Executor` 目前物理操作实现位于测试沙箱，不能由平台直接实例化来接管文件。 |
| 最终发布 | `OperationPlanService.ExecuteAsync`、`TransferService` 的现有应用语义 | 可验证沙箱 copy/move/rename/trash/restore；生产文件执行、持久恢复与跨产品身份授权尚未接入，不代表下载归档链路。 |
| 精确查重 / 同源 / 分类 / 优选 | 无可用业务接口 | 强哈希和图像输入哈希不是查重服务；`exact-dedup` 发布能力仍缺失。无应用生成的候选报告，也没有自动挑选后删除的授权。 |
| MCP / Developer API | `integrations/mcp/README.md` 为预留 | 不生成可调用工具描述，不猜测路由。AssetLink 总合同中的 handshake/events/upload/download 等也不因 schema 存在就视作本 Host 已开放。 |
| 天枢平台接点 | TS-012 的 `projections.project/tasks` 内部应用端口 | 平台只有来源解析和模型配置快照两条 HTTP；资产能力、整理命令、下载器均未接。内部投影字段不是已发布跨产品协议。 |

代码依据：`services/core-server/Adapters/AssetLink/AssetLinkReadBodyParser.cs`、`ReadOnlyAssetLinkProtocol.cs`，`Host/Trial/TrialManagementGateway.cs`、`TrialLibraryManagementControls.cs`、`TrialScanComposition.cs`，`Modules/OperationTrash/Application/OperationPlanService.cs`、`Contracts/OperationContracts.cs`，`tests/dotnet/AssetLibrary.TransferOperation.Tests/SandboxOperationComposition.cs`。基线本身存在历史文档进度差异，以上以调用链和门禁输出为准。

## 最小接入与职责

1. 第一切片复用当前 AssetLink 的授权只读列表、详情和范围搜索；不新增平行的资产读 API。服务调用身份尚无接入合同，浏览器 Cookie/CSRF 会话不能被默认为平台服务凭据。平台服务身份必须映射到 AssetLibrary 当前库权限，由双方所有者决定。
2. 文件发现只接受 AssetLibrary 已登记库/目录范围。平台不能扫描绝对路径、挂载 NAS、读取资产数据库或根据文件名合并身份。不可见库返回同一404，离线保留明确标旧的最后投影；权限失效清除旧结果，不能以缓存绕过。
3. 只新增一个待议的候选报告形状，见 [候选语义](candidate.md) 与 [schema](candidate.schema.json)。它表达查重/分类/优选建议和不可用原因，不授予计划执行权。已有文件组、动画和 sidecar 全部保留。
4. 整理目标由平台提出，AssetLibrary 解析位置引用、预检并返回自己的计划 ID/确认摘要；确认后仍由 AssetLibrary 再核验权限、保护、源事实、冲突、空间、锁和有效期。平台不持有 `OperationExecutionRight`，不签发存储令牌、不实现另一个文件执行器。
5. 下载器只能写已授权暂存区；AssetLibrary 才最终发布受管文件并记录强校验。下载完成、发布完成、资产索引、外部阅读器索引分别记录。优选不删除，重复事件不重复发布；确认超时/断连是结果未知，先查执行方状态，不能新键重试。生产发布尚未实现时返回不可用，不制造成功回执。

未来平台投影可使用 AssetLibrary 作为 owner，保留 `job_id/version/event_cursor/observed_at` 和任务阶段；对象版本、订阅游标、接收时间各有独立含义。单一 `phase` 不能合并下载、归档和各索引阶段；取消转回实际执行方，其接收回执与最终取消分开。正式投影字段和取消合同仍待双方审查。

## 验证与交接

[测试入口](../../../tests/integration/tianshu/README.md)记录真实命令、替身边界及验收条件；[测试结果](tests.md)、[结构化结果](result.json)、[任务交接](../../handoffs/TS-060.md)记录实际执行。受本次显式允许目录约束，未写产品通常使用的 `.codex/handoffs/`，其 `architecture_review` 内容保留在上述结果文件中。

缺失能力的最小实施包见 [后续任务](next-task.md)。须先批准具体内部模块与契约所有权再实现；TS-060 不扩大到业务源代码、数据库迁移、真实 NAS 或发布。
