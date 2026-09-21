# TS-099 测试与验证记录（第四次返修轮）

本文件严格区分三类证据：**已实际执行并通过**、**已实际执行但被环境阻断**、**未执行**。
不得把编译通过读作测试通过，不得把被环境阻断读作断言失败，不得把上一候选的实跑结果读作本轮结果。

## 0. 本轮（协调第四次验收后）证据摘要

协调第四次验收 `acd8eb6a9b4a77044021e2a6ba272d7249a38fd3` 的**真实**结果是
「132 项 129 过 1 失败 2 链接条件 skip，build/format/架构通过」。那**唯一失败**是本卡自己的测试构造
问题（重复路径夹具拼成非法 JSON）；另有**一项真实生产缺陷**（`Record` 先 Add 后判 cap，内部去重集合
无界增长）由协调静态与实测确认，本轮修好。

| 证据 | 结果 | 日志 |
| --- | --- | --- |
| 产品编译 Release | 退出 0，**0 警告 0 错误** | `.runtime/dsh-delivery/logs/build-r4-core-1.txt` |
| 测试编译 Release | 退出 0，**0 警告 0 错误** | `.runtime/dsh-delivery/logs/build-r4-tests-1.txt` |
| 真实程序集探针（本轮逐项复算） | **55 行记录**；sink 内外计数有界成立、目标根两场景与既有行全部成立 | `.runtime/dsh-delivery/logs/probe-run-44.txt` |
| 标准 `dotnet test`（窄过滤器，本轮一次） | **环境阻断，0 个测试运行** | `.runtime/dsh-delivery/logs/test-r4-narrow-1.txt` |
| 标准 `dotnet format --verify-no-changes` | **环境阻断**：`UnauthorizedAccessException` at `NamedPipeClientStream.TryConnect` | `.runtime/dsh-delivery/logs/format-r2-2.txt`、`format-r2-3.txt` |

**没有任何测试在本沙箱内运行过。本卡不宣称任何测试通过。**

### 0.1 上一轮 6 个失败的真实分类（按卡 4 第 35 行更正）

上一轮（协调第三次验收，候选 `22b5cb50`）的 6 个失败**不是全部测试问题**。本轮按报告如实分类：

| 类别 | 项 | 说明 |
| --- | --- | --- |
| **真实产品缺陷（1 项）** | 目标受信根被换成普通文件仍 `Inspected` | `TryObserve` 对普通文件返回 true，产品确实错了。由上一轮 R2 的 `TryObserveDirectory` + `RequireTargetParentDirectory` 修复，本轮**未改写**，协调第四次验收已确认真实通过 |
| **测试构造错误（4 项）** | 重复声明路径夹具是非法 JSON；孤立代理转义回归经 `JsonNode` 二次转义；合法对/UTF-8 对照未区分最终字节；键名场景断言任意码 | 均在测试侧，本轮修 2 项（重复路径、转义），另 2 项属同一转义用例的连带更正 |
| **邻接构造同步修正（1 项）** | 「少一字节」读预算基准 | 与同用例族的读预算基准取错**同源**，属邻接构造同步修正，**不是第七个失败** |

上一轮把 6 个失败一律写成「全部是测试构造/断言问题」**不实**，本轮已在 `summary.md`、`docs/handoffs/TS-099.md`
与本文件同步更正。

### 0.2 本轮真实程序集探针复算（`probe-run-44.txt`）

- **R1 有界诊断（本轮唯一生产改动）**：cap=1 时同一 `(code, location)` 记录两次 → 首次 `true`、
  重复 `false`、`IsTruncated=false`，内部 `issues=1`、`recorded=1`；再来一个**新的不同**对 →
  `false` 且 `IsTruncated=true`，内部仍各 1、`codes=1`；**10000 个不同 location** 后公开 1 条、
  内部 `issues=1`、`recorded=1`、`codes=1`。
- **R4 目标根读取中变文件（上一轮已修，本轮加正式回归）**：初始即普通文件 → `Rejected` / `unsafe_path` /
  `root_is_file=true` / 0 文件 0 字节 / `hasher_calls=0` / **不报 `target_exists`** / 无绝对路径；
  首文件 hash 后替换为普通文件 → `unsafe_path@target`、4 次真实 hash 后拒绝、**不报 `target_exists`**；
  根被删除仍 `target_unavailable`。
- **既有行**：两个正例与 17 个冻结反例、边界拼写、原始摘要规则、只读清单、10 个真实端到端预检继续成立；
  55 行记录中**无任何抛异常、无任何回显原文**。

### 0.3 真实程序集探针是什么（以及不是什么）

`.runtime/probe/verify/` 是**本卡私有的沙箱外工具**，不属于 31 文件白名单、不进交付候选。
它引用 `services/core-server/bin/Release/net10.0/AssetLibrary.CoreServer.dll` 与
`AssetLibrary.AssetLink.dll`，因此驱动的是**真实产品程序集**，不是 Python 重写版。
它**能**证明上述具体行为在真实程序集上成立；它**不能**替代标准测试宿主：它不运行 MSTest 的
`[TestInitialize]`/夹具生命周期，也不覆盖 70 个用例里其余仅依赖产品 API 的断言组合。
协调仍须在固定候选上实跑标准套件。

### 0.4 夹具不变性复核

对 `tests/dotnet/AssetLibrary.TransferOperation.Tests/Fixtures/MediaPackage/` 的 6 个文件：
磁盘 4 个为 CRLF、2 个为 LF，`HEAD` 存 LF；6/6 的 **LF 归一化字节与 `HEAD` 完全相同**
（`lf_equal=True`），5 个钉值 5/5 相符，`manifest.json` LF 摘要 `b523ba43…` 相符。
本轮**一字未改**任何夹具字节——包括那个曾经导致测试失败的 `examples.json`：
修的是测试怎么读它，不是它的内容。

## 1. 已实际执行并通过（沙箱内，本工作树）

| 命令 | 结果 | 日志 |
| --- | --- | --- |
| `dotnet restore AssetLibrary.slnx --locked-mode -p:NuGetAudit=false -m:1` | 退出 0，14 个项目还原 | `.runtime/dsh-delivery/logs/restore-locked2.txt` |
| 锁定包完整性核对（50 个在解内包） | 双向一致 50/50 | `.runtime/dsh-delivery/logs/lock-hashes.txt` |
| `dotnet build AssetLibrary.slnx -c Release --no-restore -m:1 -p:BuildProjectReferences=false` | 退出 0，**0 警告 0 错误**，15 个项目 | `.runtime/dsh-delivery/logs/build-slnx2.txt` |
| `dotnet build services/core-server/AssetLibrary.CoreServer.csproj -c Release` | 退出 0，0 警告 0 错误 | `.runtime/dsh-delivery/logs/build-r4-core-1.txt`（本轮） |
| `dotnet build tests/.../AssetLibrary.TransferOperation.Tests.csproj -c Release` | 退出 0，0 警告 0 错误 | `.runtime/dsh-delivery/logs/build-r4-tests-1.txt`（本轮） |
| 真实程序集探针（见 0.2） | 55 行记录，本轮逐项结论全部成立 | `.runtime/dsh-delivery/logs/probe-run-44.txt`（本轮） |
| `python -B tests/architecture/check_release_gates.py --target v0.1-start` | `RELEASE_GATE_ALLOWED: v0.1-start` | `.runtime/dsh-delivery/logs/release-gates.txt` |
| 仓库校验 12 步（见下） | 全部通过 | `.runtime/dsh-delivery/logs/verify-steps.txt` |
| 静态空白自查（支持性，见 3.3） | 19 个文件；7 个仅有 >120 列长行，其余规则无发现 | `.runtime/dsh-delivery/logs/whitespace-selfcheck-r4.txt` |

### 锁定依赖证据（回应协调方"复制缓存不作为可信性证明"）

复制来的包缓存本身不是证明。本卡对**每个**在解内锁定包做了两次独立核对：

1. `packages.lock.json` 的 `contentHash` == 缓存 `.nupkg.metadata` 的 `contentHash` → **50/50 一致**
2. `<id>.<version>.nupkg.sha512` == base64(SHA-512(`.nupkg` 字节)) → **50/50 一致**

脚本：`.runtime/dsh-delivery/logs/verify-lock-hashes.py`，输出 `.runtime/dsh-delivery/logs/lock-hashes.txt`。
说明：NuGet 的 `contentHash` 与 `.nupkg` 文件摘要**本来就是两个不同摘要**，早期误把二者直接比较得到的
"185 处不一致"是核对方法错误，不是包损坏；诊断过程记录在
`.runtime/dsh-delivery/logs/lock-hash-diagnosis.txt`。7 个 `apps/windows-client` 与
`tests/windows-{client,setup}` 的锁文件不属于本解决方案，未在本卡还原范围。

### 仓库校验通过的 12 步

`handoff`、`architecture-baseline`、`v0_1_alpha`、`dotnet-source`、`migration-tool-validate`、
`assetlink-sdk-generate-check`、`assetlink-sdk-source`、`assetlink-sdk-deps`、`web-deps`、`web-source`、
`native-theme-export`、`android-deps`。

其中与本次改动直接相关的两项：

- `validate_dotnet_source.py` → `.NET source policy passed`，
  即跨文件重复令牌块（≥60 token）与敏感日志模式检查通过。
- `validate_architecture_baseline.py` → `Architecture quality gate passed`，
  即层级依赖方向、跨模块公开层、禁止令牌（含 Domain 的 `system.io`）与循环依赖检查通过。

## 2. 已实际执行但被环境阻断（不是通过，也不是断言失败）

| 命令 | 现象 | 判定 |
| --- | --- | --- |
| `dotnet test ... --filter "FullyQualifiedName~MediaPackage"`（本轮，一次） | `vstest.console process failed to connect to testhost process after 90 seconds`，测试运行中止 | **测试从未运行**；VSTest 与其 testhost 之间的命名管道被沙箱拒绝。按协调指令**未重试** |
| `dotnet test ... -c Release --no-build --no-restore`（首轮） | 同上 | 同上 |
| `dotnet test ... --logger trx`（更早一轮） | testhost 立即中止：`Win32Exception (5): 拒绝访问` at `System.Diagnostics.ProcessManager.OpenProcess` | 沙箱拒绝父进程句柄，比命名管道更早失败 |
| `dotnet format AssetLibrary.slnx --verify-no-changes --no-restore` | `UnauthorizedAccessException` at `NamedPipeClientStream.TryConnect` | `MSBuildWorkspace` 的 build host 需要命名管道，被沙箱拒绝 |
| `python -m unittest discover -s tests/architecture` | 14 个用例 28 个 **error**，全部在 `setUp` 写临时目录时 `PermissionError` | 环境错误，非断言失败 |
| `python -m unittest discover -s tests/database` | 同上，`PermissionError` 于 `tempfile` 清理 | 环境错误，非断言失败 |
| `.runtime/probe/escape` 临时探针（本轮） | 两次无输出超时（600000ms / 300000ms） | **已按协调指令停止**；静态读取还发现它把 `examples.json` 根数组误当对象成员，故其失败**不是产品结论**，也不据此改夹具或 reader |

按协调指令：**不**申请提权、**不**改动 ACL 或全局配置、**不**改断言/skip/替换测试引擎、
**不**重启会话、**不**做全局 kill。

### 关于自建反射 runner（更早一轮，已停止，仅作受限留痕）

更早一轮曾自建 in-process 反射 runner（`.runtime/dsh-delivery/logs/ts099-inproc-runner/`）：
它跑到了真实执行路径，但 `AppContext.BaseDirectory` 指向 runner 输出目录，夹具搜索起点不对，
43 个用例的 39 个失败**不是产品缺陷**。按协调指令该路线已停止，两次原始日志保留为
`ts099-inproc-runner-probe-1830.txt` 与 `ts099-inproc-runner-run-1831.txt`。
**那 4 通过 / 39 失败一律不计入测试结论。**

## 3. 未执行

- 上述 `dotnet test`：**70 个本卡已编写测试方法**
  （`MediaPackageBoundaryTests` 11、`MediaPackageContractTests` 22、
  `MediaPackageFailureTests` 30、`MediaPackageInspectionTests` 7）
  **一个都没有在标准测试宿主中运行过**。本轮同样未运行。
  协调第四次验收给出的真实结果（132 项 129 过 1 失败 2 链接条件 skip）针对的是**上一版候选**，
  本轮改动**尚未**经过任何测试宿主。
- `dotnet format --verify-no-changes` 的真实结论。加载工作区即被命名管道权限拒绝，本轮未再重跑。
- 真实 NAS、真实数据库、真实媒体解码、真实发布/索引链路。
- 恶意并发目录替换（TOCTOU）、断电持久性。

## 3.1 本轮对测试代码的改动（2 项更正 + 2 个新回归）

1. **R2 更正**：`MediaPackageBoundaryTests.DuplicateDeclaredPathsAreRejectedByTheReader` 改为
   `JsonNode` 数组操作——保留原 `poster.png` 对象，向 `files` 数组**追加**一条完整的 `Poster.PNG`
   冲突对象（沿用原条目的 kind/size/sha），并在调用 reader 前**重解析**断言两条路径都在、
   数组长度只多一条。原写法把对象拼进**属性列表**得到
   `"path":"poster.png",{"cid":null,...`，是非法 JSON，reader 以 `invalid_manifest` 拒绝，
   所以 `duplicate_path` 断言必然失败。**产品未因此调整**。
2. **R3 更正**：`MediaPackageContractTests` 的转义回归改用 `MutateWithRawEscape`：先写入唯一 ASCII
   占位符 `zzRawEscapeSlotzz`，正常序列化，再在**最终字节**里把占位符替换成真实转义。原写法把文字
   `\uD800` 交给 `JsonNode` 再序列化，得到的是 `5c 5c 75 44 38 30 30`（转义反斜杠 + 字母 u），
   是普通字符串，根本没测代理。新用例**先断言字节形状**（占位符必须出现、替换后必须含单反斜杠转义），
   再断言精确诊断：`invalid_manifest` + `surrogate_escape`，且**不得**出现字段级码；孤立高半、孤立低半、
   连接写法 `\uD800XDC00` 各在 bvid、path、**属性名**三处覆盖。
   合法对/UTF-8/字面反斜杠现在是**三个真实不同的字节样本**，断言三者互不相同、都不报
   `surrogate_escape`、且合法对与 UTF-8 写法给出**同一**字段语义诊断。原始摘要按最终字节重算。
3. **R1 新回归**：`MediaPackageFailureTests.IssueSinkKeepsEveryInternalStoreBoundedAtTheCap`——
   cap=1 下重复对（count 1、不截断）、第二个不同对（count 1、截断）、10000 个不同 location 后
   内外集合仍 ≤1。内部计数用**只读反射**读私有 `issues` / `recorded`，因为只断言公开
   `Issues.Count` 不能证明内存有界；卡 4 明确禁止为此新增生产观测接口。
4. **R4 新回归**：`MediaPackageFailureTests.TargetRootReplacedByAFileAfterTheFirstHashIsRefused`——
   通过新增的 `MediaPackageSandbox.ComposeWithHasher` 包装**真实** `MediaPackageFileHasher`，
   首文件真实 hash 返回后删除空目标根并写入普通文件；真实 Inspector 最终必须拒绝 `unsafe_path`、
   不返回 `Inspected`、不伪称 `target_exists`、0 验证文件、位置保持相对且无绝对路径。
   破坏性步骤先自证只作用于本测试自己的沙箱（绝对路径且在夹具根下），仅用
   `.runtime/sandbox-storage/TS-099` 下的目录。

`[TestMethod]` 由 126 增至 **128**（其中 MediaPackage 相关 68 → **70**），`[Ignore]` 仍为 0，
**没有任何测试或断言被删除、skip 或放宽**；`MediaPackageFailureTests` 中「初始根是文件」的
拒绝断言**逐字保留**。

### 3.2 上一轮的分类更正（历史留痕，本轮未改动）

上一轮按 R5 卡文把「scope 到期」从 `target_unavailable` 更正为 `scope_changed`：
`ExpiredScopeIsRefusedAsTargetUnavailable` → **`ExpiredScopeIsRefusedAsScopeChanged`**，
并**新增**「不得报另一个码」的断言；`OfflineLibraryAvailabilityIsRefusedAsTargetUnavailable` 保持
`target_unavailable`。本轮**未再改动**这两个用例，其结论仍以协调实跑为准。

### 3.3 静态风格自查（支持性证据，不是格式门禁结论）

`.runtime/dsh-delivery/whitespace-selfcheck.py` 按 `.editorconfig` 静态检查本卡 19 个 C# 文件：
LF 行尾、文件末尾换行、无行尾空白、无制表符、缩进为 4 的倍数。

**实际输出（`whitespace-selfcheck-r4.txt`，逐条核对，不整绿宣称）**：19 个文件中 **7 个有发现**，
**全部且仅有** `max_line_length = 120` 超列：`MediaPackageInspectionContracts.cs`(58)、
`IsolatedMediaPackageInspector.cs`(18)、`MediaPackageBoundaryTests.cs`(16,231)、
`MediaPackageContractTests.cs`(21,405,406,407)、`MediaPackageFailureTests.cs`(16,366,688)、
`MediaPackageInspectionTests.cs`(15,159)、`MediaPackageSandbox.cs`(22,139,443)。
除超列外**没有**任何行尾/末尾换行/行尾空白/制表符/缩进发现（脚本退出码 1 仅由超列导致）。
这些长行是 `[SuppressMessage]` 的 `Justification` 字符串、XML 文档注释或单行断言消息这类
**不可安全折行**的文本；本轮**未**为凑指标改写既有文本。真实的格式结论仍以协调的
`dotnet format --verify-no-changes` 为准。

## 4. 已编写测试覆盖（仅表示"已编写"，不代表已通过）

| 文件 | 用例数 | 覆盖 |
| --- | --- | --- |
| `MediaPackageContractTests.cs` | 22 | 夹具 LF 摘要、两个正例、17 个反例、原始字节摘要、键序无关、冻结词表与 unverified 清单、上限构造拒绝；固定 `Season 01`（错季/去季/自定子目录三态 + 原拼写仍可读）、孤立代理半对三处拒绝（**本轮改为真实原始字节 + 精确诊断**）、合法成对与 UTF-8 等价（**本轮改为三个真实不同字节样本**） |
| `MediaPackageInspectionTests.cs` | 7 | 单 P/多 P 正常路径、报告恒不授权 + `unverified` 完整、未授权零 I/O、`target_exists`、重复预检确定性、不创建目标目录 |
| `MediaPackageBoundaryTests.cs` | 11 | 原始路径拒绝/接受清单、大小写折叠碰撞、重复声明路径（**本轮改为数组追加 + 重解析自证**）、`Contains`/`Overlaps` 前缀安全、逃逸拒绝、根重叠、链接根/链接文件拒绝、全分集离开 `Season 01` |
| `MediaPackageFailureTests.cs` | **30** | 空间不足、恰好余量、空间未知、源缺失、多余文件/目录、大小/哈希不符、运行中撤销 → `scope_changed`、离线/过期 scope、取消传播、超时、文件数/读取/清单预算、并发 `busy` 与释放、问题上限与截断、**sink 内部有界（本轮新增）**、**首 hash 后目标根变文件（本轮新增）**、报告不含绝对路径与内容 |

链接（符号链接/重解析点）用例在机器无法创建链接时使用 `Assert.Inconclusive`，
不伪装成通过。协调实测 2 个条件 skip 即来自这里。

### 本轮被更正用例的严格度说明

被更正的 2 处**没有一处是放宽**：

- 重复路径用例：从「非法 JSON 里找 `duplicate_path`」变成「合法 JSON、两条路径都真实存在、
  再断言 `duplicate_path`」，并且**新增**重解析与数组长度断言，检查更强。
- 转义用例：从「任意冻结码」变成「先断言最终字节含真实单反斜杠转义，再断言精确
  `invalid_manifest` + `surrogate_escape` 且无字段级码」，检查更强。

## 5. 结论

交付状态 **`needs_validation`**：编译、真实程序集探针、依赖与架构门禁已实际通过；
**测试未在标准宿主中执行**（协调第四次验收的 132 项结果针对上一版候选，本轮改动尚未经过任何测试宿主），
`dotnet format` 未取得真实结论，必须由协调方在固定候选后用真实命令
（`restore --locked-mode` / `build` / `test` / `format --verify-no-changes`）复验。
本卡不宣称任何测试通过。
