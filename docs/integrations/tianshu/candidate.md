# 候选报告 0.1.0-candidate

此文件及 schema 是 TS-060 提案，不是 AssetLibrary 或天枢已发布的网络协议；没有对应 HTTP URL。生产者拟为 AssetLibrary 分析用例，消费者拟为平台审阅视图。当前两个真实适配器均未实现，夹具不能宣称双方验收。

只读发现继续复用已批准的 AssetLink [试用协议](../../../contracts/assetlink/read-only-trial-v1.md)和[交互查询](../../../contracts/assetlink/web-interaction-v1.md)。跨产品发布前，协调者需将审核后的唯一版本及双方样例放入协调仓库 `contracts/`；不要复制整个 AssetLink SDK 或生产契约到本目录。

## 最小形状

报告以 `report_id/revision` 标识不可变快照，`request_id` 关联请求，`library_id` 限定一个已授权库，`observed_at` 表示生产者观察时间。`analysis_state=unavailable` 与“分析已完成但候选为空”不同。源修订的可靠绑定当前缺失：`source_revision=null` 明确未验证；不能由 mtime、文件名、UUID 或分页游标推导内容版本。

`entries` 只列有限候选涉及的稳定 ID 和可空强哈希，无原件字节、绝对路径、数据库地址、身份声明或凭据。一次最多100个引用和100组建议；这不是全库快照，也不保证500,000资产性能。大库分析必须未来在 AssetLibrary 内有界后台完成；平台不能遍历全库后自行做一遍查重。

`candidates` 包含建议 ID、种类、成员 ID、证据和解释：

- `exact_duplicate`：至少两项；只有 AssetLibrary 同一稳定读取阶段的完整 SHA-256 才可提出，哈希为空不能认定重复。相同内容不同路径仍可为有意副本。
- `same_source`：至少两项；记录同源/裁剪/压缩等候选，不按像素相似度宣告相同文件。
- `classification`：至少一项和明确候选标签；`filename` 证据只表示名称提示，不能直接决定身份、角色、授权或物理目录。
- `preferred`：至少两项且 `preferred_entry_id` 必须属于本组。只标优选显示；必须保留原图/修订图、动画和附属文件，不附带删除或覆盖指令。

schema约束结构，测试中的关系检查另外验证唯一 ID、同库成员、优选成员、精确重复强哈希一致。该检查只是提案验证器，不是产品查重算法。`execution_available` 永远为 false；未知字段拒绝，避免夹带 `delete`、路径或自授权限。

[合成建议实例](examples/review-candidates.json)全部人工构造，ID不是实际 Core 返回、建议不是产品分析输出；[不可用实例](examples/unavailable.json)表达当前缺失分析器。它们用于审阅与负例验证。

## 整理计划复用而不复制

已有应用类型是 `OperationPlanRequest → PreparedOperationPlan → OperationPlanResult`，包含 `PlanId/Items/ExpectedSource/Deadline`、`Preflight.Confirmation/ExpiresAt` 与逐项失败/目标强校验。后续网络适配应直接映射这些公开类型，补充真实调用身份、库授权与 AssetLibrary 签发的位置引用；本提案不发布第二套通用任务或计划 schema。

报告 ID、优选项或 source_revision 都不是确认摘要。准备不执行；确认必须绑定确切计划、预检和当前权限；源变化、保护新增、同名冲突、空间不足、超时及过期执行权均拒绝或返回逐项失败。失败先保留源，完成须目标可读和完整强哈希相符。

生产发布回执、事件版本、索引回执、执行状态查询及幂等恢复未定义完整，不能通过模拟 `completed` 解除门禁。尤其现有沙箱 `MemoryOperationStore` 不证明跨进程重复请求安全；此缺口由下一任务列入验收。
