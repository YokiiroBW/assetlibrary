# TS-066 测试与验证记录

所有命令都在任务检出 `C:/YOKI/Codex/tianshu-peiban-bot/worktrees/TS-066/assetlibrary` 内、使用合成数据执行。
非零退出码均如实记录，缺证据项不记为通过。

## 1. .NET 与仓库门禁

| 命令                                                                            | 结果                                                                                                                                                                                                |
| ------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `dotnet format AssetLibrary.slnx --verify-no-changes --no-restore`              | 通过（首次发现 2 处格式问题：`DedupReportRegistry.cs` 空白、`DedupPorts.cs` 末行换行；返修轮又发现 `DedupReportWireTests.cs` 10 处 CRLF——该文件由 PowerShell `Set-Content` 写入——均修正后复验通过） |
| `dotnet build AssetLibrary.slnx --configuration Release --no-restore`           | 通过，0 警告 0 错误                                                                                                                                                                                 |
| `dotnet test AssetLibrary.slnx --configuration Release --no-build --no-restore` | 通过                                                                                                                                                                                                |

测试项目明细（通过 / 跳过 / 总计，**第五轮复验值**）：

- `AssetLibrary.AssetLink.Tests` 13 / 0 / 13
- `AssetLibrary.Build.Tests` 2 / 0 / 2
- `AssetLibrary.TaskHealth.Tests` 32 / 0 / 32
- `AssetLibrary.TransferOperation.Tests` 64 / 0 / 64
- `AssetLibrary.ReadCore.Tests` 110 / 25 / 135（含 TS065 的 49 项查重用例与第二轮新增 4 项增长/预算用例；跳过项均为既有 PostgreSQL/POSIX 条件用例）
- `AssetLibrary.Packaging.Tests` 63 / 0 / 63
- `AssetLibrary.WebGateway.Tests` 124 / 5 / 129（含本卡新增的 13 项线格式契约用例、6 项端点路由用例、第二轮的 3 项复核围栏用例与第四轮的 2 项注册表证据用例；第五轮把试运行的导出断言收紧到「复核后的版本与摘要」并新增被取代版本的 409 负例）
- `AssetLibrary.Preview.Tests` 111 / 24 / 135

合计 **519 通过 / 54 跳过 / 0 失败**（第五轮提交 `06c1882`）。

### 1.0.4 第五轮（`06c1882`）用例与实测

第五轮没有新增 .NET 用例，只把真实试运行的导出断言收紧：`DedupTrialIntegrationTests` 第 5 步先按
**复核后**的版本与摘要导出并断言文档自己声明的 `analysis_version`/`plan_digest` 与之相等、
`grants_file_operation=false`，再按**被取代的 `:1` 版本**导出并断言 409 `dedup_version_conflict`
（另加一条不存在的版本号）。`DedupTrialDriver` 新增 `DocumentPlanDigest` 读取导出文档自身的摘要。
真正的修复在页面侧，由 Chromium 用例覆盖（见第 2、3 节）与真实浏览器试运行验证（见第 5 节）。

### 1.0 第二轮新增用例

**`DedupRecheckFenceTests`（3 项，`AssetLibrary.WebGateway.Tests`）** —— 针对「复核扫描曾整个跑在
`heartbeat.CommitAsync` 内、持 Postgres 行锁遍历目录」这一阻塞。夹具 `DedupRecheckSyntheticLibrary`
在系统临时目录建一个两文件同内容（2048 B）的合成库；`DedupRecheckFenceDoubles.FenceProbeGuard`
实现 `IDurableTaskCommitGuard`，记录**提交次数**与**同时在开的事务数**；`DedupRecheckFenceFixture.ProbeDiscovery`
包装真实发现端口，记录「读取发生时的围栏是否打开」「读取次数」「是否被取消」。用例：
`ARecheckScansWithNoFenceHeldAndFilesOnlyItsVerdictUnderOne`（扫描期间围栏从未打开，结论在恰好一次围栏内落盘）、
`AVerdictIsRefusedInsteadOfFiledWhenTheVersionItVerifiedIsNoLongerRetained`（版本被丢弃后期望
`dedup_version_conflict`）、`ARecheckThatIsCancelledMidScanReadsNoFurtherAndFilesNothing`。

**`DedupGrowthBudgetTests`（4 项，`AssetLibrary.ReadCore.Tests`）** —— 针对「预算按扫描旧长度扣费、
`read_bytes` 不等于实际读取」这一阻塞：真实长度结算、整份计划超预算时无论批次布局都有界、
读取器返回超过许可字节时该答案失败且失败原因可见、`ReadBytes` 恒等于 `ContentVerified` 条目长度之和。

### 1.0.2 第三轮新增用例

**`DedupTrialIntegrationTests`（1 项，`AssetLibrary.WebGateway.Tests`，`[DoNotParallelize]`）** ——
真实栈上的查重工作台：真实临时 PostgreSQL 集群 + 真实迁移 + 真实 HTTPS Core + 真实管理员会话 +
组合根自己的查重服务与托管 worker。逐项断言：六个操作的未登录调用全部 401 `authentication_required`
且答案不描述被拒请求；`start` 受理即活动态且无可读报告；轮询期间至少两次观测到仍处于活动态；
`results` 自报版本、重复组含**两个成员**、`total>0`；`revalidate` 先回执 `pending` 再 `completed`
且 `plan_still_current=true`；`export` 版本一致、`grants_file_operation=false`，换版本导出 →
409 `dedup_version_conflict`；`cancel` 记到持久任务上且**不产生报告**；重启后同一 `task_id`
仍可读而报告不可读（`results` → `dedup_report_not_retained`、`groups` 空、`total=0`，
`revalidate` → 404）；结束时逐文件比对 SHA-256 与 mtime 证明合成源未变。
（第四轮已把租约、取消与重启三段换成更严的断言，见 1.0.3；本节保留第三轮口径备查。）

**`DedupRecheckFenceTests` 新增 1 项（共 4 项）** ——
`ALateVerdictNeverReplacesANewerAnalysisOfTheSameLibrary`：**旧 key 仍保留**（不是删除旧 key 的场景），
在复核扫描中途发布更新的一次分析，复核必须在落盘前被拒（`completed=false`、
`dedup_version_conflict`），`latest` 仍指向新任务，旧版本仍可读。反向探针确认该用例会咬住旧代码：
把 `File` 换回上一轮的非原子写法后立即失败
（`Assert.AreNotEqual 失败。应为: <6499ee26-…> 以外的任意值，实际为: <6499ee26-…>`）。

**`TrialOperatorBootstrap`（共享夹具助手）** —— 两个真实试运行原先各自复制一份「初始化保护算子密钥 +
引导首个管理员」的 60 token 代码块，被 `scripts/validate_dotnet_source.py` 判为重复；抽成一处后
`.NET source policy passed`。

### 1.0.3 第四轮新增用例

**`tests/web/row-window.spec.mjs`（3 项，Chromium）** —— 行窗口在**闲置**时不得持续渲染：

| 用例                                                       | 断言                                                                                                                                                     |
| ---------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `an idle asset list renders once and stays still`           | 100 项资产列表加载完成后 `waitForTimeout(1200)`，console 与 `pageerror` 中 `Maximum update depth exceeded` **0 次**；窗口化渲染仍生效（可视行数 < 总数） |
| `an idle library list renders once and stays still`         | 80 个资源库列表同上，且 `role=list` 的「资源库列表」仍在                                                                                                 |
| `the asset window follows a scroll, a resize, a view change and the keyboard` | 滚动（`scrollTop=1200`）、缩放（1024×700）、列表↔网格切换、`End` 键后窗口与焦点都跟随；全程无递归更新                                  |

反向探针：把 `useRowWindow` 的比较改回「恒假 + `[getScrollElement]` 依赖」后 3 项全部失败；
协调者探针 `.runtime/review-window.spec.mjs` 从 `IDLE_MAXIMUM_UPDATE_DEPTH_ERRORS=7` 变为 **0 并通过**。

**`DedupTrialLeaseReader`（试运行助手，只读）** —— 以模块自身的 `ModulePostgresSession(TaskHealth)`
读 `task_health.durable_task` 的 `state/lease_owner/lease_generation/heartbeat_at/lease_until/
cancellation_requested_at`。`WaitForClaimAsync` 等到 `leased` 且 `lease_generation>0`（失败即报错），
`ObserveWhileActiveAsync` 每 25 ms 采样直到任务settle或窗口关闭。**产品侧没有任何测试钩子**，
读到的是 `heartbeat_durable_task` 真实写下的列。

**`DedupTrialIntegrationTests` 第四轮的收紧**（同一个用例内，替换第三轮对应段落）：

- 租约续期：认领时记录 `lease_until`，在**仍为 `leased`** 的采样中要求至少两次，且
  `heartbeat_at` 前进、`lease_until` 越过认领窗口、`cancellation_requested_at` 为空。
  实测 `heartbeat_at` 由 `…26.700` 前进到 `…27.247`（约 110 ms 步进，心跳间隔设为 100 ms）。
  第三轮的「终态 `updated-created > 400 ms`」不再作为续期证据。
- 取消运行中任务：第二个操作键 `start` → 确认 `leased` 且尚未请求取消 → `cancel` →
  终态**必须** `cancelled`（`succeeded` 判失败）→ 无报告 → 回读持久行确认 `cancellation_requested_at` 非空。
- 重启：先断言重启前报告可读且 `total>0`，重启后断言同一 `task_id`、状态不变、
  `report_available=false`、`results` → `dedup_report_not_retained`、`revalidate` → 404。
- 合成库隔离：`DedupTrialAssets` 现在为**两个**库各写一份填充（`dedup-library` 与
  `dedup-browser-library`），因为注册会拒绝重叠根，而浏览器段落需要一个能真正重新开始分析的库。
- 填充由 16×16 MiB 改为 32×63 MiB（单文件严格小于 64 MiB 上限）：本机文件缓存会让 256 MiB
  在 130 ms 内读完，比一个心跳间隔还短，租约续期就无从观察。

**`tests/integration/read-only-trial/dedup-browser.mjs` + `DedupTrialBrowser` + `TrialBrowserProcess`** ——
真实 Chromium 页面闭环：SPKI 固定宿主证书、真实管理员口令登录、页面自身点击完成
登录 → 选库 → 开始分析 → 状态/结果（重复组两名成员、开合详情后焦点回到该行）→ 导出计划
（文档 `grants_file_operation=false`、版本与页面所报一致）→ 重新核对（`来源未变化`）→
第二个库开始分析并在运行中取消（页面「已取消」+「本次分析没有可读取的结果」）。
`TrialBrowserProcess` 是**两个**试运行共用的进程外壳（原先只有浏览试运行自己一份，抽取后
`validate_dotnet_source.py` 的 60-token 重复块检查通过）。
`run_e2e.py` 新增 `--trial` 聚焦开关与每个试运行的 `deadline_seconds`。

**`DedupReportRegistryEvidenceTests`（2 项，`AssetLibrary.WebGateway.Tests`）** —— 钉住注册表两个
「比较并写入」步骤各自回答什么，因为调用方用同一个 `Filed` 标志读两个不同的问题：

| 用例                                                     | 断言                                                                                                                                                     |
| -------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `EvidenceAgainstTheCurrentVersionIsStoredAndNamesThatVersion` | `StoreEvidenceIfCurrent` 在**成功**时返回 `Filed=false` 但 `Key` 指向被写入的那一版（这正是它与 `Superseded` 的区别），当前版本不移动，证据可读回 |
| `EvidenceAgainstAVersionTheLibraryMovedOnFromIsRefused`  | 库已前移到更新的分析后，返回 `Superseded`（`Key=default`），旧版本上**没有**任何证据                                                                     |

反向探针：把 `StoreEvidenceIfCurrent` 的返回值改成 `new FileOutcome(true, expected)` 后第一项立即失败
（`Assert.IsFalse 失败。'condition' 表达式:'outcome.Filed'`），改回后 2 项全绿。
核实记录：`DedupRecheckRunner.File` 里 `result.CurrentPlan is null` 那条分支在可达路径上不会执行——
`DedupPlanPolicy.Recount` 恒把非空 `current` 放进 `DedupRecountResult`（全仓仅此一处构造该记录），
因此生产中的 `!outcome.Filed` 只可能来自 `FileIfCurrent` 的版本比较。据此**不改签名**（不扩大重构）。

### 1.0.1 第一轮（`2d890e7`）用例明细，保留备查
- `AssetLibrary.ReadCore.Tests` 106 / 25 / 131
- `AssetLibrary.WebGateway.Tests` 118 / 4 / 122

合计 509 通过 / 53 跳过 / 0 失败。

### 1.1 线格式契约用例（13 项：`DedupReportWireTests` / `DedupExportWireTests` / `DedupRefusalWireTests` / `DedupRecheckWireTests`）

浏览器套件跑的是手写夹具，所以「Host 真正序列化出来的键」与「页面解码器要读的键」之间原本没有任何门禁。
用例直接调用真实 Host 构造器（`TrialDedupJson` / `TrialDedupPageJson` / `TrialDedupExportJson`），
序列化后再解析，逐键断言页面解码器 `apps/web/src/dedup/dedupResponses.ts` 要求的**完整键集合**与 JSON
类型，并断言：

- `failure_code` / `performed_at` / `previous_plan_digest` 这类可空字段**以显式 null 出现**，不是缺键；
- `source_failures` 是对象数组（`source_id` + `reason_code`），不是裸字符串；
- 报告版本以 `任务 id:代数` 绑定，页面后续每次调用都带这个值；
- 导出的计划文档自带 `grants_file_operation = false` 与只读声明；
- 全部数字都是整数且落在 JavaScript 安全整数范围内（2^53−1），`maximum_bytes` 仍是 JSON 数字而不是字符串；
- 每个拒绝码映射到调用方可处理的状态码（400/403/404/409/503），且带可读中文文案；
- 返修轮新增的 `job.limits` 子对象逐字段存在且与受理值一致（预算只有一个权威来源）；
- 返修轮新增的复核回执：`state`/`state_text`/`recheck_task_id` 必在，`pending` 时 `completed=false` 且
  **不出现**任何「没有变化」结论，`refused` 时明示未执行——「未完成」与「没有变化」不可能被读成同一件事。

这些用例的**有效性经过反向探针确认**：把 `TrialDedupJson.Job` 里的 `analysis_version` 临时改名为
`analysis_ver`，用例立即失败并打印完整键集合（`keys: task_id,...,analysis_ver,...`）；改回后恢复通过。
因此这不是一组恒真的断言，而是真正锁住了 C# ↔ TypeScript 之间唯一没有被其他门禁覆盖的接缝。

### 1.2 端点路由与只读边界用例（`DedupEndpointRoutingTests`，6 项）

`TrialWebEndpoints.Configure` 把六个操作注册在既有 `/assetlink/v1` 面内，而浏览器套件用的是夹具拦截，
所以「路由真的存在」这件事原本没有门禁。这组用例在环回地址上以**真实 HTTPS**（自签证书 + 测试信任回调）
启动一个最小宿主并挂上与试运行宿主相同的 `TrialDedupEndpoints.Map`，然后：

- 逐个 POST 六个操作路径，断言 **403 `permission_denied`**（而不是 404）——路径存在且被守卫；
- 断言未知路径 `/assetlink/v1/dedup/analyse` 返回 404，GET `/status` 返回 405——查询绝不能启动或改变分析；
- 直接对共享读取器 `AssetLinkRequestBody` 断言：`Content-Length` 超 64 KiB 抛
  `RequestBodyTooLargeException`（对应 413）、流式超限同样抛错、非 JSON 内容类型抛
  `UnsupportedRequestMediaTypeException`（对应 415）；
- **按操作而不是按端点校验 `task_id`**（返修项 2）：`start` / `cancel` 用浏览器真实发送的载荷（只带
  `library_id` / `operation_key` / `retry`）必须受理，只有在这些操作**显式**带上 `task_id` 时才进入报告寻址；
  `status` / `results` / `revalidate` / `export` 缺少 `task_id` 必须被拒。

同样经过反向探针：把 `Operations` 里的 `export` 临时改名为 `export_plan`，用例立即以
`Forbidden 应为 / NotFound 实际` 失败；改回后恢复通过。生产文件已确认与提交版本一致（`git diff` 为空）。

这组用例只覆盖到授权之后的那一步：认证中间件依赖数据库账号存储（`GatewayAuthenticationRuntime`），
本机不可用，因此「已登录管理员在环回地址上走通六个操作」这一条仍未在任何门禁中执行，如实记录在
第 5 节。

| 命令                                                                          | 结果                                                                                  |
| ----------------------------------------------------------------------------- | ------------------------------------------------------------------------------------- |
| `python -I -B scripts/verify_repository.py`                                   | `Repository verification passed (M0-009 fast architecture gate).`（含 14 项架构测试） |
| `python -I -B scripts/validate_dotnet_source.py`                              | `.NET source policy passed (636 C# files scanned).`                                   |
| `python -B tests/architecture/check_release_gates.py --target v0.1-start`     | `RELEASE_GATE_ALLOWED: v0.1-start`                                                    |
| `python -I -B scripts/validate_web_source.py`                                 | `Web read-only source boundary passed.`                                               |
| `python -I -B scripts/validate_web_dependencies.py --require-build-artifacts` | `Web dependency, lock, and license policy passed with build budgets.`                 |

构建过程中被门禁拦下并**真实修复**的问题（不是绕过）：

1. `validate_dotnet_source.py`：`ReadOnlyAssetLinkEndpoints.cs` 与 `TrialDedupRequest.cs` 的 60 token
   重复块 → 抽出 `Adapters/AssetLink/AssetLinkRequestBody.cs`，两处共用同一实现。
2. `validate_dotnet_source.py`（返修轮）：新增的复核载荷与 Host 复核线格式之间出现 60 token 重复块
   （两者都是「枚举 → 线名」的分支列表）→ Host 侧改为 `Dictionary<DedupRecountStatus, string>` 查表，
   与模块内 `DedupJobWire` 的 switch 不再逐 token 相同，两处语义保持一致。
3. `validate_web_source.py`：新增文件里出现过第二处 `fetch(`、`sessionStorage` 与 `console.log` →
   删除 `dedupStorage.ts`，六个操作改为经唯一 `AssetLinkClient` 适配器发送；浏览器不再保存任务标识，
   恢复语义改由服务端幂等任务身份承载。
4. `validate_web_source.py`（返修轮）：`useDedupJob.ts` 达到 20060 B，超单文件 20000 B 复核上限 →
   去掉已无消费者的 `onRecheck` 回调（页面改为直接读 `dedup.recheck`，句柄即唯一所有者），现为 19898 B。
5. `dotnet build`：`CA1506`（组合根/HTTP 面类型计数）、`ASP0016`、`CS0122`、`CS8754` 等按仓库既有做法修复
   （`SuppressMessage` 带 `Justification`、lambda 转 `Delegate`、公开同一程序集内被 Host 读取的读取器）；
   返修轮另有 `CA1506` 落到 `DedupJobWorker`（分析与复核两种载荷的并集：带理由抑制）与
   `TrialDedupReportJson`（拆出 `Paths`/`StateName`/`StateText` 后消解）。

## 2. Web 构建与浏览器回归

| 命令                                                              | 结果                                                                                                                                                             |
| ----------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `prettier --check apps/web/src tests/web`                         | `All matched files use Prettier code style!`                                                                                                                     |
| `tsc --project apps/web/tsconfig.json --noEmit`                   | 通过（等同 `pnpm run lint` / `typecheck`）                                                                                                                       |
| `vite build`（工作目录 `apps/web`）                               | 通过（第五轮）：入口 `index-*.js` 274777 B + `DedupWorkbench-*.js` 35172 B + `LibraryAdmin-*.js` 7947 B = **317896 B**（预算 327680 B）、`index-*.css` 32394 B（预算 32768 B） |
| `playwright test --config apps/web/playwright.config.mjs`（全量） | **92 通过 / 0 失败**（本卡 23 项；进程退出码按本仓惯例仍为 1，判据是 `92 passed` 行）                                                                             |
| 本卡用例 `--repeat-each 2 --workers 2`                            | 22 通过 / 0 失败（第三轮稳定性复验；第五轮改跑全量并单独重跑本卡用例）                                                                                            |

`pnpm` 未在本机 PATH 上，浏览器门禁以固定版本 `node v24.20.0` 直接调用仓库内
`apps/web/node_modules/@playwright/test/cli.js`、`typescript/bin/tsc`、`vite/bin/vite.js` 与
`prettier/bin/prettier.cjs`，等价于 `ci-tiers.json` 中 `pnpm --dir apps/web run …` 的对应命令。

## 3. 本卡浏览器用例（`tests/web/dedup-workbench.spec.mjs`，第五轮 23 项）

第五轮新增/收紧的 6 项（其余 17 项与 3 项 `row-window.spec.mjs` 用例保持通过）：

| 用例                                                             | 断言要点                                                                                                                                                          |
| ---------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 复核完成后页面切到它落下的版本，导出写的就是该版本               | `results` 恰好被读 2 次（`:1` 与 `:2`）；复核按 `:1` 的摘要提交；导出请求体是 `:2` 的版本与新摘要；页面显示新版本                                                    |
| 页面首次轮询前就已完成的复核，在第一次轮询即被应用               | 复核完成先于页面的 `revalidate` 轮询时，页面仍切到新版本并重读，不显示成「没有变化」                                                                                |
| 被拒的复核不写版本，页面保留自己持有的版本并仍可导出             | `refused` 回执不改变版本；导出仍按屏上那一版成功                                                                                                                  |
| 服务器已更新版本但页面还在读取时，导出被禁用                     | `holdAnswers(state, ["results"])` 卡住读取：禁用态与说明同时出现，读取完成后恢复可用                                                                                |
| 被取代版本的导出由服务端拒绝时如实报错，不悄悄改写绑定           | 409 `dedup_version_conflict` 原样呈现；页面不重试、不换版本                                                                                                        |
| 对已离开的库的迟到答案不写进当前打开的库                         | 切库后到达的旧响应不改变新库的状态、版本与导出绑定                                                                                                                |


| #   | 用例                                         | 断言要点                                                                                                                        | 截图                                                                          |
| --- | -------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------- |
| 1   | 未开始分析时说明范围与预算                   | 标题/只读说明/仅本库说明；按钮在未选库时禁用；未发出任何请求；**首屏不出现任何预算数字**（服务端尚未受理，页面不得自造）        | `dedup-idle-1440.png`                                                         |
| 2   | 开始分析显示服务端自己的状态，不伪造结果     | 状态「等待执行」；请求体只有 `library_id`/`operation_key`/`retry`；整套请求不含 `C:/`；改为 leased 后显示「正在分析」           | `dedup-scanning-1440.png`                                                     |
| 3   | 同一资源库再次运行时重申服务端已受理的预算   | 第二次 `start` 的 `maximum_files`/`maximum_bytes`/`maximum_file_bytes` 与受理值逐一相等（预算只有一个权威来源）                 | —                                                                             |
| 4   | 完成的分析分开列出组、计数与证据             | 结果版本、默认 tab、组行、证据文案、本次不可读、范围说明；`results` 只发一次且带 `task_id`                                      | `dedup-results-1440.png`                                                      |
| 5   | 打开组加载成员、说明证据、关闭回到原行       | 详情标题获得焦点；成员含 `holiday/beach.png`；`group_key` 正确；关闭后详情消失且原行重新获得焦点                                | —                                                                             |
| 6   | 切换区块把版本带进 URL                       | `section=unverified`；最后一次 `results` 的 `kind` 为 `Unverified`                                                              | —                                                                             |
| 7   | 过期的计划被如实报告，不会被当成当前版本导出 | `PlanStale` 文案、变化/消失/新增计数、`plan_digest` 正确、**没有**发出导出请求                                                  | —                                                                             |
| 8   | 未完成的复核不会被显示成「没有变化」         | 首次 `revalidate` 不带 `recheck_task_id`（只受理），随后按回执自己的 `recheck_task_id` 轮询；`pending` 期间不出现「无变化」结论 | —                                                                             |
| 9   | 被服务端拒绝的复核明示「没有执行比较」       | 服务端 `refused` 原样呈现，且不得出现「相同」                                                                                   | —                                                                             |
| 10  | 取消只针对分析本身                           | 「已请求取消，仅取消分析本身」；取消按钮禁用；请求体键为 `library_id` + `operation_key`                                         | `dedup-cancelled-1440.png`                                                    |
| 11  | 不完整的扫描说明自身限制，预算证据与重复分开 | 计划文案、达到预算上限后停止、本次不可读、实际读取量；不显示成功结论                                                            | `dedup-incomplete-1440.png`                                                   |
| 12  | 宽布局下详情在列表右侧，关闭后焦点回原行     | 详情块 x 在列表右边界之外，宽度取整为 360                                                                                       | `dedup-detail-1440.png`                                                       |
| 13  | 报告被丢弃时如实说不可读，不显示成没有重复   | 「本次结果已不可读取」；不出现重复组                                                                                            | —                                                                             |
| 14  | 导出被拒是权限失败而不是空计划               | 403 文案；只发出一次导出请求                                                                                                    | —                                                                             |
| 15  | 窄屏、深色与减少动态下堆叠且无横向溢出       | 1024 / 390（深色）/ 320 三档：`scrollWidth <= innerWidth`，行可见；移动列 `select` 高度 ∈ [44, 46]                              | `dedup-1024.png`、`dedup-390.png`、`dedup-320.png`                            |
| 16  | 首屏概览在桌面与手机宽度下未滚动拍摄         | 1440/390/320 三档 `scrollTo(0, 0)` 后截图；390/320 断言 `select` 高度 ∈ [44, 46]；复核文案可见                                  | `dedup-overview-1440.png`、`dedup-overview-390.png`、`dedup-overview-320.png` |
| 17  | 长中文名称保留完整值，行序与键盘顺序可用     | 完整名称可读、Tab 顺序稳定、Enter 可打开组                                                                                      | —                                                                             |

截图存放于 `.runtime/dedup-frontend-evidence/`（12 张，由用例在断言通过后 `animations: "disabled"` 拍摄；
概览三张为未滚动首屏，其余为 `fullPage`）。该目录是运行产物，不入提交；用例本身带截图语句，重跑即可复现。

## 3.1 构建体积实测

`validate_web_dependencies.py` 对 `dist/assets/*.js` **全量求和**，因此延迟加载只把字节从入口分包搬到
异步分包，总和不变。下列数字来自 `vite build` 后的 `dist/assets` 实测（工作目录 `apps/web`）：

| 拆法                                                    | 入口分包   | 异步分包                                     | 求和       | 与 327680 预算的差 |
| ------------------------------------------------------- | ---------- | -------------------------------------------- | ---------- | ------------------ |
| 全部静态（`a3edff5` 的状态）                            | 336165     | —                                            | 336165     | +8485              |
| 工作台路由级动态导入                                    | 305162     | `DedupWorkbench` 34458                       | 339620     | +11940             |
| ＋资源库管理页动态导入（第一轮 `e492d28`）              | 297973     | `DedupWorkbench` 34415 + `LibraryAdmin` 7959 | 340347     | +12667             |
| ＋复用既有响应原语/失败助手、关闭 modulePreload（`eacf26e`） | **297503** | `DedupWorkbench` **33876** + `LibraryAdmin` 7959 | **339338** | **+11658**         |
| 再拆叠加层与条目视图（已试并**撤回**）                  | 297973     | 更多碎片，总额 342185                        | 342185     | +14505             |

### 3.2 第二轮逐项实测（每一项都是独立一次构建后的 `dist/assets` 求和）

| 候选                                                              | 实测 Δ    | 处置                                                                   |
| ----------------------------------------------------------------- | --------- | ---------------------------------------------------------------------- |
| `vite.config.mjs`：`modulePreload: { polyfill: false }`           | **−532**  | 采用（目标浏览器均原生支持 `modulepreload`，polyfill 永不被执行）       |
| `dedupResponses.ts` 复用 `assetLinkResponses` 的响应原语          | **−276**  | 采用（同一判定只有一份，壳与工作台不会对「合法响应」产生分歧）          |
| `useDedupJob.ts` 复用 `queryState.ts` 的 `failure/isAbort/isAccessFailure` | **−211**  | 采用（原先是逐字重复实现，只差一句错误文案）                            |
| 删除 `dedupTypes.ts`/`dedupResponses.ts` 中零读取者的两个字段     | −92       | **未采用**：它们来自服务端响应，删除会缩小线格式覆盖面，收益与语义不符 |
| 去掉 `DedupRoute.group`/`.cursor`                                 | −163      | **未采用**：会砍掉 `?group=`/`?cursor=` 深链                            |
| `sourcemap: false`                                                | −145      | **未采用**：与功能无关，且证据截图/排查需要源码映射                     |
| 手写 `manualChunks`（合并为单包）                                 | +245→**−245**（求和反而降 245） | 未采用：整体更碎，且厂商分包 **+285**（多出 `rolldown-runtime` 分片） |
| 合并两个异步视图为一个                                                | **+478**  | 未采用                                                                 |
| 撤销 `LibraryAdmin` 的动态导入（改回静态）                        | **+371**  | 未采用（说明动态导入本身是净收益）                                     |
| `build.target: "es2022"`                                          | **0**     | 无收益（Vite 8/rolldown 已是该层级）                                   |
| `cssCodeSplit` / `legalComments` / `oxc` 选项                     | **0**     | 无收益                                                                 |
| 删除 `dedupResponses.ts` 里零引用的 `errorMessage()` 导出          | **0**     | 无收益（压缩器已摇树）——「删死代码」在此项目并不自动省字节             |

门禁口径下 340347 → **339338**（第二轮），仍差 **11658**。第三轮按产物归因把它真正消掉，见 3.3。

### 3.3 第三轮：在原口径下通过（每一项都是独立一次 `vite build` 后的 `dist/assets` 求和）

| 产物                      | 第二轮     | 第三轮     | 变化       |
| ------------------------- | ---------- | ---------- | ---------- |
| `index-*.js`（入口）      | 297503     | 274611     | **−22892** |
| `DedupWorkbench-*.js`     | 33876      | 33862      | −14        |
| `LibraryAdmin-*.js`       | 7959       | 7947       | −12        |
| **JS 合计（门禁求和）**   | **339338** | **316420** | **−22918** |
| `index-*.css`             | 32394      | 32394      | 0          |
| **JS+CSS（门禁求和）**    | 371732     | **348814** | −22918     |

预算保持协调要求恢复的原值（`javascript` 327680、`javascript_and_css` 360448、`css` 32768），
求和口径、计量单位与压缩方式均未改动：
`python -I -B scripts/validate_web_dependencies.py --require-build-artifacts` →
`Web dependency, lock, and license policy passed with build budgets.`（退出码 0）。

**这一轮采用的唯一结构性改动**：新增 `apps/web/src/hooks/useRowWindow.ts`，导出与调用点原先使用的
`useVirtualizer` **同名同形状**的 hook（`getTotalSize()` / `getVirtualItems()` / `scrollToOffset()` /
`scrollToIndex()`），`VirtualEntryList` 与 `LibraryCatalogPage` 共用它，不再各自从
`@tanstack/react-virtual` 导入。该库按 `ROUND5-FRONTEND-SPEC` 第 1 节「仅在现有长列表模式确需时复用」
不再被运行时导入；依赖仍在 `package.json` 与 `pnpm-lock.yaml` 中，**锁文件未改**。
行为保持：总高、overscan（列表 8 / 网格 3 / 资源库表 5）、`scrollToOffset` 的 clamp、
`scrollToIndex` 的「已完整可见则不滚动」语义、行位置与网格列宽全部不变；
`scripts/validate_web_source.py` **未修改**，其窗口化渲染断言仍然真实成立。
证据：全量 `playwright test` 86 通过（含列表/网格滚动、键盘移动与焦点回位用例），
真实试运行通过。

### 3.4 第四轮：稳定订阅后复测（独立一次 `vite build`）

| 产物                      | 第二轮     | 第三轮     | 第四轮     | 相对第二轮 |
| ------------------------- | ---------- | ---------- | ---------- | ---------- |
| `index-*.js`（入口）      | 297503     | 274611     | 274777     | **−22726** |
| `DedupWorkbench-*.js`     | 33876      | 33862      | 33862      | −14        |
| `LibraryAdmin-*.js`       | 7959       | 7947       | 7947       | −12        |
| **JS 合计（门禁求和）**   | **339338** | **316420** | **316586** | **−22752** |
| `index-*.css`             | 32394      | 32394      | 32394      | 0          |
| **JS+CSS（门禁求和）**    | 371732     | 348814     | **348980** | −22752     |

第四轮的 +166 B 全部来自 `useRowWindow.ts` 的稳定订阅与几何比较（含注释）。门禁仍为原值与原口径，
`validate_web_dependencies.py --require-build-artifacts` 通过（退出码 0）。

**稳定订阅做了什么**：`getScrollElement` 被存进 ref 而不是当依赖（元素身份是唯一状态，
`useLayoutEffect` 无依赖数组，`setNode` 只在元素真的换了才写入），几何量在调用 setter 之前先与上一次
**比较**，未变化时不写入。React 对「同值同引用」才会跳过渲染，新对象一定触发渲染，所以比较必须在
setter 之前——这正是本轮缺陷的根因。订阅只在 `node` 变化时重建，回调是稳定引用，
挂载/卸载、容器替换、列表↔网格切换与缩放各自只做该做的事。

## 4. 覆盖到的失败与边界（夹具驱动）

- **拒绝/授权**：非管理员账号显示无权限页；导出 403 显示服务端文案而不是空计划。
- **CSRF**：夹具对每个查重请求校验 `X-AssetLibrary-CSRF`，缺失即抛错，因此所有用例都隐含覆盖该头。
- **同键冲突**：受理规则由服务端 `dedup_already_running` / `idempotency_conflict` 承担；页面在 400/409 时
  重新向服务端读取状态而不是自行猜测（`revision` 触发重读）。拒绝码与状态码的对应由
  `DedupRefusalWireTests` 锁定。
- **Host 与页面之间的键与类型**：见 1.1（本卡新增的 13 项线格式契约用例）。
- **六个操作的路由与只读边界**：见 1.2（本卡新增的 6 项端点路由用例）。
- **未完成的复核不得被读成「没有变化」**：`DedupRecheckWireTests` 锁定 `pending`/`completed`/`refused`
  三态与 `recheck_task_id` 的存在性，浏览器用例 8、9 从页面侧再锁一次（1.1 与第 3 节）。
- **预算只有一个权威来源**：受理回执 `job.limits` 经 `DedupReportWireTests` 锁定；浏览器用例 3 断言第二次
  `start` 原样重申受理值，用例 1 断言受理之前页面上没有任何预算数字。
- **不可读项只出现一次**：`DedupFindingClassifier.Section` 给出互斥分类（不可读 > 未验证 > 唯一），
  由 `DedupReportProjectionTests` 覆盖；`DedupReport.Facts` 去重后是重建的唯一来源。
- **复核不得把新增文件报成「文件消失」**：`DedupRecountLimits` 不得把复核预算压到上次读到的字节数之下，
  由 `tests/dotnet/AssetLibrary.ReadCore.Tests/DedupPlanTests.cs` 的
  `NewContentIsReportedAsNewRatherThanUnchanged` 覆盖（该用例正是本轮定位到该缺陷的用例）。
- **取消/重启/旧租约迟到结果**：TaskHealth 的租约围栏与 `DedupJobWorker` 的 `LeaseLostException` 负责；
  页面只展示服务端状态，取消后按钮禁用。返修轮的复核任务走同一套围栏（`DedupRecheckRunner` 每个分支
  都先确认报告版本仍是当时受理的那一代，迟到结果不得覆盖新版）。
- **任务/报告版本绑定游标**：游标由服务端 HMAC 签发并绑定 `DedupReportKey`；页面把游标当不透明字符串使用，
  过期时显示服务端文案。
- **登出/切库清空旧结果**：切库或失去库时页面重置并 abort 在途请求；`current.current` 守卫丢弃旧库回包。
- **预算/逃逸/来源变化**：由 TS065 的 `DedupScopePolicy`/`DedupPlanPolicy` 与其 49 项用例覆盖，本卡未重复实现。

## 5. 未执行（证据缺失，不记为通过）

- **旧的只读浏览试运行：最后一步被既有缺陷阻断（不是本卡引入）。** 第一轮记的「本机无 PostgreSQL 二进制」
  是错的——PostgreSQL **16.15 的二进制**（非服务）存在于
  `C:\YOKI\Codex\worktrees\V01-004\.runtime\sandbox-storage\V01-004\postgresql-16.15\pgsql\bin` 与
  `%TEMP%\V01-014-tooling-and-tests\tooling\postgresql\pgsql\bin`，`run_e2e.py --execute` 能自己起临时集群
  （仍无 docker、无 wsl；需要 `git` 在 `PATH` 上）。已跑通：临时集群初始化、真实迁移、真实 HTTPS Core、
  真实 Chromium 管理员登录、登记合成库、首次扫描、目录浏览、搜索、桌面宽度详情。
  唯一失败点在 `browser.mjs` 的 390×844 段：双击结果条目后找不到 `资产详情` 对话框。实测输出为
  `DRAWER_PROBE {"width":390,"dialogs":["图片预览"],"asides":["资源库导航"],"hasEntry":true}` ——
  窄屏双击同时进入快速预览，详情抽屉因此不渲染；`EntryDetails.tsx` 的窄屏分支与 `useNarrowWorkspace`
  （1199px）在**基线 `5d9dc39` 就已存在**，不是本卡引入。**未记为通过**，命令、探针输出与建议见交接第 7.4
  与第 9 节；本卡未改共享壳绕过它。
- **已登录管理员走通六个操作：已执行（第四轮起，第五轮恢复正确顺序）。** 第四轮起 `run_e2e.py` 有独立的
  `trial_e2e_dedup` 试运行：HTTP 契约段落 + **真实 Chromium 页面闭环**，全部打真实 Core、无任何路由 mock。
  第五轮把顺序恢复为卡要求的「登录 → 选库 → 开始分析 → 状态/结果（开合组详情）→ 重新核对完成 →
  确认页面已切到复核落下的新版本 → 导出成功 → 第二个库运行中取消」，实测 1/1 通过（1 分 40 秒），
  导出文档的 `analysis_version`/`plan_digest` 与服务端及页面一致、`grants_file_operation=false`。
  可用 `--trial trial_e2e_dedup` 单独执行；整轮 `run_e2e.py` 的退出码仍会被上一条既有缺陷拖成 1。
  注意 `--playwright-module` 必须指向 `@playwright/test/index.mjs`（`playwright/index.mjs` 不导出 `expect`）。
- **复核在真实租约下的执行：已执行（第四轮）。** 试运行里 `revalidate` 段落跑在真实 worker 的真实租约下
  （回执 `pending` → `completed` → `plan_still_current=true`），并且租约续期本身改为直接读
  `task_health.durable_task` 的 `heartbeat_at`/`lease_until` 断言（见 1.0.3）。
  但**「迟到复核不得覆盖新报告」这一竞争仍不在试运行里**：worker 单线程串行认领，扫描与落盘之间不会插进
  另一次分析，因此该竞争继续由进程内确定性用例
  `DedupRecheckFenceTests.ALateVerdictNeverReplacesANewerAnalysisOfTheSameLibrary` 覆盖（含反向探针）。
- **复核后页面不重载新版本：第五轮已修复**（第四轮曾把它记为「既有缺口、未修」，该口径已纠正）。
  读取开关改为「任务 + 服务端当前报告版本」，版本一律取服务端答案（`status`/`revalidate` 回执/
  `results` 页自带），读取期间禁用导出并在读取完成后重读切片，迟到旧响应不覆盖新状态，切库仍清空原数据。
  真实试运行实测版本由 `:1` 变为 `:2`，页面确认新版本后才导出，导出文档即为该版本；
  被取代版本的导出仍由同一试运行的 HTTP 契约段断言 409 `dedup_version_conflict`。
- **多实例并发**：本卡未新增调度器，跨实例互斥由 TaskHealth 既有租约保证，未在本机做多进程验证。
