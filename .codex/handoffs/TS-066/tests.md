# TS-066 测试与验证记录

所有命令都在任务检出 `C:/YOKI/Codex/tianshu-peiban-bot/worktrees/TS-066/assetlibrary` 内、使用合成数据执行。
非零退出码均如实记录，缺证据项不记为通过。

## 1. .NET 与仓库门禁

| 命令                                                                            | 结果                                                                                                                                                                                                |
| ------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `dotnet format AssetLibrary.slnx --verify-no-changes --no-restore`              | 通过（首次发现 2 处格式问题：`DedupReportRegistry.cs` 空白、`DedupPorts.cs` 末行换行；返修轮又发现 `DedupReportWireTests.cs` 10 处 CRLF——该文件由 PowerShell `Set-Content` 写入——均修正后复验通过） |
| `dotnet build AssetLibrary.slnx --configuration Release --no-restore`           | 通过，0 警告 0 错误                                                                                                                                                                                 |
| `dotnet test AssetLibrary.slnx --configuration Release --no-build --no-restore` | 通过                                                                                                                                                                                                |

测试项目明细（通过 / 跳过 / 总计，**第二轮复验值**）：

- `AssetLibrary.AssetLink.Tests` 13 / 0 / 13
- `AssetLibrary.Build.Tests` 2 / 0 / 2
- `AssetLibrary.TaskHealth.Tests` 32 / 0 / 32
- `AssetLibrary.TransferOperation.Tests` 64 / 0 / 64
- `AssetLibrary.ReadCore.Tests` 110 / 25 / 135（含 TS065 的 49 项查重用例与第二轮新增 4 项增长/预算用例；跳过项均为既有 PostgreSQL/POSIX 条件用例）
- `AssetLibrary.Packaging.Tests` 63 / 0 / 63
- `AssetLibrary.WebGateway.Tests` 121 / 4 / 125（含本卡新增的 13 项线格式契约用例、6 项端点路由用例与第二轮的 3 项复核围栏用例）
- `AssetLibrary.Preview.Tests` 111 / 24 / 135

合计 **516 通过 / 53 跳过 / 0 失败**（第二轮提交 `eacf26e`）。

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

**`DedupTrialLeaseProbe`（由上一项调用）** —— 真实租约的心跳证据。worker 只在一次尝试超过
心跳间隔（2 秒）时才续租，而合成源的哈希快于该间隔，所以心跳不走轮询：用**同一个
`IDurableTaskCoordinator`**（即同一套真实 `task_health` SQL）按名字驱动一次完整租约生命周期——
入队 → 认领（`leased`、`attempt=1`）→ 心跳续租 `Accepted` 且租约窗口被**延长到认领授予的窗口之后**
→ 在租约下 `finish` 为 `succeeded` → 用已终结的 identity 再续租被拒 `NotCurrent`。
探针任务用独立任务类型 `dedup.trial.lease_probe`：worker 认领的是「最老的 queued 任务」而不区分类型，
若用工作台自己的类型，worker 会先把它当查重载荷执行。

**`DedupRecheckFenceTests` 新增 1 项（共 4 项）** ——
`ALateVerdictNeverReplacesANewerAnalysisOfTheSameLibrary`：**旧 key 仍保留**（不是删除旧 key 的场景），
在复核扫描中途发布更新的一次分析，复核必须在落盘前被拒（`completed=false`、
`dedup_version_conflict`），`latest` 仍指向新任务，旧版本仍可读。反向探针确认该用例会咬住旧代码：
把 `File` 换回上一轮的非原子写法后立即失败
（`Assert.AreNotEqual 失败。应为: <6499ee26-…> 以外的任意值，实际为: <6499ee26-…>`）。

**`TrialOperatorBootstrap`（共享夹具助手）** —— 两个真实试运行原先各自复制一份「初始化保护算子密钥 +
引导首个管理员」的 60 token 代码块，被 `scripts/validate_dotnet_source.py` 判为重复；抽成一处后
`.NET source policy passed`。

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
| `vite build`（工作目录 `apps/web`）                               | 通过：入口 `index-*.js` 297973 B + `DedupWorkbench-*.js` 34415 B + `LibraryAdmin-*.js` 7959 B = 340347 B（预算 344064 B）、`index-*.css` 32394 B（预算 32768 B） |
| `playwright test --config apps/web/playwright.config.mjs`（全量） | **83 通过 / 0 失败**                                                                                                                                             |
| 本卡用例 `--repeat-each 2 --workers 2`                            | 22 通过 / 0 失败（稳定性复验）                                                                                                                                   |

`pnpm` 未在本机 PATH 上，浏览器门禁以固定版本 `node v24.20.0` 直接调用仓库内
`apps/web/node_modules/@playwright/test/cli.js`、`typescript/bin/tsc`、`vite/bin/vite.js` 与
`prettier/bin/prettier.cjs`，等价于 `ci-tiers.json` 中 `pnpm --dir apps/web run …` 的对应命令。

## 3. 本卡浏览器用例（`tests/web/dedup-workbench.spec.mjs`，17 项）

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
证据：全量 `playwright test` 83 通过（含列表/网格滚动、键盘移动与焦点回位用例），
真实试运行通过。

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

- **真实 PostgreSQL / HTTPS / Chromium 端到端：已执行，最后一步被既有缺陷阻断。** 第一轮记的「本机无
  PostgreSQL 二进制」是错的——PostgreSQL **16.15 的二进制**（非服务）存在于
  `C:\YOKI\Codex\worktrees\V01-004\.runtime\sandbox-storage\V01-004\postgresql-16.15\pgsql\bin` 与
  `%TEMP%\V01-014-tooling-and-tests\tooling\postgresql\pgsql\bin`，`run_e2e.py --execute` 能自己起临时集群
  （仍无 docker、无 wsl；需要 `git` 在 `PATH` 上）。本轮已跑通：临时集群初始化、真实迁移、真实 HTTPS Core、
  真实 Chromium 管理员登录、登记合成库、首次扫描、目录浏览、搜索、桌面宽度详情。
  唯一失败点在 `browser.mjs` 的 390×844 段：双击结果条目后找不到 `资产详情` 对话框。加入诊断后的实测输出为
  `DRAWER_PROBE {"width":390,"dialogs":["图片预览"],"asides":["资源库导航"],"hasEntry":true}` ——
  窄屏双击同时进入快速预览，详情抽屉因此不渲染；`EntryDetails.tsx` 的窄屏分支与 `useNarrowWorkspace`
  （1199px）在**基线 `5d9dc39` 就已存在**，不是本卡引入。**未记为通过**，命令、探针输出与建议见交接第 7.4 节。
- **已登录管理员走通六个操作**：认证中间件需要数据库账号存储（`GatewayAuthenticationRuntime` 依赖
  `PostgresAuthenticationStore`），本次真实闭环的浏览器段确实以真实管理员会话完成了登录与登记/扫描/浏览/
  搜索，但**六个查重操作本身**仍未在真实 HTTPS 上被管理员账号走通：`run_e2e.py` 尚无查重步骤，而浏览器段
  现在卡在上述既有缺陷上。未登录边界与答案形状分别由 1.2 与 1.1 覆盖。
- **复核在真实租约下的执行**：`prepare → scan → file` 三段与围栏内外分工已由第二轮的
  `DedupRecheckFenceTests` 用**真实 `DedupJobWorker`** 加合成围栏探针覆盖（断言扫描期间围栏从不打开、
  结论在恰好一次围栏内落盘、版本被替换时拒绝、取消后不落盘）；但对着**真实 PostgreSQL** 的
  「认领 → 心跳 → 提交围栏 → 迟到拒绝」整段仍未在本机执行，与上一条同一环境缺口。
- **多实例并发**：本卡未新增调度器，跨实例互斥由 TaskHealth 既有租约保证，未在本机做多进程验证。
