# TS-099 交付摘要

## 一句话

在 `OperationTrash` 模块内交付**资产成品包只读预检**：零写入、零授权即零 I/O，
冻结错误词表与未验证清单，严格清单解析与路径边界，真实字节摘要与预算执行。

## 交付内容

| 层 | 文件 | 作用 |
| --- | --- | --- |
| Contracts | `MediaPackageContracts.cs` | 请求、报告、问题、去重问题汇聚器、状态枚举 |
| Contracts | `MediaPackageInspectionContracts.cs` | 冻结错误码与 `unverified` 清单 |
| Domain | `MediaPackagePolicy.cs` | 清单语义校验、身份、布局、文件集、目标目录命名 |
| Domain | `MediaPackagePathPolicy.cs` | 原始清单路径词法策略（无 `System.IO`） |
| Application | `MediaPackageInspectionPorts.cs` | 授权 scope、卷空间、时钟、哈希端口 |
| Application | `MediaPackageInspectionLimits.cs` | 全部预算常量（单一来源） |
| Application | `MediaPackageInspectionService.cs` | 编排：授权 → 清单 → 边界 → 目标 → 容量 → 逐文件复核 |
| Infrastructure | `MediaPackageManifestReader.cs` | `System.Text.Json` 严格读取 + BOM/UTF-8/重复键/整数词法/预算 |
| Infrastructure | `IsolatedMediaPackageInspector.cs` | 真实文件系统预检，零写入 |
| Infrastructure | `MediaPackagePathBoundary.cs` | 根锚定、枚举、重解析点拒绝、相对路径解析 |
| Infrastructure | `MediaPackageFileHasher.cs` | 流式 SHA-256，带缓冲上限与预算 |

测试 6 个文件（`MediaPackage{Sandbox,TestPorts,ContractTests,InspectionTests,BoundaryTests,FailureTests}.cs`）
与 6 个候选夹具字节精确副本。

## 关键设计决定

1. **两种摘要分离**：请求 `expected_digest` 是收到字节的原始 SHA-256；夹具/清单锁定摘要用 LF 归一化。
   早期把 Windows CRLF 分发字节摘要当作清单摘要，已纠正并固化为测试。
2. **`Domain` 不含 `System.IO`**：路径以原始字符串进入 `MediaPackagePathPolicy` 做词法判定，
   真实文件系统解析只在 `Infrastructure`。这满足架构门禁的 `system.io` 禁止令牌，也避免 Domain 触碰平台。
3. **越界文件已删除**：`Infrastructure/StrictJsonReader.cs` 不在白名单，已删除；JSON 一律用 .NET 自带
   `System.Text.Json`，严格性通过 `JsonDocumentOptions` + 自有重复键/整数词法/UTF-8 检查实现，
   **没有**引入第二套通用 JSON 解析器。
4. **零授权即零 I/O**：授权在解析清单之前，未授权时连卷空间都不探测（测试断言 `Space.Calls == 0`）。
5. **报告不可授权**：`grants_file_operation` 恒为 `false`；`unverified` 每次都完整回填，
   使"预检通过"与"可发布"在类型层面就不可混淆。
6. **`MediaPackagePolicy.MatchesLayout` 收紧**：只接受根级文件或 `Season ` 目录下的两段路径；
   cid 命名必须含 `-cid-<cid>`，非 cid 条目必须 `cid: null`。

## 已复用而非重造

`RelativeAssetPath`、`LibraryId`、`CanonicalLibraryRoot`、`Sha256Digest`、`StorageAvailability`
沿用各自单一所有者的已发布版本；`MediaPackagePathBoundary`、`MediaPackageFileHasher` 为本模块内
单一实现，被 inspector 复用。未新增主语言、框架或第三方依赖。

## 证据（沙箱内实际执行）

- `dotnet restore AssetLibrary.slnx --locked-mode` → 通过。
- 锁定包完整性：50 个在解内包，`packages.lock.json` 的 `contentHash` 与缓存 `.nupkg.metadata`
  一致 50/50；`.nupkg` 字节 SHA-512 与 `.nupkg.sha512` 一致 50/50。
- `dotnet build AssetLibrary.slnx -c Release` → 15 个项目，**0 警告 0 错误**。
- `check_release_gates.py --target v0.1-start` → `RELEASE_GATE_ALLOWED`。
- 仓库校验 12 步通过，含 `.NET source policy passed (666 C# files)` 与
  `Architecture quality gate passed`（层级方向、跨模块公开层、禁止令牌、循环依赖）。

## 未执行（`needs_validation`）

- `dotnet test`：VSTest ↔ testhost 命名管道被沙箱拒绝，90 秒超时中止。**测试从未运行，不得记为通过。**
- `dotnet format --verify-no-changes`：`MSBuildWorkspace` build host 同样需要命名管道，被拒。
- `tests/database`、`tests/architecture` Python 用例：沙箱拒绝在临时目录写入/清理（环境错误，非断言失败）。
- **本地提交未创建**：本 worktree 的 git 元数据目录
  `projects/assetlibrary/.git/worktrees/assetlibrary5` 在本会话可写工作区之外，
  `git add` 报 `index.lock: Permission denied`。等效交付为 `.runtime/dsh-delivery/changes.patch`
  （29 文件，267114 字节），已实测 `git apply --check --cached`（退出 0，对基线索引干净可应用）
  与 `git apply --check --reverse`（退出 0，与工作树一致）；由协调方应用并提交。

## 风险

1. 静态重解析点拒绝不能关闭恶意并发目录替换（TOCTOU）；真实 NAS 语义与断电持久性未验证。
2. 候选合同为 `task_fixture_only`；冻结为生产合同前若语义变更，需同步更新夹具与词表。
3. 真实测试未执行，回归风险由协调环境复验承担。

## 技术债

1. `MediaPackagePolicy.cs` 776 行、`MediaPackagePathBoundary.cs` 394 行，接近可维护上限，后续可按
   "身份/布局/文件集"拆分职责。
2. 多处以 `[SuppressMessage]` 豁免 `CA1506`/`CA1822`，均带理由；若后续引入组合根可回收。
3. 枚举上限 4096 是常量而非可配置，超大 staging 目录会直接 `budget_exceeded`。

## 建议合并顺序

本卡为只读预检，不依赖其他在途任务，可独立合并；合并前必须由协调方在固定提交上实跑
`dotnet test`、`dotnet format` 与 Python 架构/数据库用例。
