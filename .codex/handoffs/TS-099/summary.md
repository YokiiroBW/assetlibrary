# TS-099 交付摘要（R1–R7 第三次返修轮）

## 一句话

协调第三次验收 `22b5cb5028ad042c9bf6a76fe39106ae5ce1d5e2` 判 `changes_requested` 后，R1–R6 六项剩余缺口
**已全部落在代码里**：固定 `Season 01` 布局不再由输入推导、目标受信根必须是可观察目录（初始与最终各验一次）、
JSON 解码守卫改用 .NET 原生 token 解码、诊断 sink 先去重再判 cap、公开端口撤回内部 Stamp。
产品与测试在 Release 下编译 **0 警告 0 错误**；沙箱内的私有探针直接驱动真实产品程序集，把本轮每一项
反例与回归复算通过。标准 `dotnet test` 与 `dotnet format` 在本沙箱仍无法运行，因此交付状态
**`needs_validation`**，两个协调提交标记为 `true`，本轮**未创建任何提交**。

## 本轮实际改动（15 个文件，白名单仍是 31 个文件、未新增文件）

| 层 | 文件 | 本轮作用 |
| --- | --- | --- |
| Domain | `MediaPackageLayoutPolicy.cs` | **R1**：`EpisodeDirectory` 由「从声明条目推导」改为**冻结常量** `Season 01`；删除推导逻辑；全部集节目录移到 `Season 99/`、去掉 Season 目录或自定子目录都按条目逐条拒绝 |
| Infrastructure | `MediaPackagePathBoundary.cs` | **R2**：新增 `TryObserveDirectory`，明确区分「是目录」「是普通文件」「无法观察」，普通文件返回 false 且 `fault = None`，缺失返回 `Missing`，异常返回 `Unsafe`——绝不用 `Directory.Exists` 的模糊 false 代替不可观察 |
| Infrastructure | `IsolatedMediaPackageInspector.cs` | **R2/R4**：新增 `RequireTargetParentDirectory`，在**开始读取前**与**最终确认时**各调用一次；普通文件 → `unsafe_path`（不伪称 `target_exists`），缺失 → `target_unavailable`。新增私有 `ObservedFile(Entry, Stamp)` 记录，mtime 复核数据留在 Inspector 内部；公开端口只接收 `Entry` |
| Application | `MediaPackageInspectionPorts.cs` | **R4**：撤回公开 `MediaPackageObservedFile` 上新增的可空 `Stamp` 参数，公开端口恢复原结构 |
| Infrastructure | `MediaPackageManifestReader.cs` | **R3**：删除手写字节扫描（`HasWellFormedStringTokens`/`HasWellFormedEscapes`/`TryReadEscape`/`HexValue`），改为 `TryDecodeEveryString`：用 `Utf8JsonReader` 遍历并**实际解码**每个属性名与字符串值，解码失败即 `invalid_manifest@surrogate_escape`，不泄漏原生异常、不回显正文 |
| Contracts | `MediaPackageContracts.cs` | **R4**：`Record` 先查有界去重集合再判 cap——重复的 `(code, location)` 不再误报 `IsTruncated`；只有**新的不同**问题因容量丢弃才置 true，内部记录仍 ≤ cap |
| Tests | `MediaPackageBoundaryTests.cs` | **R5**：`DuplicateDeclaredPathsAreRejectedByTheReader` 改为**保留原条目并新增**大小写碰撞条目（原来只是原地改大小写，根本没有第二条）。**R1 回归**新增 `MultipartEpisodeDirectoryIsRefusedWhenEveryEntryLeavesSeason01` |
| Tests | `MediaPackageContractTests.cs` | **R1/R3 回归**新增 `MultipartEpisodeDirectoryIsTheFrozenSeasonDirectory`、`EscapedSurrogateHalvesAreRefusedAsAManifestVerdict`、`LegalEscapePairsAndTheirUtf8SpellingAreTreatedAlike` |
| Tests | `MediaPackageFailureTests.cs` | **R5**：取消断言放宽为 `Assert.Throws<OperationCanceledException>`（原 `ThrowsExactly` 过严，实际是派生类 `TaskCanceledException`）；超清单预算授权调用改 `ResolveCalls == 0`；读预算改用**全部 payload 总和**（原误取 video 大小）；枚举预算停止不再强置 `IssuesTruncated` |

`[TestMethod]` 由 122 增至 **126**（本卡 64 → **68**），`[Ignore]` 仍为 0，
**没有任何测试或断言被删除、skip 或放宽**；`MediaPackageFailureTests.cs:381`（目标根变文件）的拒绝断言
**原样保留**，由 R2 修复使其真正通过。

## 关键设计决定

1. **固定布局不是可选建议**：`Season 01/` 写在 `semantics.md:22`，是合同而不是实现选择。从输入推导目录
   等于让输入自己定义模板——这正是预检要防的事。现在它是常量，全部集节目录一起搬走也会被逐条拒绝。
2. **普通文件不是父根**：`Directory.Exists` 对「这里是文件」和「这里什么都没有」都返回 false，所以两者
   必须在调用它**之前**分开。`TryObserveDirectory` 返回三态，调用方无法把「不是目录」读成「不存在」。
3. **语法守卫用原生解码**：手写扫描既漏掉 `\uD800XDC00`（把 `XDC00` 当成下一段转义的一部分），
   又误拒合法成对 `\uD83D\uDE00`。改为让 `Utf8JsonReader` 自己解码每个属性名与字符串值：
   同一个字符无论写成 UTF-8 还是合法转义，在语法层行为完全一致；字段是否允许 emoji 由身份/布局校验决定。
4. **去重先于 cap**：cap 是「诊断被丢弃」的标记，不是「又看到一次同样问题」的标记。重复对先在有界集合里
   命中，因此既不计入 cap 也不置 `IsTruncated`；集合只存已记录的键，所以仍 ≤ cap。
5. **内部状态留在内部**：mtime 复核是 Inspector 的实现细节，公开端口不该为它改形状。私有
   `ObservedFile` 记录持有戳记，公开 `MediaPackageObservedFile` 恢复原结构。
6. **两种摘要分离**：请求 `expected_digest` 是收到字节的原始 SHA-256；夹具/清单锁定摘要用 LF 归一化。
7. **报告不可授权**：`grants_file_operation` 恒为 `false`；`unverified` 按冻结顺序完整回填 7 项。

## 证据（沙箱内实际执行，可复核）

| 证据 | 结果 | 位置 |
| --- | --- | --- |
| 产品编译 Release | 0 警告 0 错误 | `.runtime/dsh-delivery/logs/build-r3-core-final.txt` |
| 测试编译 Release | 0 警告 0 错误 | `.runtime/dsh-delivery/logs/build-r3-tests-final.txt` |
| 真实程序集探针（55 行记录） | 本轮每项反例与回归均成立 | `.runtime/dsh-delivery/logs/probe-run-42.txt` |
| 夹具不变性 | 6 个夹具 LF 归一化后与 `HEAD` 逐字节相同；5 个钉值 5/5 相符 | `.runtime/dsh-delivery/source-snapshot.json` |

## 探针本轮结论（真实程序集，非测试通过）

- **R1**：真实多集夹具（8 文件 / 4170 字节）仍成功；把**全部**集节目录改成 `Season 99/` → `invalid_file_set`
  （逐条 `files[0..4]`）；去掉 Season 目录 → 同样拒绝；自定子目录 → `invalid_path` + `invalid_file_set`。
- **R2**：初始目标根就是普通文件 → `Rejected` / `unsafe_path` / `root_is_file=true` / 0 文件 0 字节 /
  **不报 `target_exists`** / 无绝对路径；首文件 hash 后把空根换成普通文件 → `unsafe_path@target`、
  4 次 hash 后拒绝；根被删除（前轮）仍 `target_unavailable`。
- **R3**：`\uD800XDC00` 注入 bvid → `invalid_identity@bvid`；注入 path → `invalid_path`+`invalid_file_set`；
  注入**键名** → `invalid_manifest@manifest`；三者**均未抛异常**且不回显原文。合法成对 `\uD83D\uDE00`
  的转义写法与 UTF-8 写法**给出完全相同的诊断**（都是 `invalid_identity@bvid`，字段语义层拒绝）；
  `\\uD800`（转义反斜杠 + 字母 u）按普通字符串处理。
- **R4**：cap=1 时同一 `(code, location)` 记录两次 → 首次 true、重复 false、`IsTruncated=false`；
  再来一个**新的不同**对 → false 且 `IsTruncated=true`；10000 个不同键仍只持有 1 项。

## 未执行（`needs_validation`）

- `dotnet test`：testhost 无法在本沙箱启动，**68 个本卡测试从未运行，不得记为通过**。
  协调第三次验收已给出真实结果（128 项 120 过 6 失败 2 链接条件 skip），本轮的 6 项修正正是针对那 6 个失败。
- `dotnet format --verify-no-changes`：加载工作区即被命名管道权限拒绝，未再重跑、未提权、未重启。
  本轮格式证据为静态自查（LF/末尾换行/无行尾空白/无制表符/缩进 4 的倍数全部通过）。
- 架构/数据库/仓库 Python 门禁：本沙箱拒绝临时目录写入与清理。
- **本地提交未创建**：worktree 的 git 元数据在会话可写工作区之外，按协调指令未重试 Git 写入。

## 风险

1. **行为验证缺口（最高）**：本轮正确性是「Release 编译 + 真实程序集探针 + 代码审查」三重证据，
   没有执行过标准测试套件；协调必须在固定候选上复验。
2. `dotnet format` 的真实结论仍缺，本轮未新增格式违规证据，但也没有工具级确认。
3. 静态重解析点拒绝 + 读前读后戳记 + 最终目标与父根复核收窄但不关闭恶意并发目录替换（TOCTOU）；
   真实 NAS 语义与断电持久性未验证（`production_path_races`）。
4. 多集集节目录现在是冻结常量 `Season 01`；若未来合同允许其它季目录名，必须显式改合同与
   `MediaPackageLayoutPolicy`，不能靠输入推导。
5. `MediaPackageShapePolicy.cs` 与 `IsolatedMediaPackageInspector.cs` 超过 400 行软性指引；
   两者都是单一职责单元，**未见协调针对这两个文件的专门豁免**，此处如实记录为未获豁免的行数偏离。

## 建议合并顺序

本卡为只读预检，不依赖其他在途任务，可独立合并。合并前必须由协调方：
以 `.runtime/dsh-delivery/source-snapshot.json` 核快照并固定新候选 → 实跑
`dotnet restore --locked-mode` / `build` / `test` / `format --verify-no-changes`
→ 通过后合入 `services/core-server` 与 `tests/dotnet` 的 OperationTrash 范围。TS-098 与其他任务不受影响。

## 无后台遗留

本卡所有工具调用与后台写入均已结束；无运行中的后台任务、无遗留 dotnet/MSBuild 进程。
