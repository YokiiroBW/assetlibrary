# TS-065 结构化结果

| 项 | 值 |
| --- | --- |
| 任务 | TS-065 资产只读查重与整理计划预览 |
| 状态 | ready_for_review（本切片范围内完成；无执行入口；待协调集成） |
| 分支 / worktree | `work/ts-065` / `C:/YOKI/Codex/tianshu-peiban-bot/worktrees/TS-065/assetlibrary` |
| 基线 | `ff8e8a1fc405870d5a82e44cc344c9e083df059a` |
| 范围 | 仅 `services/core-server/Modules/AssetIdentity/`、`tests/dotnet/AssetLibrary.ReadCore.Tests/`、`docs/integrations/tianshu/`、`docs/handoffs/`、`.codex/` |

## 交付内容

- `Modules/AssetIdentity/Contracts/DedupContracts.cs`、`DedupPorts.cs`
- `Modules/AssetIdentity/Domain/DedupAnalysisEntry.cs`、`DedupAnalysisPolicy.cs`、`DedupScopePolicy.cs`、`DedupPlanPolicy.cs`
- `Modules/AssetIdentity/Application/DedupAnalyzer.cs`、`DedupScopeResolver.cs`、`DedupSourceCollector.cs`、`DedupPlanBuilder.cs`、`DedupPlanItemFactory.cs`、`DedupPlanDigest.cs`
- `Modules/AssetIdentity/Infrastructure/DedupInfrastructure.cs`、`SystemDedupContentReader.cs`、`DedupPathGuard.cs`、`DedupReadWindow.cs`
- `tests/dotnet/AssetLibrary.ReadCore.Tests/DedupGlobalUsings.cs`、`DedupScenario.cs`、`DedupEvidenceTests.cs`、`DedupSourceScopeTests.cs`、`DedupPathContractTests.cs`、`DedupPlanTests.cs`
- `docs/integrations/tianshu/dedup-readonly-preview.md`、`docs/handoffs/TS-065.md`

## 证据规则（与卡一致）

完整强哈希相同才算字节重复；文件名与大小不作证据；近似图、不同编码、动画与附属文件保留为不同资产；不自动合并身份、不以“大图优选”推断可删除；漏扫、断线、权限拒绝、超预算、取消与超时都是“本次未能完成”的具名原因，不当作删除；计划无执行入口、无确认摘要、无写入凭据。

## 验证

| 命令 | 结果 |
| --- | --- |
| `dotnet test tests/dotnet/AssetLibrary.ReadCore.Tests -c Release` | 106 通过 / 0 失败 / 25 跳过（既有 PG、POSIX 条件用例）；切片用例全绿 |
| `dotnet build services/core-server + tests/... -c Release` | 成功，0 警告 0 错误 |
| `python -I -B scripts/validate_architecture_baseline.py` | 通过（566 输入，0 问题） |
| `python -I -B scripts/validate_dotnet_source.py` | 通过（605 文件） |

SDK 解析、离线包源与全部原始命令见 `tests.md`。

## 验收返修（第二轮）

协调验收 `d9d4cf4` 裁决 changes_requested，两项 P1 已修复并以正式断言与协调者探针复验：

1. **计划复核覆盖全部已记录事实。** 每条计划记录（含从未读取的独长/超预算文件）的长度、写入时间、读取状态与跳过原因都参与比较；摘要绑定长度、完整强哈希、结构指纹与写入时间；只有无差异、摘要一致且内容证据仍匹配时才返回 `Identical`。未读取项以 `ContentUnverified` 如实标注，既不伪造内容保证也不谎报失效。
2. **重叠判定对称。** 先按固定登记根/输出根裁决，再对请求内先后出现的源做双向重叠检查；角色矛盾按 `ManagedLibraryOverlap` 拒绝登记侧。子先父后、父先子后、同目录两次提交、inbound 等于/包含已登记库、输出回流在任一顺序下都拒绝，合法原地分析保持可用。

协调者探针副本见 `.runtime/review-repro/`；原始五条复现行现输出 `SourceChanged`/`Disappeared` 与 `accepted=1 rejected=1`/`accepted=0 rejected=1`。

## 架构审查

- **module**：`AssetIdentity`（唯一被修改模块），四个强制层各自持有对应职责；未改其他模块。
- **dependency_direction_ok**：是。Contracts ← Domain ← Application ← Infrastructure；Domain 不引用文件系统/数据库/网络。已移除对 `ScanReconciliation` 内部层的依赖（架构门禁禁止且会形成循环 `assetidentity → scanreconciliation → assetidentity`）。
- **reused_existing_logic**：复用了 `RelativeAssetPath`、`LibraryId`、`CanonicalLibraryRoot`、`StorageAvailability`、`Sha256Digest` 及共享只读排除规则；未复制扫描器、文件操作引擎、计划执行或任何写入路径。
- **duplicated_logic**：仅只读目录遍历为本模块自有实现，原因是跨模块内部依赖被禁止且会成环；排除清单与共享策略保持一致。
- **cross_module_access**：无内部层访问。仅使用其他模块 `Contracts` 层的公开类型。
- **new_language_or_framework**：无。
- **new_major_dependencies**：无（仅 BCL：`System.Security.Cryptography`、`System.Text.Json`、`System.Buffers`）。
- **contract_changes**：无共享契约变更；未改根 contracts、迁移、锁、版本号、原生客户端与发布门禁。
- **technical_debt**：无执行入口（本轮设计）、无 HTTP/平台投影、无近似/同源查重；组合根装配留给协调者。

## 风险与剩余

1. 生产写入仍归 `OperationTrash`/`TransferSync` 及其门禁，本切片不提供执行入口。
2. 未接入天枢、未新增公网 API、未改 `GatewayAuth` 权限合同。
3. 近似/同源查重、分类与优选未实现，保持 unavailable。
4. 复杂度有界性**仅为静态论证**；本轮无 50 万或任何接近规模的跑测，无吞吐/内存/耗时实测，亦无断电/跨进程恢复证据。

**无 BLOCKED 项。** 合同缺口（执行入口、平台身份与投影）属于卡明确排除范围，已记录为后续任务而非阻断。
