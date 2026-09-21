# TS-099 测试与验证记录（R1–R7 第二次返修轮）

本文件严格区分三类证据：**已实际执行并通过**、**已实际执行但被环境阻断**、**未执行**。
不得把编译通过读作测试通过，不得把被环境阻断读作断言失败。

## 0. 本轮（R1–R7）证据摘要

| 证据 | 结果 | 日志 |
| --- | --- | --- |
| 产品编译 Release | 退出 0，**0 警告 0 错误** | `.runtime/dsh-delivery/logs/build-r2-core-4.txt` |
| 测试编译 Release | 退出 0，**0 警告 0 错误** | `.runtime/dsh-delivery/logs/build-r2-tests-3.txt` |
| 真实程序集探针（R1–R7 逐项复算） | 42 行记录，**17 个负例 `misses: []`、两个正例通过、6 个布局反例全部拒绝** | `.runtime/dsh-delivery/logs/probe-run-38.txt`、`.runtime/probe/verify/verify-results.json` |
| 夹具钉值与不变性 | 5/5 LF 归一化摘要相符；`manifest.json` LF 摘要 `b523ba43…` 相符；6 个夹具 LF 归一化后与 `HEAD` 逐字节相同 | 本轮以脚本独立复算（下节 0.3） |
| 标准 `dotnet test` | **环境阻断，0 个测试运行** | `.runtime/dsh-delivery/logs/test-r2-1.txt`（本轮）、`test-1.txt`（首轮） |
| 标准 `dotnet format --verify-no-changes` | **环境阻断**：`UnauthorizedAccessException` at `NamedPipeClientStream.TryConnect` | `.runtime/dsh-delivery/logs/format-r2-2.txt`、`format-r2-3.txt` |

**没有任何测试在本沙箱内运行过。本卡不宣称任何测试通过。**

### 0.1 真实程序集探针是什么（以及不是什么）

`.runtime/probe/verify/` 是**本卡私有的沙箱外工具**，不属于 31 文件白名单、不进交付候选。
它引用 `services/core-server/bin/Release/net10.0/AssetLibrary.CoreServer.dll` 与
`AssetLibrary.AssetLink.dll`，因此驱动的是**真实产品程序集**，不是 Python 重写版。
本轮它复算了协调第二次验收列出的每一项具体反例与证据：

- `r1_repository_root_discovery`：从测试二进制目录与当前目录都解析到仓库根，夹具目录 6 个文件。
- `r2_*`（6 项）：单集文件进子目录、单集 nfo 进子目录、未选 cid、错季、缺季、第二目录 → 全部 `invalid_file_set`。
- `positive_single` / `positive_multipart`：`succeeded: true`（多集正例在本轮修复前曾被误拒）。
- `negative_examples_exact_codes`：17 个负例经真实 reader 全部命中各自冻结码，`misses: []`。
- `path_policy_vocabulary` / `boundary_separator_decision`：17 拒绝 + 11 接受拼写 `misses: []`；
  `/` 嵌套路径在 Windows 解析成功且被包含、`\` 得 `Unsafe`、5 个逃逸全拒、末尾分隔符拒绝。
- `raw_digest_and_target_directory`：单集 `bilibili-BV0000000001-cid-101`、多集 `bilibili-BV0000000001`；
  CRLF 改写与键序重排都 `digest_mismatch`，重排后用自身摘要则接受。
- `validated_manifest_is_read_only`：`Files`/`SelectedParts` 都不能 Add（`NotSupportedException`）。
- `r5_bounded_diagnostics`：记 10000 次只报 1 条、`IsTruncated = true`、两个集合都不能转可变列表、回写被拒。
- `r6_surrogate_*`：值里的 `\uD800`、值里的 `\uDC00`、**键**里的 `\uD800` 都返回
  `invalid_manifest@surrogate_escape`，**零异常、原文零回显**。
- 10 个真实端到端预检（经 `MediaPackageInspectionService` + `IsolatedMediaPackageInspector` + 真实
  `MediaPackageFileHasher`）：`normal_files` 4 文件/2154 字节/不建目标；`multipart_nested` 8 文件/4170 字节；
  `unknown_space` 仅 `target_unavailable`；`target_created_during_read` → `target_exists` 且**无绝对路径**；
  `source_same_size_changed_after_read` → `source_changed`；`target_parent_removed_during_read` → `target_unavailable`；
  `missing_target` → `target_unavailable`；读预算少 1 字节 → `budget_exceeded`；刚好够 → `Inspected`。
- `report_shape`：单集 4/2154、多集 8/4170，`grants_file_operation` 恒 `false`，`unverified` 与冻结 7 项顺序一致。

它**能**证明这些具体行为在真实程序集上成立；它**不能**替代标准测试宿主：它不运行 MSTest 的
`[TestInitialize]`/夹具生命周期，也不覆盖 64 个用例里其余仅依赖产品 API 的断言组合。
协调仍须在固定候选上实跑标准套件。

### 0.2 上一轮的静态自查与补丁复现（历史留痕，本轮未重跑）

`.runtime/dsh-delivery/logs/static-selfcheck.py`（Python 独立重写冻结语义 → 19/19 一致）与
`.runtime/dsh-delivery/logs/verify-patch-reproduction.py`（15/15 字节复现）属于上一轮证据，保留原样，
**本轮未重跑**，也不作为本轮结论。

### 0.3 夹具不变性复核（本轮实际执行）

对 `tests/dotnet/AssetLibrary.TransferOperation.Tests/Fixtures/MediaPackage/` 的 6 个文件：

- 磁盘 4 个为 CRLF、2 个为 LF；`HEAD` 存 LF。
- 6/6 的 **LF 归一化字节与 `HEAD` 完全相同**（`lf_equal=True`），即**夹具正文一字未改**。
- 5 个被 `manifest.json` 钉住的文件，其 LF 归一化 SHA-256 与钉值 **5/5 相符**。
- `manifest.json` 自身的 LF 归一化 SHA-256 = `b523ba439ded3950e1ac3dcc483dcba3e3f90b4848f2e662377e770f3637db5f`（相符）。

## 1. 已实际执行并通过（沙箱内，本工作树）

| 命令 | 结果 | 日志 |
| --- | --- | --- |
| `dotnet restore AssetLibrary.slnx --locked-mode -p:NuGetAudit=false -m:1` | 退出 0，14 个项目还原 | `.runtime/dsh-delivery/logs/restore-locked2.txt` |
| 锁定包完整性核对（50 个在解内包） | 双向一致 50/50 | `.runtime/dsh-delivery/logs/lock-hashes.txt` |
| `dotnet build AssetLibrary.slnx -c Release --no-restore -m:1 -p:BuildProjectReferences=false` | 退出 0，**0 警告 0 错误**，15 个项目 | `.runtime/dsh-delivery/logs/build-slnx2.txt` |
| `dotnet build services/core-server/AssetLibrary.CoreServer.csproj -c Release` | 退出 0，0 警告 0 错误 | `.runtime/dsh-delivery/logs/build-r2-core-4.txt` |
| `dotnet build tests/.../AssetLibrary.TransferOperation.Tests.csproj -c Release` | 退出 0，0 警告 0 错误 | `.runtime/dsh-delivery/logs/build-r2-tests-3.txt` |
| 真实程序集探针（见 0.1） | 42 行记录，逐项结论全部成立 | `.runtime/dsh-delivery/logs/probe-run-38.txt` |
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

- 上述 `dotnet test`：64 个已编写测试方法
  （`MediaPackageBoundaryTests` 10、`MediaPackageContractTests` 19、
  `MediaPackageFailureTests` 28、`MediaPackageInspectionTests` 7）
  **一个都没有在标准测试宿主中运行过**。本轮同样未运行。
- `dotnet format --verify-no-changes` 的真实结论。本轮加载工作区即被命名管道权限拒绝，
  **R7 对 `MediaPackagePathBoundary.cs` 305–311 行的缩进修复未经格式工具确认**，须由协调核验。
- 真实 NAS、真实数据库、真实媒体解码、真实发布/索引链路。
- 恶意并发目录替换（TOCTOU）、断电持久性。

## 3.1 本轮对测试代码的改动

1. `MediaPackageBoundaryTests.cs` 里 `BoundaryAcceptsPosixNestedPathsAndRefusesEveryOtherSeparator`
   的一段**注释**（把「Windows 才把正斜杠当占用分隔符」的过时说明，改为「正斜杠在所有平台被接受并在
   真实访问前转 native 形式，反斜杠处处拒绝」）。该用例的**断言**本轮逐字未动——它们本来就是平台中立的。
2. `MediaPackageFailureTests.cs` 里被 R5 点名的两个用例（见 3.2）。

`[TestMethod]` 计数仍为 122（其中 MediaPackage 相关 64），`[Ignore]` 仍为 0，
**没有任何测试或断言被删除、skip 或放宽**。

### 3.2 R5 授权的断言更正（严格度提高，不是弱化）

R5 卡文明确：**目标父根缺失/离线/空间未知 → `target_unavailable`；scope 到期/撤权/版本改变 →
`scope_changed`**；上一轮把「到期」也判成 `target_unavailable` 并未被授权。因此随产品修正同步更正：

- `ExpiredScopeIsRefusedAsTargetUnavailable` → **`ExpiredScopeIsRefusedAsScopeChanged`**，
  断言 `scope_changed`，并**新增** `Assert.IsFalse(Codes(report).Contains("target_unavailable"))`。
- `OfflineLibraryAvailabilityIsRefusedAsTargetUnavailable` 保持 `target_unavailable`，
  继续断言不得报 `scope_changed`（存储可达与许可到期是两件不同的事）。

两处都**新增**了「不得报另一个码」的断言，严格度提高而非降低。

### 3.3 静态风格自查（支持性证据，不是格式门禁结论）

`.runtime/dsh-delivery/whitespace-selfcheck.py` 按 `.editorconfig` 静态检查本卡 19 个 C# 文件：
LF 行尾、文件末尾换行、无行尾空白、无制表符、缩进为 4 的倍数——**全部通过**。
另有 7 个文件存在**超过 120 列**的长行（`max_line_length`），逐条查看后全部是
`[SuppressMessage]` 的 `Justification` 字符串、XML 文档注释或单行断言消息这类**不可安全折行**的文本，
且**没有一条**出现在协调的格式日志（`format.log`）里——该日志只报 `MediaPackagePathBoundary.cs`
的 7 处 `WHITESPACE`。因此这些长行不构成本轮结论，也**未**为凑指标改写既有文本；
真实的格式结论仍以协调的 `dotnet format --verify-no-changes` 为准。

## 4. 已编写测试覆盖（仅表示"已编写"，不代表已通过）

| 文件 | 用例数 | 覆盖 |
| --- | --- | --- |
| `MediaPackageContractTests.cs` | 19 | 夹具 LF 摘要、两个正例、17 个反例、原始字节摘要、键序无关、冻结词表与 unverified 清单、上限构造拒绝；**R1 新增 10 个**：根级 `cid:null` 被接受、根级必须带 `cid` 键、三层未知键拒绝、单集模板精确（含 `video.mp4x` 这类后缀）、两种合法扩展名、多集 cid 前缀/集数映射/未选 cid、必需对象与每 cid thumb 基数、重复与多余对象冻结码、原始路径拼写先于规范化被拒、校验后集合为只读快照 |
| `MediaPackageInspectionTests.cs` | 7 | 单 P/多 P 正常路径、报告恒不授权 + `unverified` 完整、未授权零 I/O、`target_exists`、重复预检确定性、不创建目标目录 |
| `MediaPackageBoundaryTests.cs` | 10 | 原始路径拒绝/接受清单、大小写折叠碰撞、重复声明路径、`Contains`/`Overlaps` 前缀安全、逃逸拒绝、根重叠、链接根/链接文件拒绝；**R2 新增 2 个**：POSIX 嵌套路径在任意平台可解析且反斜杠一律拒绝、Windows 规范根不因分隔符被误拒且兄弟目录仍在外 |
| `MediaPackageFailureTests.cs` | 28 | 空间不足、恰好余量、空间未知、源缺失、多余文件/目录、大小/哈希不符、运行中撤销 → `scope_changed`、离线/过期 scope、取消传播、超时、文件数/读取/清单预算、并发 `busy` 与释放、问题上限与截断、报告不含绝对路径与内容；**R2/R3/R5 新增 9 个**：受信目标根缺失 → `target_unavailable`（且不得报 `source_missing`）、目标路径不可观察不得读作不存在、包根缺失 → `source_missing`（且不得报 `target_unavailable`）、读预算恰好够 → 通过、读预算少 1 字节 → `budget_exceeded`（不得报 `io_failure`）、枚举边消费边计数 → `budget_exceeded` + `truncated`、64 MiB headroom 是下限（0 与 -1 均抛）、headroom 不足 → `insufficient_space`、`Issue.Location` 只含已校验相对路径或固定字段位 |

链接（符号链接/重解析点）用例在机器无法创建链接时使用 `Assert.Inconclusive`，
不伪装成通过。

### 一处断言更正（R5 指定，非弱化）

R5 要求修正「Offline → `scope_changed`」这一错误分类，因此两个用例随产品修正同步更名并加强断言：
`OfflineLibraryAvailabilityIsRefusedAsTargetUnavailable` 与
`ExpiredScopeIsRefusedAsTargetUnavailable`，各自**新增**
`Assert.IsFalse(Codes(report).Contains("scope_changed"))`，同时证明必须报 `target_unavailable` 且不得报
`scope_changed`。其余 62 个测试方法的断言逐字未动；**没有任何断言被删除、skip 或放宽**。

## 5. 结论

交付状态 **`needs_validation`**：编译、真实程序集探针、依赖与架构门禁已实际通过；
**测试未在标准宿主中执行**，`dotnet format` 未取得真实结论，必须由协调方在固定候选后用真实命令
（`restore --locked-mode` / `build` / `test` / `format --verify-no-changes`）复验。
本卡不宣称任何测试通过。
