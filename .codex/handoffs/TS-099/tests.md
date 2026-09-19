# TS-099 测试与验证记录（R1–R6 修复轮）

本文件严格区分三类证据：**已实际执行并通过**、**已实际执行但被环境阻断**、**未执行**。
不得把编译通过读作测试通过，不得把被环境阻断读作断言失败。

## 0. 本轮（R1–R6）证据摘要

| 证据 | 结果 | 日志 |
| --- | --- | --- |
| 产品编译 Release | 退出 0，**0 警告 0 错误** | `.runtime/dsh-delivery/logs/build-r2-core.txt` |
| 测试编译 Release | 退出 0，**0 警告 0 错误** | `.runtime/dsh-delivery/logs/build-r2-tests.txt` |
| 静态自查：17 负例 + 2 正例 vs 冻结码 | **19/19 一致，0 mismatch** | `.runtime/dsh-delivery/logs/static-selfcheck.txt` |
| 交付补丁复现候选 | **15/15 文件字节与 SHA-256 完全一致** | `.runtime/dsh-delivery/logs/verify-patch-reproduction.txt` |
| 标准 `dotnet test` | **环境阻断，0 个测试运行** | `.runtime/dsh-delivery/logs/test-1.txt`（首轮）、`.runtime/dsh-delivery/logs/ts099-r2.trx`（本轮失败留痕） |
| 标准 `dotnet format --verify-no-changes` | **环境阻断** | `.runtime/dsh-delivery/logs/format-check.txt` |

**没有任何测试在本沙箱内运行过。本卡不宣称任何测试通过。**

### 静态自查是什么（以及不是什么）

`.runtime/dsh-delivery/logs/static-selfcheck.py` 用 Python **独立重写**了冻结语义（路径词法、形状与
精确键集、身份词表、模板匹配与基数、声明预算、大小写折叠碰撞、原始字节摘要、BOM/UTF-8/重复键/整数
词素），然后把 17 个反例与 2 个正例全部重放一遍，并额外校验服务端目标目录名。
它**能**发现 C# 实现与冻结语义的偏离（这正是运行它的理由），但它**不是**产品代码的执行证据：
它不是端到端预检，也不覆盖真实文件 I/O、枚举、预算执行、复核时序与报告装配。
输出：19/19 一致，0 mismatch。

### 补丁复现是什么

`.runtime/dsh-delivery/logs/verify-patch-reproduction.py` 在 `.runtime` 下重建 base 提交的文件状态，
用 `git apply --directory` 应用 `changes.patch`，再把产物与 `source-snapshot.json` 记录的字节长度与
SHA-256 逐一比对：**15/15 一致**。这证明交付补丁能逐字节复现候选，不证明行为正确。

## 1. 已实际执行并通过（沙箱内，本工作树）

| 命令 | 结果 | 日志 |
| --- | --- | --- |
| `dotnet restore AssetLibrary.slnx --locked-mode -p:NuGetAudit=false -m:1` | 退出 0，14 个项目还原 | `.runtime/dsh-delivery/logs/restore-locked2.txt` |
| 锁定包完整性核对（50 个在解内包） | 双向一致 50/50 | `.runtime/dsh-delivery/logs/lock-hashes.txt` |
| `dotnet build AssetLibrary.slnx -c Release --no-restore -m:1 -p:BuildProjectReferences=false` | 退出 0，**0 警告 0 错误**，15 个项目 | `.runtime/dsh-delivery/logs/build-slnx2.txt` |
| `dotnet build services/core-server/AssetLibrary.CoreServer.csproj -c Release` | 退出 0，0 警告 0 错误 | `.runtime/dsh-delivery/logs/build-core-*.txt` |
| `dotnet build tests/.../AssetLibrary.TransferOperation.Tests.csproj -c Release -m:1 -p:BuildProjectReferences=false` | 退出 0，0 警告 0 错误 | `.runtime/dsh-delivery/logs/build-tests-m4.txt` |
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
  **一个都没有在标准测试宿主中运行过**。
- `dotnet format --verify-no-changes` 的真实结论。
- 真实 NAS、真实数据库、真实媒体解码、真实发布/索引链路。
- 恶意并发目录替换（TOCTOU）、断电持久性。

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

交付状态 **`needs_validation`**：编译、静态自查、补丁复现、依赖与架构门禁已实际通过；
**测试未在标准宿主中执行**，必须由协调方在固定提交后用真实命令
（`restore --locked-mode` / `build` / `test` / `format --verify-no-changes`）复验。
本卡不宣称任何测试通过。
