# ALIGN-002 — 扫描物理边界对齐

## 任务

- Task ID：`ALIGN-002`
- 标题：`修复只读根探测与扫描的联接边界`
- 所属里程碑：`V0.1`
- 负责人：`library-asset-scan-owner`
- 分支：`codex/align-002-scan-physical-boundary`
- Worktree：`C:\YOKI\Codex\AssetLibrary-worktrees\ALIGN-002`
- 依赖：V01-004 已合并；以 `c1df723` 的只读核心为审查输入。

## 目标

拒绝把符号链接/目录联接本身或其后代作为库根，并在扫描读取目录和条目之前复核物理祖先；枚举期间目录被替换为联接时中止完整快照，不能把库外内容写入索引。

## 必读上下文

- `AGENTS.md`、`.codex/START_HERE.md`
- `docs/01_核心原则与范围边界.md`、`docs/02_已确认需求基线.md`
- `docs/03_资源库与物理目录模型.md`、`docs/05_总体架构与技术框架.md`
- `docs/08_扫描索引预览与格式处理.md`、`docs/16_版本路线与验收门禁.md`
- `docs/17_Codex并行开发工作流.md`、`docs/22_编码与架构开发原则.md`、`docs/23_M0架构冻结与质量门禁.md`
- `.codex/policies/CODE_QUALITY.md`、`.codex/policies/MODULE_BOUNDARIES.md`
- `docs/adr/ADR-0001_物理文件系统权威.md`、`docs/adr/ADR-0002_一库一物理根.md`、`docs/adr/ADR-0011_模块化单体与端口适配器.md`
- `.codex/tasks/V01-004.md` 与其交接；LibraryStorage/AssetIdentity/ScanReconciliation 既有公开端口。

## 允许修改

- `services/core-server/Modules/LibraryStorage/Infrastructure/**`
- `services/core-server/Modules/ScanReconciliation/Infrastructure/**`
- 必要时上述两模块 Application 调用；不改变公开契约。
- `tests/dotnet/AssetLibrary.ReadCore.Tests/**`
- 本任务包和 `.codex/handoffs/ALIGN-002/{summary.md,result.json,tests.md}`。

## 禁止修改

- `contracts/**`、模块公开 Contracts、Domain、数据库迁移、ADR、依赖与版本锁。
- 其他模块内部实现；禁止跨模块引用 Infrastructure。
- registry、project-state、task-graph 和发布门禁，由主协调线程单独管理。
- 生产写入、真实 NAS/用户资产、Provider/Explorer 或平台发布能力。

## 架构与复用

- LibraryStorage 拥有库根可用性探测；ScanReconciliation 拥有只读发现。
- 复用 `ILibraryRootProbe`、`IReadOnlyFileDiscovery`、`FileDiscoveryException`、`InitialReadOnlyScanService` 的失败中止，以及 `RepositorySandbox`。
- 检查只存在各自 Infrastructure；保留各自明确错误语义，不为两个模块建设跨模块文件系统工具层。
- 不新增语言、框架、依赖、数据库或公开契约。

## 安全与性能边界

- 所有测试只在当前 worktree `.runtime/sandbox-storage/`；联接的源和目标均位于自建 fixture 内。
- 测试覆盖根联接、中间祖先联接、子目录枚举后替换、当前枚举目录替换和正常联接不递归。
- 不声称路径检查消除检查与系统调用之间的微观 TOCTOU；非协作路径突变仍需后续句柄/no-follow 或 Worker 隔离证据。
- 保持流式枚举和有界观察批次；祖先检查的元数据调用成本随目录深度增加，不外推 50 万真实 NAS 性能。

## 验证命令

```text
dotnet restore tests/dotnet/AssetLibrary.ReadCore.Tests/AssetLibrary.ReadCore.Tests.csproj --locked-mode
dotnet format tests/dotnet/AssetLibrary.ReadCore.Tests/AssetLibrary.ReadCore.Tests.csproj --verify-no-changes --no-restore
dotnet build tests/dotnet/AssetLibrary.ReadCore.Tests/AssetLibrary.ReadCore.Tests.csproj -c Release --no-restore
dotnet test tests/dotnet/AssetLibrary.ReadCore.Tests/AssetLibrary.ReadCore.Tests.csproj -c Release --no-build --no-restore
python -B scripts/validate_dotnet_source.py
python -B scripts/validate_architecture_baseline.py
python -B -m unittest discover -s tests/architecture -p test_*.py
python -B -m unittest discover -s tests/spikes/assetlink -p test_*.py
python -B tests/architecture/check_release_gates.py --target production-file-writes
git diff --check
```

最后的 production-file-writes 必须继续以退出码 3 阻断；不是发布证明。

## 完成标准

- [x] 沙箱回归明确证明旧实现允许跨物理边界。
- [x] 静态根/祖先和确定性目录替换被拒绝，首次扫描失败中止完整快照。
- [x] 原有只读、流式和忽略规则保持。
- [x] 目标测试、源码、架构与契约验证通过。
- [x] 标准交接与可引用 commit 完成，限制和风险如实记录。
