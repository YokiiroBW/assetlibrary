# 资产只读查重与整理计划预览

状态：**只读切片已实现并隔离验证；无执行入口，待协调审查集成。**
任务：TS-065（第四轮），依据 V2 §8 / A11 与 `docs/architecture/image-library-curation-design.md`。

本切片只回答一个问题：**已登记库或显式隔离待整理源里，哪些文件当前字节相同，哪些看起来相似但绝不能当作重复。** 它不移动、不复制、不重命名、不删除、不发布，也不产出任何可交给写入方的凭据。

## 实际能力与真实接点

| 能力 | 真实接点 | 说明与边界 |
| --- | --- | --- |
| 源范围裁决 | `DedupScopePolicy.Resolve` | 只接受已登记库根或显式隔离源；拒绝重复登记、库内嵌套待整理目录、输出回流、未知库、文件系统根、离线源。 |
| 只读遍历 | `SystemDedupFileDiscovery` | 本模块自有的只读遍历；不跟随重解析点，祖先含 junction 时整次遍历拒绝（`directory_reparse_point`）。 |
| 流式强哈希 | `SystemDedupContentReader` | 只读 + 共享读打开，`IncrementalHash` 流式 SHA-256，采样首尾做结构指纹；读取中长度变化即拒绝出哈希。 |
| 精确字节重复 | `DedupAnalysisPolicy.BuildGroups` | 同一完整强哈希 + 同一长度才成组；组内不合并资产身份、不推断保留者。 |
| 关系与不可合并项 | `DedupAnalysisPolicy.DetectRelations` | 同名异内容、同长异内容、附属文件、动画变体、编码变体、修订变体全部只作关系，绝不并入身份。 |
| 可审阅计划 | `DedupCurationPlan`、`DedupPlanDigest` | 可 JSON 往返；摘要覆盖本次观察到的证据；计划不含任何文件操作能力。 |
| 确认前重新核对 | `DedupAnalyzer.RecountAsync`、`DedupPlanPolicy.Recount` | 源变化/不可读/消失/新增/来源被拒分别命名，返回差异集合与新计划，不改写旧计划。 |

已核查并**复用**：`RelativeAssetPath`、`LibraryId`、`CanonicalLibraryRoot`、`StorageAvailability`、`Sha256Digest`（均按各自单一所有者发布的版本使用）。**未修改**任何共享契约、数据库迁移、根锁、版本号、原生客户端与发布门禁。

遍历为何不复用 `ScanReconciliation` 内部端口：架构门禁禁止跨模块内部层依赖，且该方向会形成 `assetidentity → scanreconciliation → assetidentity` 循环。因此本模块拥有自己的只读遍历，并与共享发现策略保持同一套排除规则（缩略图缓存、临时文件、`.part/.partial`）。

## 证据规则

- **只有完整强哈希相同才标字节重复。** 文件名、大小、mtime 都不作为证据；同长异内容显式标注为已比较并被否决。
- **读取失败不是删除。** 权限拒绝、文件消失、设备断开、超预算、取消/超时全部作为“本次未能完成”的具名原因上报，不推断资产不存在。
- **不自动合并身份、不以“大图优选”推断可删除。** 动画、不同编码、原图/修订、附属文件一律保留为不同资产，只给出关系。
- **扫描界限如实上报。** 文件数上限、字节预算、单文件上限、遍历中断、超时都进入统计与摘要，绝不把未读文件当成“唯一”。

## 50 万资产复杂度

长度分桶是唯一预筛：`O(N)` 一次遍历 + 每个真实候选一次强哈希，**不做两两全比较**。关系检测用两个索引（基名、长度）并对每桶设 64 候选上限，最坏 `O(N + Σ bucket²)` 且有硬上限。单次分析受文件数、总字节、单文件字节与并发上限约束，超限如实上报。无视觉模型、无 GPU、无付费服务。

## 验证

- `AssetLibrary.ReadCore.Tests`：**96 通过、0 失败、25 跳过**（跳过项为既有 PostgreSQL/POSIX 条件用例，与本切片无关）。其中本切片新增 **39 项**。
- `python -I -B scripts/validate_architecture_baseline.py`：通过（566 份架构输入）。
- `python -I -B scripts/validate_dotnet_source.py`：通过（605 个 C# 文件）。
- 全部为 `.runtime/sandbox-storage` 下的合成隔离验证；未接触真实 NAS、真实库、账号、生产数据库或设备。

隔离用例覆盖：同内容不同目录、跨源同内容、同名异内容、同长异内容、零字节、大文件跨缓冲区、动画/编码/附属/修订关系、junction 不被跟随、仅祖先 junction 即拒绝、路径越界（绝对/驱动器/穿越/反斜杠）、文件读取中消失、遍历中断、离线源、空源、文件数上限、单文件与总字节预算、超时、取消、请求校验、计划 JSON 往返与摘要稳定性、重新核对的一致/变更/消失/新增/被拒/超时。

## 未完成与缺口

1. **无执行入口（本轮设计如此）。** 计划不能移动/复制/删除/发布；生产写入仍归 `OperationTrash` / `TransferSync` 及其门禁，本切片不复制测试沙箱执行器。
2. **无 HTTP / 平台投影。** 未新增公网 API、未改 `GatewayAuth` 权限合同、未接入天枢；组合根装配留给协调者按模块所有权分配。
3. **同源/近似查重未实现。** 感知哈希、特征、视觉向量与分类/优选明确保持 unavailable，不以本切片宣称 A11 全部完成。
4. **规模与耐久未验收。** 50 万资产的复杂度有界性已论证，但没有真实规模跑测、断电或跨进程恢复证据。

## 依据

V2 §8、A11；本产品 `AGENTS.md`、`docs/10_搜索查重AI与元数据增强.md`（10.3 精确与同源查重）、`docs/22_编码与架构开发原则.md`；`docs/architecture/image-library-curation-design.md`；架构门禁 `tests/architecture/architecture-rules.json`。
