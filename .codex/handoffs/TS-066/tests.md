# TS-066 测试与验证记录

所有命令都在任务检出 `C:/YOKI/Codex/tianshu-peiban-bot/worktrees/TS-066/assetlibrary` 内、使用合成数据执行。
非零退出码均如实记录，缺证据项不记为通过。

## 1. .NET 与仓库门禁

| 命令 | 结果 |
| --- | --- |
| `dotnet format AssetLibrary.slnx --verify-no-changes --no-restore` | 通过（首次发现 2 处格式问题：`DedupReportRegistry.cs` 空白、`DedupPorts.cs` 末行换行；已修正后复验通过） |
| `dotnet build AssetLibrary.slnx --configuration Release --no-restore` | 通过，0 警告 0 错误 |
| `dotnet test AssetLibrary.slnx --configuration Release --no-build --no-restore` | 通过 |

测试项目明细（通过 / 跳过 / 总计）：

- `AssetLibrary.AssetLink.Tests` 13 / 0 / 13
- `AssetLibrary.Build.Tests` 2 / 0 / 2
- `AssetLibrary.TaskHealth.Tests` 32 / 0 / 32
- `AssetLibrary.TransferOperation.Tests` 64 / 0 / 64
- `AssetLibrary.ReadCore.Tests` 106 / 25 / 131（含 TS065 的 49 项查重用例，跳过项均为既有 PostgreSQL/POSIX 条件用例）
- `AssetLibrary.Packaging.Tests` 63 / 0 / 63
- `AssetLibrary.WebGateway.Tests` 115 / 4 / 119（含本卡新增的 8 项线格式契约用例与 3 项端点路由用例）
- `AssetLibrary.Preview.Tests` 111 / 24 / 135

合计 **506 通过 / 53 跳过 / 0 失败**。

### 1.1 线格式契约用例（`DedupReportWireTests` / `DedupExportWireTests` / `DedupRefusalWireTests`）

浏览器套件跑的是手写夹具，所以「Host 真正序列化出来的键」与「页面解码器要读的键」之间原本没有任何门禁。
新增的 8 项用例直接调用真实 Host 构造器（`TrialDedupJson` / `TrialDedupPageJson` / `TrialDedupExportJson`），
序列化后再解析，逐键断言页面解码器 `apps/web/src/dedup/dedupResponses.ts` 要求的**完整键集合**与 JSON
类型，并断言：

- `failure_code` / `performed_at` / `previous_plan_digest` 这类可空字段**以显式 null 出现**，不是缺键；
- `source_failures` 是对象数组（`source_id` + `reason_code`），不是裸字符串；
- 报告版本以 `任务 id:代数` 绑定，页面后续每次调用都带这个值；
- 导出的计划文档自带 `grants_file_operation = false` 与只读声明；
- 全部数字都是整数且落在 JavaScript 安全整数范围内（2^53−1），`maximum_bytes` 仍是 JSON 数字而不是字符串；
- 每个拒绝码映射到调用方可处理的状态码（400/403/404/409/503），且带可读中文文案。

这些用例的**有效性经过反向探针确认**：把 `TrialDedupJson.Job` 里的 `analysis_version` 临时改名为
`analysis_ver`，用例立即失败并打印完整键集合（`keys: task_id,...,analysis_ver,...`）；改回后恢复通过。
因此这不是一组恒真的断言，而是真正锁住了 C# ↔ TypeScript 之间唯一没有被其他门禁覆盖的接缝。

### 1.2 端点路由与只读边界用例（`DedupEndpointRoutingTests`，3 项）

`TrialWebEndpoints.Configure` 把六个操作注册在既有 `/assetlink/v1` 面内，而浏览器套件用的是夹具拦截，
所以「路由真的存在」这件事原本没有门禁。这组用例在环回地址上以**真实 HTTPS**（自签证书 + 测试信任回调）
启动一个最小宿主并挂上与试运行宿主相同的 `TrialDedupEndpoints.Map`，然后：

- 逐个 POST 六个操作路径，断言 **403 `permission_denied`**（而不是 404）——路径存在且被守卫；
- 断言未知路径 `/assetlink/v1/dedup/analyse` 返回 404，GET `/status` 返回 405——查询绝不能启动或改变分析；
- 直接对共享读取器 `AssetLinkRequestBody` 断言：`Content-Length` 超 64 KiB 抛
  `RequestBodyTooLargeException`（对应 413）、流式超限同样抛错、非 JSON 内容类型抛
  `UnsupportedRequestMediaTypeException`（对应 415）。

同样经过反向探针：把 `Operations` 里的 `export` 临时改名为 `export_plan`，用例立即以
`Forbidden 应为 / NotFound 实际` 失败；改回后恢复通过。生产文件已确认与提交版本一致（`git diff` 为空）。

这组用例只覆盖到授权之后的那一步：认证中间件依赖数据库账号存储（`GatewayAuthenticationRuntime`），
本机不可用，因此「已登录管理员在环回地址上走通六个操作」这一条仍未在任何门禁中执行，如实记录在
第 5 节。

| 命令 | 结果 |
| --- | --- |
| `python -I -B scripts/verify_repository.py` | `Repository verification passed (M0-009 fast architecture gate).`（含 14 项架构测试） |
| `python -B tests/architecture/check_release_gates.py --target v0.1-start` | `RELEASE_GATE_ALLOWED: v0.1-start` |
| `python -B scripts/validate_web_source.py` | `Web read-only source boundary passed.` |
| `python -B scripts/validate_web_dependencies.py --require-build-artifacts` | `Web dependency, lock, and license policy passed with build budgets.` |

构建过程中被门禁拦下并**真实修复**的问题（不是绕过）：

1. `validate_dotnet_source.py`：`ReadOnlyAssetLinkEndpoints.cs` 与 `TrialDedupRequest.cs` 的 60 token
   重复块 → 抽出 `Adapters/AssetLink/AssetLinkRequestBody.cs`，两处共用同一实现。
2. `validate_web_source.py`：新增文件里出现过第二处 `fetch(`、`sessionStorage` 与 `console.log` →
   删除 `dedupStorage.ts`，六个操作改为经唯一 `AssetLinkClient` 适配器发送；浏览器不再保存任务标识，
   恢复语义改由服务端幂等任务身份承载。
3. `dotnet build`：`CA1506`（组合根/HTTP 面类型计数）、`ASP0016`、`CS0122`、`CS8754` 等按仓库既有做法修复
   （`SuppressMessage` 带 `Justification`、lambda 转 `Delegate`、公开同一程序集内被 Host 读取的读取器）。

## 2. Web 构建与浏览器回归

| 命令 | 结果 |
| --- | --- |
| `prettier --check . ../../tests/web` | `All matched files use Prettier code style!` |
| `tsc --project apps/web/tsconfig.json --noEmit` | 通过（等同 `pnpm run lint` / `typecheck`） |
| `vite build` | 通过：`dist/assets/index-*.js` 336.16 kB（预算 344064 B）、`index-*.css` 32.11 kB（预算 32768 B） |
| `playwright test --config apps/web/playwright.config.mjs`（全量） | **79 通过 / 0 失败** |
| 本卡用例 `--repeat-each 2 --workers 2` | 22 通过 / 0 失败（稳定性复验） |

`pnpm` 未在本机 PATH 上，浏览器门禁以固定版本 `node v24.20.0` 直接调用仓库内
`apps/web/node_modules/@playwright/test/cli.js`、`typescript/bin/tsc`、`vite/bin/vite.js` 与
`prettier/bin/prettier.cjs`，等价于 `ci-tiers.json` 中 `pnpm --dir apps/web run …` 的对应命令。

## 3. 本卡浏览器用例（`tests/web/dedup-workbench.spec.mjs`，13 项）

| # | 用例 | 断言要点 | 截图 |
| --- | --- | --- | --- |
| 1 | 未开始分析时说明范围与预算 | 标题/只读说明/仅本库说明/预算四项；按钮在未选库时禁用；未发出任何请求 | `dedup-idle-1440.png` |
| 2 | 开始分析显示服务端自己的状态，不伪造结果 | 状态「等待执行」；请求体只有 `library_id`/`operation_key`/`retry`；整套请求不含 `C:/`；改为 leased 后显示「正在分析」 | `dedup-scanning-1440.png` |
| 3 | 完成的分析分开列出组、计数与证据 | 结果版本、默认 tab、组行、证据文案、本次不可读、范围说明；`results` 只发一次且带 `task_id` | `dedup-results-1440.png` |
| 4 | 打开组加载成员、说明证据、关闭回到原行 | 详情标题获得焦点；成员含 `holiday/beach.png`；`group_key` 正确；关闭后详情消失且原行重新获得焦点 | — |
| 5 | 切换区块把版本带进 URL | `section=unverified`；最后一次 `results` 的 `kind` 为 `Unverified` | — |
| 6 | 过期的计划被如实报告，不会被当成当前版本导出 | `PlanStale` 文案、变化/消失/新增计数、`plan_digest` 正确、**没有**发出导出请求 | — |
| 7 | 取消只针对分析本身 | 「已请求取消，仅取消分析本身」；取消按钮禁用；请求体键为 `library_id` + `operation_key` | `dedup-cancelled-1440.png` |
| 8 | 不完整的扫描说明自身限制，预算证据与重复分开 | 计划文案、达到预算上限后停止、本次不可读、实际读取量；不显示成功结论 | `dedup-incomplete-1440.png` |
| 9 | 报告被丢弃时如实说不可读，不显示成没有重复 | 「本次结果已不可读取」；不出现重复组 | — |
| 10 | 导出被拒是权限失败而不是空计划 | 403 文案；只发出一次导出请求 | — |
| 11 | 宽布局下详情在列表右侧，关闭后焦点回原行 | 详情块 x 在列表右边界之外，宽度取整为 360 | `dedup-detail-1440.png` |
| 12 | 窄屏、深色与减少动态下堆叠且无横向溢出 | 1024 / 390（深色）/ 320 三档：`scrollWidth <= innerWidth`，行可见 | `dedup-1024.png`、`dedup-390.png`、`dedup-320.png` |
| 13 | 长中文名称保留完整值，行序与键盘顺序可用 | 完整名称可读、Tab 顺序稳定、Enter 可打开组 | — |

截图存放于 `.runtime/dedup-frontend-evidence/`（9 张，由用例在断言通过后 `animations: "disabled"`、
`fullPage` 拍摄）。该目录是运行产物，不入提交；用例本身带截图语句，重跑即可复现。

## 4. 覆盖到的失败与边界（夹具驱动）

- **拒绝/授权**：非管理员账号显示无权限页；导出 403 显示服务端文案而不是空计划。
- **CSRF**：夹具对每个查重请求校验 `X-AssetLibrary-CSRF`，缺失即抛错，因此所有用例都隐含覆盖该头。
- **同键冲突**：受理规则由服务端 `dedup_already_running` / `idempotency_conflict` 承担；页面在 400/409 时
  重新向服务端读取状态而不是自行猜测（`revision` 触发重读）。拒绝码与状态码的对应由
  `DedupRefusalWireTests` 锁定。
- **Host 与页面之间的键与类型**：见 1.1（本卡新增的 8 项线格式契约用例）。
- **六个操作的路由与只读边界**：见 1.2（本卡新增的 3 项端点路由用例）。
- **取消/重启/旧租约迟到结果**：TaskHealth 的租约围栏与 `DedupJobWorker` 的 `LeaseLostException` 负责；
  页面只展示服务端状态，取消后按钮禁用。
- **任务/报告版本绑定游标**：游标由服务端 HMAC 签发并绑定 `DedupReportKey`；页面把游标当不透明字符串使用，
  过期时显示服务端文案。
- **登出/切库清空旧结果**：切库或失去库时页面重置并 abort 在途请求；`current.current` 守卫丢弃旧库回包。
- **预算/逃逸/来源变化**：由 TS065 的 `DedupScopePolicy`/`DedupPlanPolicy` 与其 49 项用例覆盖，本卡未重复实现。

## 5. 未执行（证据缺失，不记为通过）

- **真实 PostgreSQL / HTTPS / Chromium 端到端**：`python -I -B tests/integration/read-only-trial/run_e2e.py`
  需要 PostgreSQL 服务或二进制（`--postgres-bin` / `--postgres-external`），本机二者都没有，也没有
  docker，因此未运行。`tests/integration/read-only-trial/browser.mjs` 目前也只有库登记步骤，没有查重操作；
  若要在真实环境覆盖本卡，需要在那里补一个查重步骤，再由具备 PostgreSQL 的环境执行。
- **已登录管理员走通六个操作**：认证中间件需要数据库账号存储
  （`GatewayAuthenticationRuntime` 依赖 `PostgresAuthenticationStore`），本机不可用，因此端到端用例只覆盖
  到「未登录一律 403」为止；授权之后的调用链由 1.1 的线格式用例与 13 项浏览器用例分别覆盖两端。
- **多实例并发**：本卡未新增调度器，跨实例互斥由 TaskHealth 既有租约保证，未在本机做多进程验证。
