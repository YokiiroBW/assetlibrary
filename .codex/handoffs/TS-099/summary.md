# TS-099 交付摘要（R1–R7 第二次返修轮）

## 一句话

协调第二次验收 `1b5ad8de7508237ffa95ef660c66952a407b5493` 判 `changes_requested` 后，R1–R7 七项修复
**已全部落在代码里**：产品与测试在 Release 下编译 **0 警告 0 错误**；沙箱内的探针程序直接驱动真实产品程序集，
把 59 个失败用例的根因（仓库根查找）、两个正例、17 个负例、边界分隔符判定、原始字节摘要规则、
只读清单快照、有界诊断、三种代理转义拒绝与 10 个真实端到端预检全部复算通过。
标准 `dotnet test` 与 `dotnet format` 在本沙箱被 IPC 拒绝而**未执行**，因此
交付状态 **`needs_validation`**，两个协调提交标记为 `true`，本轮**未创建任何提交**。

## 本轮实际改动（12 个文件，白名单仍是 31 个文件、未新增文件）

| 层 | 文件 | 本轮作用（R1–R7） |
| --- | --- | --- |
| Tests | `MediaPackageSandbox.cs` | **R1**：`FixtureRelativeDirectory` 改为相对仓库根的**完整**路径；`FixtureRoot` 由它拼出；根查找同时从测试二进制目录与当前目录向上找 `AGENTS.md` **加**该完整夹具路径 |
| Domain | `MediaPackageLayoutPolicy.cs` | **R2**：整体重写为**全路径**比对（不再只比文件名），集节目录由声明的分集条目推导；重复角色按「角色 + 该 part 的 cid」判定，根级角色仍全局唯一 |
| Domain | `MediaPackageShapePolicy.cs` | **R2**：类与记录改 `internal`，7 个成员由 `public static` 改 `internal static`（只被同模块 `MediaPackagePolicy` 使用） |
| Domain | `MediaPackagePolicy.cs` | **R2**：不再丢弃布局结论、不再重复路径循环；布局拒绝却未记因时补记 `invalid_file_set@files` |
| Infrastructure | `MediaPackagePathBoundary.cs` | **R3**：去掉「Windows 才把 `/` 当占用分隔符」的分支，正斜杠在**所有平台**被接受并在真实访问前转成原生形式；反斜杠仍处处拒绝。**R7**：`FaultReason` 的 switch 表达式分支按原诊断**插入四个空格**（305–311 行） |
| Infrastructure | `IsolatedMediaPackageInspector.cs` | **R3/R4/R5**：`LoadAsync` 增加 `targetName`；容量拒绝只出一个码（未知空间 `target_unavailable`，只有观测到真实缺口才 `insufficient_space`）；父根缺失映射为 `target_unavailable` 而不是 `source_missing`；`ReadAndRecheckAsync` 回传长度与最后写入戳并存入 `MediaPackageObservedFile`，最终复核据此发现**等长改写**；结尾重新观测目标与其受信父根；`Location` 只写目标目录名，不再泄漏绝对路径 |
| Application | `MediaPackageInspectionPorts.cs` | **R4**：`MediaPackageObservedFile` 增加可空 `(Length, ModifiedAt)` 戳 |
| Application | `MediaPackageInspectionService.cs` | **R5**：按卡文明确分类——`IsAvailable` 只看存储可用性（离线/不可用 → `target_unavailable`），`IsCurrent` 看许可有效期（到期 → `scope_changed`），`IsCurrentAsync` 看运行中撤权/版本改变（→ `scope_changed`）；开头与结尾各判一次，互不合并 |
| Infrastructure | `MediaPackageFileHasher.cs` | **R4**：每次读取按剩余额度裁剪；额度用尽后探读 1 字节，仍有数据即抛预算异常 |
| Infrastructure | `MediaPackageManifestReader.cs` | **R6**：在任何成员名或值被取出**之前**，按原始 JSON 字节扫描每个字符串 token 的 `\uXXXX` 转义；孤立的代理半对记为 `invalid_manifest@surrogate_escape`，异常类型与原文都不进报告 |
| Contracts | `MediaPackageContracts.cs` | **R5**：`MediaPackageIssueSink` 有界——`Issues` 返回 `AsReadOnly()`，`Codes` 由 `HashSet` 改 `List` 并以 `IReadOnlyList` 只读暴露，达到上限即 `IsTruncated = true` 且不再写去重表 |
| Tests | `MediaPackageBoundaryTests.cs` | **R7**：把过时注释改为「正斜杠在所有平台被接受并转原生形式、反斜杠处处拒绝」（断言本身早已与平台无关） |
| Tests | `MediaPackageFailureTests.cs` | **R5**：按卡文更正被点名的两个用例——到期许可更名 `ExpiredScopeIsRefusedAsScopeChanged` 并断言 `scope_changed`，离线保持 `target_unavailable`；两者都新增「不得报另一个码」的断言 |

本轮**未新增、未删除、未跳过、未削弱任何测试**（`[TestMethod]` 仍为 122 个，`[Ignore]` 为 0，
测试工程总计 128 个测试结果与协调基线一致：69 通过 / 59 失败，59 个失败全部是 R1 的根查找缺陷）。
6 个候选夹具字节精确副本内容**一字未改**：磁盘 4 个为 CRLF、HEAD 为 LF，**LF 归一化后与 HEAD 完全相同**，
且仍与夹具 `manifest.json` 的 5 个钉值逐一相符。

## 关键设计决定

1. **两种摘要分离**：请求 `expected_digest` 是收到字节的原始 SHA-256；夹具/清单锁定摘要用 LF 归一化。
2. **`Domain` 不含 `System.IO`**：两个策略类是纯函数（无 IO、无时钟、无环境）；路径先词法拒绝，
   再由 `Infrastructure` 规范化并访问。`MediaPackagePathPolicy` 是原始路径拼写的**唯一**权威。
3. **比较形式与访问形式分离**：受信根在比较时统一为规范正斜杠形式并沿用 `RootPathComparison`，
   真实文件访问仍用平台原生形式。
4. **两种缺失严格分开**：`source_missing`（包/文件缺失）与 `target_unavailable`（目标或其父根不可用）；
   `Missing` 与 `Unsafe` 也严格分开，拒绝永不被读作「不存在」。
5. **零授权即零 I/O**：授权在解析清单之前；未授权时连卷空间都不探测。
6. **报告不可授权**：`grants_file_operation` 恒为 `false`；`unverified` 每次都按冻结顺序完整回填 7 项。
7. **模板精确到整条路径**：`cid-10` 不等于 `cid-101`，集数必须与所选 part 一致，未选 cid 一律拒绝，
   且文件必须落在由分集条目推导出的那个集节目录里。
8. **拒绝先于取值**（本轮新增）：`R6` 的判定必须在**读字符串之前**完成，因为读一个孤立代理转义本身就是抛异常的那一步。

## 已复用而非重造

`CanonicalLibraryRoot` / `RootPathComparison` / `LibraryId` / `StorageAvailability`（LibraryStorage.Contracts）、
`Sha256Digest` / `PayloadFacts`（TransferSync.Contracts）、`MediaPackageIssueSink` /
`MediaPackagePreflightCodes` / `MediaPackageUnverifiedFacts` / `MediaPackageInspectionLimits` 均沿用既有
单一所有者版本。未新增主语言、框架或第三方依赖，未修改根合同。

## 证据（沙箱内实际执行，可复核）

| 证据 | 结果 | 位置 |
| --- | --- | --- |
| 产品编译 Release | 0 警告 0 错误 | `.runtime/dsh-delivery/logs/build-r2-core-4.txt` |
| 测试编译 Release | 0 警告 0 错误 | `.runtime/dsh-delivery/logs/build-r2-tests-3.txt` |
| 真实产品程序集探针（42 行记录） | 见下节逐项结论，`misses: []` | `.runtime/dsh-delivery/logs/probe-run-40.txt` 与 `.runtime/probe/verify/verify-results.json` |
| 仓库根查找（R1） | 从测试二进制目录与当前目录**都**解析到仓库根，夹具目录存在且 6 个文件 | 同上 `r1_repository_root_discovery` |
| 夹具钉值 | 5/5 LF 归一化摘要相符；`manifest.json` LF 摘要 `b523ba43…` 相符；6 个夹具 LF 归一化后与 HEAD 完全相同 | 本轮以脚本对夹具与 `git show HEAD:…` 逐文件独立复算（见 `tests.md`） |
| 格式检查 | 命名管道 `UnauthorizedAccessException`，未检查任何文件 | `.runtime/dsh-delivery/logs/format-r2-2.txt`、`format-r2-3.txt` |

## 探针逐项结论（真实程序集，非测试通过）

- **R1**：根查找修复后，测试二进制目录与当前目录都解析到同一仓库根，夹具目录 6 个文件。
- **R2**：6 个布局反例（单集文件进子目录、单集 nfo 进子目录、未选 cid、错季、缺季、第二目录）
  全部 `invalid_file_set`；两个正例 `succeeded: true`；17 个负例 `misses: []`。
- **R3**：真实多集包（8 文件 / 4170 字节）端到端 `Inspected`；`target_created_during_read` →
  `target_exists@bilibili-BV0000000001-cid-101`（**无绝对路径**）；`target_parent_removed_during_read` →
  `target_unavailable`；`missing_target` → `target_unavailable`。
- **R4**：`source_same_size_changed_after_read` → `source_changed`（等长改写被戳记发现）；
  读预算少 1 字节 → `budget_exceeded`（3 次哈希后停），刚好够 → `Inspected`。
- **R5**：`unknown_space` → 仅 `target_unavailable`（不再同时给 `insufficient_space`）；诊断 sink 记 10000 次
  只报 1 条、`IsTruncated = true`、`Issues`/`Codes` 都不能转成可变列表、回写被拒。
- **R6**：值里的 `\uD800`、值里的 `\uDC00`、**键**里的 `\uD800` 三种注入都返回
  `invalid_manifest@surrogate_escape`，**零异常**、**原文零回显**。
- **边界**：17 个拒绝拼写与 11 个接受拼写 `misses: []`；`/` 嵌套路径在 Windows 上解析成功且被包含，
  `\` 得 `Unsafe`，`..` 等 5 个逃逸全部拒绝，末尾分隔符拒绝，兄弟目录不包含，父子重叠判定正确。
- **清单**：单集目标目录 `bilibili-BV0000000001-cid-101`、多集 `bilibili-BV0000000001`；
  CRLF 改写与键序重排都因原始摘要不同被拒为 `digest_mismatch`，重排后用**自己的**摘要则被接受；
  已校验清单的 `Files`/`SelectedParts` 都不能 Add（`NotSupportedException`）。
- **报告形状**：单集 4 文件 / 2154 字节、多集 8 文件 / 4170 字节，`scope_revision` 保留，
  `grants_file_operation` 恒 `false`，`unverified` 与冻结 7 项顺序完全一致，`issues_truncated` 为 `false`。

## 未执行（`needs_validation`）

- `dotnet test`：testhost 无法在本沙箱启动，**64 个测试从未运行，不得记为通过**。协调需在固定候选上实跑。
- `dotnet format --verify-no-changes`：`MSBuildWorkspace` 的 build host 需要命名管道，被 `UnauthorizedAccessException`
  拒绝，加载工作区即中止；缩小 `--include` 重跑同样失败，故**未再重跑、未提权、未重启会话**。
  因此 **R7 的缩进修复本身未经格式工具确认**，由协调的格式门禁核验。
- 架构/数据库/仓库 Python 门禁：本沙箱拒绝临时目录写入与清理，未执行。
- **本地提交未创建**：本 worktree 的 git 元数据目录在会话可写工作区之外，`git add` 报
  `index.lock: Permission denied`；按协调指令**未重试 Git 写入**。交付等效物为
  `.runtime/dsh-delivery/source-snapshot.json` 中 31 个文件的原始字节摘要与长度。

## 风险

1. **行为验证缺口（最高）**：R1–R7 的正确性目前是「Release 编译 + 代码审查 + 真实程序集探针」三重证据，
   没有执行过标准测试套件。协调必须在固定候选上跑门禁才能闭环。
2. **R7 缩进未经工具确认**：`MediaPackagePathBoundary.cs` 305–311 行按原诊断插入四个空格，
   但本沙箱无法运行格式工具；这一处必须由协调的格式门禁确认。
3. 静态重解析点拒绝 + 读前读后戳记 + 最终目标与父根复核收窄但不关闭恶意并发目录替换（TOCTOU）；
   真实 NAS 语义与断电持久性未验证（`production_path_races`）。
4. 候选合同为 `task_fixture_only`；多集集节目录由声明的分集条目推导、`S01E{≥2位}` 宽度由夹具固定，
   未来若允许另一种季目录命名需显式扩展 `MediaPackageLayoutPolicy`。
5. `MediaPackageShapePolicy.cs` 与 `IsolatedMediaPackageInspector.cs` 超过 400 行软性指引；
   两者都是协调授权的单一职责单元，未用多语句单行或全局抑制掩盖职责集中。

## 技术债

1. 布局策略改为全路径比对并按 part 去重角色；旧的「只比文件名」近似判断已彻底移除。
2. inspector 保留每个观测文件的长度与最后写入戳，并在结尾重新观测目标与受信父根。
3. `R6` 的判定从「读值后校验」改为「读值前按原始字节判定」，因为读值本身会抛异常。
4. 诊断 sink 有界且只发只读视图。
5. 枚举上限 4096 是常量而非可配置，超大 staging 目录会直接 `budget_exceeded`。
6. 多处以 `[SuppressMessage]` 豁免 `CA1506`/`CA1822`，均带理由；若后续引入组合根可回收。

## 建议合并顺序

本卡为只读预检，不依赖其他在途任务，可独立合并。合并前必须由协调方：
以 `.runtime/dsh-delivery/source-snapshot.json` 核快照并固定新候选 → 实跑
`dotnet restore --locked-mode` / `build` / `test` / `format --verify-no-changes`
→ 通过后合入 `services/core-server` 与 `tests/dotnet` 的 OperationTrash 范围。TS-098 与其他任务不受影响。

## 无后台遗留

本卡所有工具调用与后台写入均已结束；无运行中的后台任务、无遗留 dotnet/MSBuild 进程。
