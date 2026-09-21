# TS-099 测试与验证记录（第五次返修轮）

本文件严格区分三类证据：**已实际执行并通过**、**已实际执行但被环境阻断**、**未执行**。
不得把编译通过读作测试通过，不得把被环境阻断读作断言失败，不得把上一候选的实跑结果读作本轮结果。

## 0. 两种口径：128 个测试方法与 134 项展开结果

必须先分清这两个数字，否则每一轮都会对不上：

| 口径 | 数值 | 含义 |
| --- | --- | --- |
| **测试方法数** | **128** | 本解决方案测试程序集里 `[TestMethod]` 的个数（本卡 MediaPackage 相关 **70**）。这是 DSH 侧可以静态数出的数，也是本卡交付的用例数 |
| **展开结果数** | **134** | 协调真实运行时的结果条数（含数据驱动/参数化展开与框架自身产生的条目）。**这个数字只属于协调环境** |

协调第五次验收的真实结果是「**134 项：131 通过、1 失败、2 链接条件跳过**」（本卡 70 项为 67 通过、
1 失败、2 跳过；原有 64 项通过），判定 `changes_requested`。
**DSH 从未运行过任何测试**，因此 134/131/1/2 一律标注为「协调环境实测」，不得写成本卡执行结果。

## 0.1 本轮（协调第五次验收后）证据摘要

候选 `68a278c358dde0d7a0ce85ed89e66ccdf90a9a6c`（第五候选；上一候选 `acd8eb6a` 是**第四**候选）。
本轮**全部生产代码冻结**，唯一改动是**一个测试文件里的一处计数语义**，其余为六份文档校准。

| 证据 | 结果 | 日志 |
| --- | --- | --- |
| 测试编译 Release（本轮改动后，一次） | 退出 0，**0 警告 0 错误** | `.runtime/dsh-delivery/logs/build-r5-tests-1.txt` |
| 产品编译 Release | **本轮未重跑**：本轮未改任何生产文件，上一候选的真实构建已通过 | `.runtime/dsh-delivery/logs/build-r4-core-1.txt`（上一轮） |
| 真实程序集探针 | **本轮未重跑**：本轮无生产改动；上一候选的 55 行记录仍成立 | `.runtime/dsh-delivery/logs/probe-run-44.txt`（上一轮） |
| 标准 `dotnet test` | **环境阻断，0 个测试运行**（已知受限路径，本轮**不再重试**） | `.runtime/dsh-delivery/logs/test-r4-narrow-1.txt` |
| 标准 `dotnet format --verify-no-changes` | **环境阻断**（已知受限路径，本轮**不再重试**） | `.runtime/dsh-delivery/logs/format-r2-2.txt`、`format-r2-3.txt` |

**没有任何测试在本沙箱内运行过。本卡不宣称任何测试通过。**

## 1. 本轮唯一的测试修正（R1）

`tests/dotnet/AssetLibrary.TransferOperation.Tests/MediaPackageFailureTests.cs` 的
`TargetRootReplacedByAFileAfterTheFirstHashIsRefused`。

**症状**：包装器在**每次**真实哈希返回后都触发回调，测试却用一个 `hashed` 计数器同时承担两种含义，
并断言 `Assert.AreEqual(1, hashed)`。真实运行读了 4 个文件、回调触发 4 次，于是断言在**换根已经正确发生、
最终也正确拒绝**的情况下提前失败——这是测试把「真实哈希次数」误当成「换根次数」，
**不是产品缺陷**（协调已确认产品在首 hash 后换根、继续读取并在最终检查拒绝，符合卡文约定）。

**修法**（按卡 5 第 11–14 行的明确语义，三个计数器分开）：

| 计数器 | 语义 |
| --- | --- |
| `hashCalls` | **每次**真实哈希返回后递增。它是被检包的性质，**不是**本场景的性质，因此**从不断言成固定值**（尤其不硬编码 4） |
| `flipCount` | 只在**换根操作真的执行成功后**递增 |
| `flipAfterHashCall` | 换根成功时记录当时的 `hashCalls`，即「换根发生在第几次真实哈希之后」 |

回调逻辑：`hashCalls++` → 只有 `hashCalls == 1` 才在**本测试自己的**沙箱内执行换根
（先自证绝对路径且在夹具根下）→ 换根成功后 `flipCount++` 并 `flipAfterHashCall = hashCalls`；
后续哈希仍走真实哈希器，但**不再换根**。

结束断言：`flipCount == 1`、`flipAfterHashCall == 1`、`hashCalls >= flipAfterHashCall`、`hashCalls > 0`。
这样证明的是「实际读取之后才换根，且只换一次」，而不是把期望值绑定到夹具的文件数上。
包装器注释同步更正：回调在**每次**真实哈希后触发，由测试决定第几次执行换根；包装器保持纯装饰器语义，
**没有**「first」的概念，也未改 `MediaPackageSandbox` 或新增公开观测接口。

**保留的断言一字未动**：目标根确为普通文件、`Rejected`、`unsafe_path`、
**非** `target_exists`、`VerifiedFileCount == 0`、位置相对且无绝对路径、破坏性步骤的沙箱边界自证。
**没有**删场景、skip 或放宽任何拒绝条件。

## 2. 历史各轮六失败/单失败的分类（直接引用协调报告，不再混写）

| 轮次 | 候选 | 协调真实结果 | 失败分类 |
| --- | --- | --- | --- |
| 第三次验收 | `22b5cb50`（第三候选） | 128 项：120 过、**6 失败**、2 skip | 见 `docs/development/reviews/TS-099-r3-review-2026-09-21.md`：其中**目标根被换成普通文件仍 `Inspected` 是真实产品缺陷**，其余为测试构造/断言问题，「少一字节」读预算属邻接构造同步修正 |
| 第四次验收 | `acd8eb6a`（**第四**候选） | 132 项：129 过、**1 失败**、2 skip | 见 `docs/development/reviews/TS-099-r4-review-2026-09-21.md`：唯一失败是重复路径夹具拼成非法 JSON（测试侧），另有一项真实生产缺陷（`MediaPackageIssueSink` 内部去重集合无界增长） |
| 第五次验收 | `68a278c`（**第五**候选） | 134 项：131 过、**1 失败**、2 skip | 见 `docs/development/reviews/TS-099-r5-review-2026-09-21.md`：唯一失败是本文件 §1 的哈希计数误用（测试侧）；**生产缺陷已全部闭合** |

**候选编号不得混写**：`acd8eb6a` 是第四候选，`68a278c` 是第五候选。

## 3. 已实际执行并通过（沙箱内，本工作树）

| 命令 | 结果 | 日志 |
| --- | --- | --- |
| `dotnet restore AssetLibrary.slnx --locked-mode -p:NuGetAudit=false -m:1` | 退出 0，14 个项目还原 | `.runtime/dsh-delivery/logs/restore-locked2.txt` |
| 锁定包完整性核对（50 个在解内包） | 双向一致 50/50 | `.runtime/dsh-delivery/logs/lock-hashes.txt` |
| `dotnet build AssetLibrary.slnx -c Release --no-restore -m:1 -p:BuildProjectReferences=false` | 退出 0，**0 警告 0 错误**，15 个项目 | `.runtime/dsh-delivery/logs/build-slnx2.txt` |
| `dotnet build tests/.../AssetLibrary.TransferOperation.Tests.csproj -c Release` | 退出 0，0 警告 0 错误 | `.runtime/dsh-delivery/logs/build-r5-tests-1.txt`（本轮） |
| `python -B tests/architecture/check_release_gates.py --target v0.1-start` | `RELEASE_GATE_ALLOWED: v0.1-start` | `.runtime/dsh-delivery/logs/release-gates.txt` |
| 仓库校验 12 步 | 全部通过 | `.runtime/dsh-delivery/logs/verify-steps.txt` |
| 静态空白自查（支持性，见 §5.3） | 19 个文件；7 个仅有 >120 列长行，其余规则无发现 | `.runtime/dsh-delivery/logs/whitespace-selfcheck-r4.txt` |

**与「真实探针」分开记录**：`.runtime/probe/verify/` 是**本卡私有的沙箱外工具**，不属于 31 文件白名单、
不进交付候选。它引用 `services/core-server/bin/Release/net10.0/AssetLibrary.CoreServer.dll`，
因此驱动的是**真实产品程序集**——它能证明具体行为在真实程序集上成立，**不能**替代标准测试宿主
（不运行 MSTest 夹具生命周期，也不覆盖 70 个用例里其余仅依赖产品 API 的断言组合）。
本轮**未重跑**它，因为本轮没有任何生产改动；上一候选的 55 行记录
（`.runtime/dsh-delivery/logs/probe-run-44.txt`）仍然成立，其中与本轮相关的是：
cap=1 时重复对不置截断、新的不同对才置截断、**10000 个不同 location 后公开 1 条而内部
`issues=1`、`recorded=1`、`codes=1`**；目标根初始即普通文件与首 hash 后变普通文件均拒绝 `unsafe_path`、
0 文件 0 字节、不报 `target_exists`、无绝对路径。

## 4. 已实际执行但被环境阻断（不是通过，也不是断言失败）

| 命令 | 现象 | 判定 |
| --- | --- | --- |
| `dotnet test ... --filter "FullyQualifiedName~MediaPackage"` | `vstest.console process failed to connect to testhost process after 90 seconds`，测试运行中止 | **测试从未运行**；VSTest 与 testhost 之间的命名管道被沙箱拒绝。**已知受限路径，本轮按卡 5 第 21 行不再重试** |
| `dotnet test ... -c Release --no-build --no-restore`（首轮） | 同上 | 同上 |
| `dotnet format AssetLibrary.slnx --verify-no-changes --no-restore` | `UnauthorizedAccessException` at `NamedPipeClientStream.TryConnect` | `MSBuildWorkspace` 的 build host 需要命名管道。**已知受限路径，本轮不再重试** |
| `python -m unittest discover -s tests/architecture` / `tests/database` | `PermissionError` 于临时目录写入/清理 | 环境错误，非断言失败 |

按协调指令：**不**申请提权、**不**改动 ACL 或全局配置、**不**改断言/skip/替换测试引擎、
**不**重启会话、**不**做全局 kill、**不**为证明本运行目录再搭新 runner。

### 关于自建反射 runner 与临时探针（均已停止，仅作受限留痕）

- 更早一轮的 in-process 反射 runner（`.runtime/dsh-delivery/logs/ts099-inproc-runner/`）因
  `AppContext.BaseDirectory` 指向 runner 输出目录而夹具搜索起点不对，其 4 通过 / 39 失败
  **不是产品缺陷**，按协调指令已停止，**不计入测试结论**。
- `.runtime/probe/escape` 临时探针两次无输出超时后已按协调指令停止；静态读取还发现它把
  `examples.json` 根数组误当对象成员，故其失败**不是产品结论**。原样保留，未删除、未重试。

## 5. 未执行

- `dotnet test`：**70 个本卡已编写测试方法**
  （`MediaPackageBoundaryTests` 11、`MediaPackageContractTests` 22、
  `MediaPackageFailureTests` 30、`MediaPackageInspectionTests` 7）
  **一个都没有在标准测试宿主中运行过**。本轮同样未运行。
  协调第五次验收的真实结果（134 项 131 过 1 失败 2 链接条件 skip）针对的是**上一版候选**，
  本轮改动**尚未**经过任何测试宿主。§1 的修正即针对那唯一失败。
- `dotnet format --verify-no-changes` 的真实结论。加载工作区即被命名管道权限拒绝，本轮未再重跑。
- 真实 NAS、真实数据库、真实媒体解码、真实发布/索引链路。
- 恶意并发目录替换（TOCTOU）、断电持久性。

### 5.1 本轮对测试代码的改动

只有一处，且**不是放宽**：`MediaPackageFailureTests.TargetRootReplacedByAFileAfterTheFirstHashIsRefused`
的计数语义（详见 §1）。`[TestMethod]` 仍为 **128**（本卡 **70**），`[Ignore]` 仍为 0，
**没有任何测试或断言被删除、skip 或放宽**；`MediaPackageSandbox`、其他测试文件与**全部生产代码**
本轮一字未改。

### 5.2 上一轮的分类更正（历史留痕，本轮未改动）

上一轮把「scope 到期」从 `target_unavailable` 更正为 `scope_changed`：
`ExpiredScopeIsRefusedAsTargetUnavailable` → **`ExpiredScopeIsRefusedAsScopeChanged`**，
并**新增**「不得报另一个码」的断言；`OfflineLibraryAvailabilityIsRefusedAsTargetUnavailable` 保持
`target_unavailable`。本轮**未再改动**这两个用例，其结论仍以协调实跑为准。

### 5.3 静态风格自查（支持性证据，不是格式门禁结论）

`.runtime/dsh-delivery/whitespace-selfcheck.py` 按 `.editorconfig` 静态检查本卡 19 个 C# 文件：
LF 行尾、文件末尾换行、无行尾空白、无制表符、缩进为 4 的倍数。

**实际输出（`whitespace-selfcheck-r4.txt`）**：19 个文件中 **7 个有发现**，**全部且仅有**
`max_line_length = 120` 超列：`MediaPackageInspectionContracts.cs`(58)、
`IsolatedMediaPackageInspector.cs`(18)、`MediaPackageBoundaryTests.cs`(16,231)、
`MediaPackageContractTests.cs`(21,405,406,407)、`MediaPackageFailureTests.cs`(16,366,688)、
`MediaPackageInspectionTests.cs`(15,159)、`MediaPackageSandbox.cs`(22,139,443)。
除超列外**没有**任何行尾/末尾换行/行尾空白/制表符/缩进发现（脚本退出码 1 仅由超列导致）。
这些长行是不可安全折行的 `[SuppressMessage]` `Justification`、XML 文档注释或单行断言消息文本。
**这不是全绿结论**；本轮未为凑指标改写既有文本，也未虚构任何豁免。真实的格式结论仍以协调的
`dotnet format --verify-no-changes` 为准。

## 6. 已编写测试覆盖（仅表示"已编写"，不代表已通过）

| 文件 | 用例数 | 覆盖 |
| --- | --- | --- |
| `MediaPackageContractTests.cs` | 22 | 夹具 LF 摘要、两个正例、17 个反例、原始字节摘要、键序无关、冻结词表与 unverified 清单、上限构造拒绝；固定 `Season 01`（错季/去季/自定子目录三态 + 原拼写仍可读）、孤立代理半对三处拒绝（真实原始字节 + 精确诊断）、合法成对与 UTF-8 等价（三个真实不同字节样本） |
| `MediaPackageInspectionTests.cs` | 7 | 单 P/多 P 正常路径、报告恒不授权 + `unverified` 完整、未授权零 I/O、`target_exists`、重复预检确定性、不创建目标目录 |
| `MediaPackageBoundaryTests.cs` | 11 | 原始路径拒绝/接受清单、大小写折叠碰撞、重复声明路径（**数组追加 + 重解析自证**）、`Contains`/`Overlaps` 前缀安全、逃逸拒绝、根重叠、链接根/链接文件拒绝、全分集离开 `Season 01` |
| `MediaPackageFailureTests.cs` | 30 | 空间不足、恰好余量、空间未知、源缺失、多余文件/目录、大小/哈希不符、运行中撤销 → `scope_changed`、离线/过期 scope、取消传播、超时、文件数/读取/清单预算、并发 `busy` 与释放、问题上限与截断、sink 内部有界、**首 hash 后目标根变文件（本轮修正其计数语义）**、报告不含绝对路径与内容 |

关于重复路径用例的实际断言（避免描述超出断言）：测试**重解析**变异后的字节，断言
`files` 数组中同时存在 `poster.png` 与 `Poster.PNG`，并断言重解析出的路径条数与变异后的数组条数
**相等**（`Assert.HasCount(files.Count, declaredPaths)`）。它证明的是「两条路径都真实存在且文档可解析」，
**不是**「比原夹具恰好多一条」这种未断言的更强说法。

链接（符号链接/重解析点）用例在机器无法创建链接时使用 `Assert.Inconclusive`，
不伪装成通过。协调实测 2 个条件 skip 即来自这里，**不记为通过**。

## 7. 结论

交付状态 **`needs_validation`**：测试编译、依赖与架构门禁已实际通过（生产构建与真实探针沿用上一候选的
真实结果，本轮无生产改动）；**测试未在标准宿主中执行**（协调第五次验收的 134 项结果针对上一版候选），
`dotnet format` 未取得真实结论，必须由协调方在固定候选后用真实命令
（`restore --locked-mode` / `build` / `test` / `format --verify-no-changes`）复验。
本卡不宣称任何测试通过。

