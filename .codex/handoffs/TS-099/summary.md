# TS-099 交付摘要（R1–R6 修复轮）

## 一句话

协调首验 `5e96662450f1bf4737faa7eb3cfead455f38e354` 判 `changes_requested` 后，R1–R6 六项修复
**全部完成**：产品与测试在 Release 下编译 **0 警告 0 错误**，17 个负例 + 2 个正例经独立静态自查
**19/19 与冻结码一致**，交付补丁可**逐文件复现候选（15/15 字节与 SHA-256 一致）**；
标准 `dotnet test` 与 `dotnet format` 在本沙箱被 IPC 拒绝而**未执行**，
交付状态 **`needs_validation`**，两个协调提交标记为 `true`。

## 交付内容（本轮实际改动）

| 层 | 文件 | 本轮作用 |
| --- | --- | --- |
| Contracts | `MediaPackageInspectionContracts.cs` | `MediaPackageManifestReadResult` 增加 `IssuesTruncated`，使截断状态端到端保留 |
| Domain | `MediaPackageShapePolicy.cs`（**新增**） | 纯 JSON 形状/类型/身份：三层精确键集、`cid` 显式 null 与缺键区分、整数词素、身份词表、尺寸上限、固定字段位诊断 |
| Domain | `MediaPackageLayoutPolicy.cs`（**新增**） | 纯布局：`layout`+`media_extension`+`selected_parts` → 精确必需/可选模板与基数，cid 与集数映射 |
| Domain | `MediaPackagePolicy.cs`（重写为编排层） | 只留冻结预算常量、编排、声明字节总量、真实列表比对、服务端目标目录名；校验后集合为只读快照 |
| Application | `MediaPackageInspectionPorts.cs` | 新增 `MediaPackageBudgetExceededException`（继承 `IOException`，供 inspector 翻成 `budget_exceeded`） |
| Application | `MediaPackageInspectionLimits.cs` | 64 MiB headroom 改为**下限**（`< 0` → `< MinimumTargetHeadroomBytes`） |
| Application | `MediaPackageInspectionService.cs` | 离线/过期 scope → `target_unavailable`（不再误报 `scope_changed`）；截断状态透传 |
| Infrastructure | `MediaPackageManifestReader.cs` | 透传 sink 的真实 `IsTruncated` |
| Infrastructure | `IsolatedMediaPackageInspector.cs` | 枚举边消费边计数；读预算先判后读；目标 `Missing`/`Unsafe` 区分；读后复核 + 最终集合复核 + 目标与 scope 再确认 |
| Infrastructure | `MediaPackagePathBoundary.cs` | 比较统一走规范正斜杠形式（修掉 Windows 根被全拒）；分隔符规则按平台；卷根包含修正；枚举流式计数 |
| Infrastructure | `MediaPackageFileHasher.cs` | 先按真实长度预检，再流式强制，超限抛预算异常而非 `IOException` |
| Tests | `MediaPackageSandbox.cs` | `FixtureRoot` 改指本仓已提交夹具目录；根目录查找以夹具存在为判据 |
| Tests | `MediaPackageContractTests.cs` | R1/R5 回归 10 个（含 19 个用例总计） |
| Tests | `MediaPackageBoundaryTests.cs` | R2 回归 2 个（含 10 个用例总计） |
| Tests | `MediaPackageFailureTests.cs` | R2/R3/R5 回归 9 个（含 28 个用例总计）；346 处 CRLF → LF |

6 个候选夹具字节精确副本内容**一字未改**（LF 归一化摘要仍与夹具 `manifest.json` 的 5 个钉值一致）。

## 关键设计决定

1. **两种摘要分离**：请求 `expected_digest` 是收到字节的原始 SHA-256；夹具/清单锁定摘要用 LF 归一化。
2. **`Domain` 不含 `System.IO`**：两个新策略类是纯函数（无 IO、无时钟、无环境）；路径先做词法拒绝，
   再由 `Infrastructure` 规范化并访问。
3. **比较形式与访问形式分离**（本轮新增）：受信根在**比较时**统一为规范正斜杠形式并沿用
   `RootPathComparison`，真实文件访问仍用平台原生形式——这正是首验里「正常 Windows 根全被拒」的根因修复。
4. **两种摘要与两种缺失**：`source_missing`（包/文件缺失）与 `target_unavailable`（目标或其父根不可用）
   严格分开；`Missing` 与 `Unsafe` 也严格分开，拒绝永不被读作「不存在」。
5. **零授权即零 I/O**：授权在解析清单之前，未授权时连卷空间都不探测（测试断言 `Space.Calls == 0`）。
6. **报告不可授权**：`grants_file_operation` 恒为 `false`；`unverified` 每次都完整回填 7 项。
7. **模板精确化**：弃用 `StartsWith("Season ")` 与 `Contains(cid)` 这类近似判断，改为按冻结表逐字符
   匹配，包括 `cid-10` 不等于 `cid-101`、集数必须与所选 part 一致、未选 cid 一律拒绝。

## 已复用而非重造

`CanonicalLibraryRoot` / `RootPathComparison` / `LibraryId` / `StorageAvailability`（LibraryStorage.Contracts）、
`Sha256Digest` / `PayloadFacts`（TransferSync.Contracts）、`MediaPackageIssueSink` /
`MediaPackagePreflightCodes` / `MediaPackageUnverifiedFacts` / `MediaPackageInspectionLimits` 均沿用既有
单一所有者版本。未新增主语言、框架或第三方依赖。

## 证据（沙箱内实际执行）

| 证据 | 结果 | 位置 |
| --- | --- | --- |
| 产品编译 Release | 0 警告 0 错误 | `.runtime/dsh-delivery/logs/build-r2-core.txt` |
| 测试编译 Release | 0 警告 0 错误 | `.runtime/dsh-delivery/logs/build-r2-tests.txt` |
| 静态自查（17 负例 + 2 正例） | 19/19 一致，0 mismatch | `.runtime/dsh-delivery/logs/static-selfcheck.txt` |
| 补丁复现候选 | 15/15 字节与 SHA-256 一致 | `.runtime/dsh-delivery/logs/verify-patch-reproduction.txt` |
| 逐文件源快照 | 15 个文件 SHA-256 + 真实字节长度 | `.runtime/dsh-delivery/source-snapshot.json` |
| 补丁 | 197836 字节，sha256 `fbac87a23b1f66594e936117f8836abfb4cd10047b7d6861bd46321b8a6c28d1`，纯 LF，15 个文件块 | `.runtime/dsh-delivery/changes.patch` |

首轮已实际通过且未被本轮改动的证据（`restore --locked-mode`、50/50 锁定包双向核对、
`build AssetLibrary.slnx -c Release` 15 项目 0 警告 0 错误、`check_release_gates.py --target v0.1-start`、
仓库校验 12 步含 `.NET source policy passed (666 C# files)` 与 `Architecture quality gate passed`）
仍见 `.runtime/dsh-delivery/logs/` 与 `.codex/handoffs/TS-099/tests.md`。

## 未执行（`needs_validation`）

- `dotnet test`：本轮复跑一次，testhost 在 `Process.GetProcessHandle` 处 `Win32Exception(5)` 拒绝访问，
  **测试从未运行，不得记为通过**（首轮另见 90 秒 testhost 命名管道超时）。
- `dotnet format --verify-no-changes`：`MSBuildWorkspace` build host 需要命名管道，被拒。
- `tests/database`、`tests/architecture` Python 用例：沙箱拒绝在临时目录写入/清理（环境错误，非断言失败）。
- 自建反射 runner：曾跑到真实执行路径（43 用例：4 通过 / 39 失败，失败主因是 runner 自身启动方式导致的
  夹具目录查找错误，不是产品缺陷）；**已按协调指令停止**，两次 600000ms 超时探针的完整原始日志按原样保存
  为 `ts099-inproc-runner-probe-1830.txt` 与 `ts099-inproc-runner-run-1831.txt`。那 4 通过 / 39 失败
  **一律不计入测试结论**。
- **本地提交未创建**：本 worktree 的 git 元数据目录
  `projects/assetlibrary/.git/worktrees/assetlibrary5` 在本会话可写工作区之外，`git add` 报
  `index.lock: Permission denied`；按协调指令**未重试 Git 写入**。等效交付为
  `.runtime/dsh-delivery/changes.patch`，已实测 `git apply --check --reverse`（退出 0，与工作树一致）
  与 scratch 树 `git apply --directory` 逐文件复现（15/15）。

## 风险

1. **行为验证缺口（最高）**：R1–R6 正确性目前只有「编译 + 代码审查 + 独立静态自查」三重证据，
   没有真实执行的端到端测试。协调必须在固定候选上跑标准门禁才能闭环。
2. 静态重解析点拒绝 + 读前读后戳记 + 最终集合复核收窄但不关闭恶意并发目录替换（TOCTOU）；
   真实 NAS 语义与断电持久性未验证（`production_path_races`）。
3. 候选合同为 `task_fixture_only`；多集 `Season 01` 与 `S01E{≥2位}` 宽度目前只由夹具与新增用例固定，
   未来若允许 `Season 02` 需显式扩展 `MediaPackageLayoutPolicy`。
4. `MediaPackageShapePolicy.cs` 548 行与 `IsolatedMediaPackageInspector.cs` 543 行超过 400 行软性指引；
   两者都是协调授权的单一职责单元，未用多语句单行或全局抑制掩盖职责集中。

## 技术债

1. `MediaPackagePolicy.cs` 已按审查要求拆分（形状/身份 → `MediaPackageShapePolicy`，
   布局/模板 → `MediaPackageLayoutPolicy`），编排层保留预算、真实列表比对与目标命名。
2. 多处以 `[SuppressMessage]` 豁免 `CA1506`/`CA1822`，均带理由；若后续引入组合根可回收。
3. 枚举上限 4096 是常量而非可配置，超大 staging 目录会直接 `budget_exceeded`。

## 建议合并顺序

本卡为只读预检，不依赖其他在途任务，可独立合并。合并前必须由协调方：
应用补丁固定新候选 → 实跑 `dotnet restore --locked-mode` / `build` / `test` / `format --verify-no-changes`
→ 通过后合入 `services/core-server` 与 `tests/dotnet` 的 OperationTrash 范围。TS-098 与其他任务不受影响。

## 无后台遗留

本卡所有工具调用与后台写入均已结束；无运行中的后台任务、无遗留 dotnet/MSBuild 进程；
自建反射 runner 路线已停止且不再运行。
