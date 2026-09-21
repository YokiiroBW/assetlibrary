# TS-099 测试与验证记录（R1–R7 第三次返修轮）

本文件严格区分三类证据：**已实际执行并通过**、**已实际执行但被环境阻断**、**未执行**。
不得把编译通过读作测试通过，不得把被环境阻断读作断言失败。

## 0. 本轮（协调第三次验收后）证据摘要

协调第三次验收 `22b5cb5028ad042c9bf6a76fe39106ae5ce1d5e2` 的**真实**结果是
「128 项 120 过 6 失败 2 链接条件 skip，build/format/架构通过」。那 6 个失败**全部是本卡自己的
测试构造/断言问题**，本轮逐条更正；产品侧的 5 项缺口（固定布局、目标根对象种类、JSON 解码、
去重与 cap 次序、公开端口越权）同时修好。

| 证据 | 结果 | 日志 |
| --- | --- | --- |
| 产品编译 Release | 退出 0，**0 警告 0 错误** | `.runtime/dsh-delivery/logs/build-r3-core-final.txt` |
| 测试编译 Release | 退出 0，**0 警告 0 错误** | `.runtime/dsh-delivery/logs/build-r3-tests-final.txt` |
| 真实程序集探针（本轮逐项复算） | **55 行记录**；本轮新增 6 项集节目录、2 项目标根、6 项解码、2 项 sink 全部符合预期 | `.runtime/dsh-delivery/logs/probe-run-42.txt` |
| 标准 `dotnet test` | **环境阻断，0 个测试运行** | `.runtime/dsh-delivery/logs/test-r2-1.txt`（首轮）、`test-1.txt` |
| 标准 `dotnet format --verify-no-changes` | **环境阻断**：`UnauthorizedAccessException` at `NamedPipeClientStream.TryConnect` | `.runtime/dsh-delivery/logs/format-r2-2.txt`、`format-r2-3.txt` |

**没有任何测试在本沙箱内运行过。本卡不宣称任何测试通过。**

### 0.1 本轮协调点名的 6 个失败：逐条更正（都在测试侧）

| # | 失败用例 | 原构造/断言的问题 | 本轮更正 |
| --- | --- | --- | --- |
| 1 | `MediaPackageBoundaryTests.DuplicateDeclaredPathsAreRejectedByTheReader` | 只在**原地**把 `poster.png` 改成 `Poster.PNG`，根本没有第二条条目，所以没有任何碰撞可查 | **保留**原条目并**新增**一条 `Poster.PNG` 条目（同 kind/cid/sha/大小），先 `Assert.Contains` 证明原条目仍在，再断言 `duplicate_path` |
| 2 | `MediaPackageFailureTests.CallerCancellationPropagatesInsteadOfBecomingAVerdict` | `Assert.ThrowsExactly<OperationCanceledException>` 过严：awaitable 实际抛**派生类** `TaskCanceledException` | 改为 `Assert.Throws<OperationCanceledException>`（接受派生类），并**新增** `Assert.IsTrue(cancelled.CancellationToken.IsCancellationRequested)` 强化「确实是取消」 |
| 3 | `MediaPackageFailureTests.ManifestByteBudget…` | 期望 `ResolveCalls == 1`，但清单字节预算在授权**之前**判定，端口根本不该被调用 | 改为 `Assert.AreEqual(0, composition.Scope.ResolveCalls)` |
| 4 | `MediaPackageFailureTests.RealReadBudgetExactlyCoveringThePackageIsEnough` | 只按 **video 单个文件**大小设预算，而额度是**跨整个包**消耗的 | 改用 `example.Payloads.Values.Sum(...)` 全包总字节 |
| 5 | `MediaPackageFailureTests.RealReadBudgetOneByteShortIsRefusedAsBudgetExceeded` | 同上，少 1 字节的基准取错 | 同样改为全包总和 − 1 |
| 6 | `MediaPackageFailureTests.EnumerationBudgetIsEnforcedWhileWalking` | 强制 `IssuesTruncated == true`；但 4096 枚举上限只产生**一条** `budget_exceeded`，没有任何诊断被丢弃 | 改为 `Assert.IsFalse(report.IssuesTruncated)`，并加注释说明枚举预算停止 ≠ 诊断被截断 |

**`MediaPackageFailureTests.cs:381`（目标受信根被换成普通文件）的拒绝断言原样保留**，未做任何放宽；
它此前失败是因为产品缺陷，本轮由 R2 的产品修复使其真正通过。

### 0.2 本轮真实程序集探针新增复算（`probe-run-42.txt`）

- **R1 固定布局**：真实多集夹具仍 `succeeded: true`；把**全部**集节目录改成 `Season 99/` →
  `invalid_file_set`（`files[0..4]` 逐条）；去掉 Season 目录 → 同样拒绝；自定子目录 →
  `invalid_path` + `invalid_file_set`。
- **R2 目标根对象种类**：初始目标根即普通文件 → `Rejected` / `unsafe_path` / `root_is_file=true` /
  0 文件 0 字节 / **不报 `target_exists`** / 无绝对路径；首文件 hash 后把根换成普通文件 →
  `unsafe_path@target`，4 次 hash 后拒绝；根被删除仍 `target_unavailable`（分类未被污染）。
- **R3 解码守卫**：`\uD800XDC00` 注入 bvid → `invalid_identity@bvid`；注入 path →
  `invalid_path`+`invalid_file_set`；注入**键名** → `invalid_manifest@manifest`；三者**零异常、原文零回显**。
  合法成对 `\uD83D\uDE00` 的转义写法与 UTF-8 写法给出**完全相同**的诊断；`\\uD800` 按普通字符串处理。
- **R4 去重与 cap**：cap=1 时同一 `(code, location)` 记录两次 → 首次 `true`、重复 `false`、
  `IsTruncated=false`；再来一个**新的不同**对 → `false` 且 `IsTruncated=true`；10000 个不同键仍只持有 1 项。

### 0.3 真实程序集探针是什么（以及不是什么）

`.runtime/probe/verify/` 是**本卡私有的沙箱外工具**，不属于 31 文件白名单、不进交付候选。
它引用 `services/core-server/bin/Release/net10.0/AssetLibrary.CoreServer.dll` 与
`AssetLibrary.AssetLink.dll`，因此驱动的是**真实产品程序集**，不是 Python 重写版。
它**能**证明上述具体行为在真实程序集上成立；它**不能**替代标准测试宿主：它不运行 MSTest 的
`[TestInitialize]`/夹具生命周期，也不覆盖 68 个用例里其余仅依赖产品 API 的断言组合。
协调仍须在固定候选上实跑标准套件。

### 0.4 夹具不变性复核

对 `tests/dotnet/AssetLibrary.TransferOperation.Tests/Fixtures/MediaPackage/` 的 6 个文件：
磁盘 4 个为 CRLF、2 个为 LF，`HEAD` 存 LF；6/6 的 **LF 归一化字节与 `HEAD` 完全相同**
（`lf_equal=True`），5 个钉值 5/5 相符，`manifest.json` LF 摘要 `b523ba43…` 相符。
本轮**一字未改**任何夹具字节。


`.runtime/probe/verify/` 是**本卡私有的沙箱外工具**，不属于 31 文件白名单、不进交付候选。
它引用 `services/core-server/bin/Release/net10.0/AssetLibrary.CoreServer.dll` 与
`AssetLibrary.AssetLink.dll`，因此驱动的是**真实产品程序集**，不是 Python 重写版。
本轮它复算了协调第二次验收列出的每一项具体反例与证据：

## 1. 已实际执行并通过（沙箱内，本工作树）

| 命令 | 结果 | 日志 |
| --- | --- | --- |
| `dotnet restore AssetLibrary.slnx --locked-mode -p:NuGetAudit=false -m:1` | 退出 0，14 个项目还原 | `.runtime/dsh-delivery/logs/restore-locked2.txt` |
| 锁定包完整性核对（50 个在解内包） | 双向一致 50/50 | `.runtime/dsh-delivery/logs/lock-hashes.txt` |
| `dotnet build AssetLibrary.slnx -c Release --no-restore -m:1 -p:BuildProjectReferences=false` | 退出 0，**0 警告 0 错误**，15 个项目 | `.runtime/dsh-delivery/logs/build-slnx2.txt` |
| `dotnet build services/core-server/AssetLibrary.CoreServer.csproj -c Release` | 退出 0，0 警告 0 错误 | `.runtime/dsh-delivery/logs/build-r3-core-final.txt`（本轮） |
| `dotnet build tests/.../AssetLibrary.TransferOperation.Tests.csproj -c Release` | 退出 0，0 警告 0 错误 | `.runtime/dsh-delivery/logs/build-r3-tests-final.txt`（本轮） |
| 真实程序集探针（见 0.2） | 55 行记录，本轮逐项结论全部成立 | `.runtime/dsh-delivery/logs/probe-run-42.txt`（本轮） |
| `python -B tests/architecture/check_release_gates.py --target v0.1-start` | `RELEASE_GATE_ALLOWED: v0.1-start` | `.runtime/dsh-delivery/logs/release-gates.txt` |
| 仓库校验 12 步（见下） | 全部通过 | `.runtime/dsh-delivery/logs/verify-steps.txt` |

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

- `validate_dotnet_source.py` → `.NET source policy passed (666 C# files scanned)`，
  即跨文件重复令牌块（≥60 token）与敏感日志模式检查通过。
- `validate_architecture_baseline.py` → `Architecture quality gate passed`，
  即层级依赖方向、跨模块公开层、禁止令牌（含 Domain 的 `system.io`）与循环依赖检查通过。

## 2. 已实际执行但被环境阻断（不是通过，也不是断言失败）

| 命令 | 现象 | 判定 |
| --- | --- | --- |
| `dotnet test ... -c Release --no-build --no-restore`（首轮） | `vstest.console process failed to connect to testhost process after 90 seconds`，测试运行中止 | **测试从未运行**；VSTest 与其 testhost 之间的命名管道被沙箱拒绝 |
| `dotnet test ... --logger trx`（本轮复跑一次） | testhost 立即中止：`Win32Exception (5): 拒绝访问` at `System.Diagnostics.ProcessManager.OpenProcess` → `Process.EnsureWatchingForExit` | **测试从未运行**；沙箱拒绝父进程句柄，比命名管道更早失败 |
| `dotnet format AssetLibrary.slnx --verify-no-changes --no-restore` | `UnauthorizedAccessException` at `NamedPipeClientStream.TryConnect` | `MSBuildWorkspace` 的 build host 需要命名管道，被沙箱拒绝 |
| `dotnet format <test project>.csproj ...` | 同上 | 同上 |
| `python -m unittest discover -s tests/architecture` | 14 个用例 28 个 **error**，全部在 `setUp` 写临时目录时 `PermissionError` | 环境错误，非断言失败 |
| `python -m unittest discover -s tests/database` | 同上，`PermissionError` 于 `tempfile` 清理 | 环境错误，非断言失败 |

已用探针定位根因（`.runtime/dsh-delivery/logs/` 内记录）：

- 沙箱允许在工作树 `.runtime` 下写文件（`WRITE OK`）。
- `tempfile.mkdtemp()` 能创建目录，但在其中**写文件**被拒绝
  （`PermissionError: ...\AppData\Local\Temp\dsh-926CZK\tmph36ygrvs\a.txt`）。
  把 `TMP`/`TEMP`/`TMPDIR` 指向工作树后，`unittest` 仍在清理阶段被拒。

按协调指令：**不**申请提权、**不**改动 ACL 或全局配置、**不**改断言/skip/替换测试引擎。
`dotnet test` 原始失败日志保留在 `.runtime/dsh-delivery/logs/test-1.txt`。

### 关于自建反射 runner（已按协调指令停止，仅作受限留痕）

为在本沙箱内取得真实执行证据，本轮曾自建一个 in-process 反射 runner（`.runtime/dsh-delivery/logs/
ts099-inproc-runner/`），它在进程内加载已编译测试程序集并直接调用 `[TestMethod]`。结论与处置：

- 它**确实**跑到了真实执行路径：43 个 MediaPackage 用例全部被调用，4 个通过、39 个失败。
- 失败的 37 个原因**不是产品缺陷**，而是 runner 自身的启动方式：`AppContext.BaseDirectory` 指向
  runner 的输出目录，夹具目录搜索起点因此不对（首轮报告里的
  `System.InvalidOperationException: The repository root could not be found.`）。
- 协调在 18:10 与 18:20 观察到两次 runner 重建/初始化探针各 600000ms 后 timed out 并伴随
  `FileNotFoundException` / `NativeCommandError`（MSBuild 节点与管道占用所致）。
- **按协调指令，这条自建 runner 路线已停止**：不再重建、不再运行、不重复 MSBuild/VSTest/IPC 受阻
  步骤、不提权、不改产品行为去迁就工具。两次完整原始日志已按原样保存：
  `.runtime/dsh-delivery/logs/ts099-inproc-runner-probe-1830.txt` 与
  `.runtime/dsh-delivery/logs/ts099-inproc-runner-run-1831.txt`。

**因此：上面那 4 通过 / 39 失败既不能算通过，也不能算产品的断言失败，本卡一律不计入测试结论。**

## 3. 未执行

- 上述 `dotnet test`：**68 个本卡已编写测试方法**
  （`MediaPackageBoundaryTests` 11、`MediaPackageContractTests` 22、
  `MediaPackageFailureTests` 28、`MediaPackageInspectionTests` 7）
  **一个都没有在标准测试宿主中运行过**。本轮同样未运行。
  协调第三次验收给出的真实结果（128 项 120 过 6 失败 2 链接条件 skip）针对的是**上一版候选**，
  本轮改动**尚未**经过任何测试宿主。
- `dotnet format --verify-no-changes` 的真实结论。加载工作区即被命名管道权限拒绝，本轮未再重跑。
- 真实 NAS、真实数据库、真实媒体解码、真实发布/索引链路。
- 恶意并发目录替换（TOCTOU）、断电持久性。

## 3.1 本轮对测试代码的改动

本轮测试侧改动全部是被协调点名的问题，另加本卡自身要求的两组回归用例：

1. **6 项失败修正**（逐条见 0.1）：`DuplicateDeclaredPathsAreRejectedByTheReader` 的真实第二条条目、
   取消断言接受派生类、`ResolveCalls == 0`、读预算改用全包总和（两个用例）、枚举预算停止不再强置截断。
2. **R1 回归**（卡 3 第 9 行要求 ContractTests 与 BoundaryTests 都要有）：
   `MediaPackageContractTests.MultipartEpisodeDirectoryIsTheFrozenSeasonDirectory` 与
   `MediaPackageBoundaryTests.MultipartEpisodeDirectoryIsRefusedWhenEveryEntryLeavesSeason01`，
   都覆盖「全部分集移至同一错误目录」与「全部分集去 Season 目录」。
3. **R3 回归**（卡 3 第 23 行要求）：
   `MediaPackageContractTests.EscapedSurrogateHalvesAreRefusedAsAManifestVerdict`（bvid/path/key 三处 × 三种写法）
   与 `LegalEscapePairsAndTheirUtf8SpellingAreTreatedAlike`（合法成对、UTF-8 等价、字面反斜杠对照）。

`[TestMethod]` 由 122 增至 **126**（其中 MediaPackage 相关 64 → **68**），`[Ignore]` 仍为 0，
**没有任何测试或断言被删除、skip 或放宽**。

### 3.2 上一轮的分类更正（历史留痕，本轮未改动）

上一轮按 R5 卡文把「scope 到期」从 `target_unavailable` 更正为 `scope_changed`：
`ExpiredScopeIsRefusedAsTargetUnavailable` → **`ExpiredScopeIsRefusedAsScopeChanged`**，
并**新增**「不得报另一个码」的断言；`OfflineLibraryAvailabilityIsRefusedAsTargetUnavailable` 保持
`target_unavailable`。本轮**未再改动**这两个用例，其结论仍以协调实跑为准。

### 3.3 静态风格自查（支持性证据，不是格式门禁结论）

`.runtime/dsh-delivery/whitespace-selfcheck.py` 按 `.editorconfig` 静态检查本卡 19 个 C# 文件：
LF 行尾、文件末尾换行、无行尾空白、无制表符、缩进为 4 的倍数——**全部通过**。
另有 7 个文件存在**超过 120 列**的长行（`max_line_length`），逐条查看后全部是
`[SuppressMessage]` 的 `Justification` 字符串、XML 文档注释或单行断言消息这类**不可安全折行**的文本，
且**没有一条**出现在协调的格式日志（`format.log`）里。因此这些长行不构成本轮结论，也**未**为凑指标
改写既有文本；真实的格式结论仍以协调的 `dotnet format --verify-no-changes` 为准。

## 4. 已编写测试覆盖（仅表示"已编写"，不代表已通过）

| 文件 | 用例数 | 覆盖 |
| --- | --- | --- |
| `MediaPackageContractTests.cs` | 22 | 夹具 LF 摘要、两个正例、17 个反例、原始字节摘要、键序无关、冻结词表与 unverified 清单、上限构造拒绝；R1 新增 10 个；**本轮新增 3 个**：固定 `Season 01`（错季/去季/自定子目录三态 + 原拼写仍可读）、孤立代理半对三处拒绝、合法成对与 UTF-8 等价 |
| `MediaPackageInspectionTests.cs` | 7 | 单 P/多 P 正常路径、报告恒不授权 + `unverified` 完整、未授权零 I/O、`target_exists`、重复预检确定性、不创建目标目录 |
| `MediaPackageBoundaryTests.cs` | 11 | 原始路径拒绝/接受清单、大小写折叠碰撞、重复声明路径、`Contains`/`Overlaps` 前缀安全、逃逸拒绝、根重叠、链接根/链接文件拒绝；R2 新增 2 个；**本轮**修正重复路径构造并**新增 1 个**全分集离开 `Season 01` 的边界回归 |
| `MediaPackageFailureTests.cs` | 28 | 空间不足、恰好余量、空间未知、源缺失、多余文件/目录、大小/哈希不符、运行中撤销 → `scope_changed`、离线/过期 scope、取消传播、超时、文件数/读取/清单预算、并发 `busy` 与释放、问题上限与截断、报告不含绝对路径与内容；R2/R3/R5 新增 9 个；**本轮更正 6 处**：取消断言接受派生类、`ResolveCalls == 0`、读预算两个用例改用全包总和、枚举预算停止不再强置 `IssuesTruncated` |

链接（符号链接/重解析点）用例在机器无法创建链接时使用 `Assert.Inconclusive`，
不伪装成通过。协调实测 2 个条件 skip 即来自这里。

### 本轮被更正用例的严格度说明

被更正的 6 处**没有一处是放宽**，逐条理由见 0.1：

- 取消断言由 `ThrowsExactly<OperationCanceledException>` 改为 `Throws<OperationCanceledException>`：
  原本要求**精确基类**，而框架自身抛派生类 `TaskCanceledException`，属于构造过严；同时**新增**
  `cancelled.CancellationToken.IsCancellationRequested`，实际检查更强。
- `ResolveCalls` 由 1 改 0：原值把「授权前拒绝」写成「已经授权过」，是断言与合同不符。
- 两个读预算用例改基准：原基准取单个文件大小，与「额度跨整包消耗」的合同不符。
- 枚举预算用例由 `IsTrue(IssuesTruncated)` 改 `IsFalse`：4096 枚举上限只产生一条 `budget_exceeded`，
  没有任何诊断被丢弃，原断言与产品合同不符；诊断截断另有专门用例证明。
- `MediaPackageFailureTests.cs:381`（目标受信根被换成普通文件）的拒绝断言**逐字保留**，
  由 R2 的产品修复使其真正通过，**未做任何放宽**。

## 5. 结论

交付状态 **`needs_validation`**：编译、真实程序集探针、依赖与架构门禁已实际通过；
**测试未在标准宿主中执行**（协调第三次验收的 128 项结果针对上一版候选，本轮改动尚未经过任何测试宿主），
`dotnet format` 未取得真实结论，必须由协调方在固定候选后用真实命令
（`restore --locked-mode` / `build` / `test` / `format --verify-no-changes`）复验。
本卡不宣称任何测试通过。

