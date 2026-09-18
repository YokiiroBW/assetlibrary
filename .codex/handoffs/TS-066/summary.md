# TS-066 摘要

**任务**：资产库精确查重网页工作台 — 在 AssetLibrary 自己的网页里完成「选已登记库 → 后台有界精确
查重 → 分页查看重复组与未验证项 → 重新核对 → 导出绑定版本的 JSON 计划」。

**状态**：`ready_for_review`（第四轮返修）。分支 `work/ts-066`，基线
`5d9dc39b926366076bb419c040cb000dd01455b5`；第一轮从 `2d890e72d9a49cc49d1ce91b014c25b433e0bb7e`
接续（依据 `TS-066-review-fixes.md`），第二轮从 `e492d28` 接续（依据 `TS-066-review-fixes-2.md`），
第三轮从 `89a5539` 接续（依据 `TS-066-review-fixes-3.md`），第四轮从 `1b8873c` 接续
（依据 `TS-066-review-fixes-4.md`），第四轮产品提交 `54c4bb1`。本地提交，未推送、未合并、未部署。

## 第四轮返修（协调复验的 4 项阻塞）

| 项                              | 修法                                                                                                                                                                                                                                                                                | 验证                                                                                                                                                                              |
| ------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 1 行窗口无限更新                | `useRowWindow` 不再把每次渲染新建的 `getScrollElement` 当依赖：元素身份是唯一状态（`useLayoutEffect` 无依赖数组、`setNode` 只在元素真的换了才写入），几何量在写入前先比较，未变化时不调用 setter；订阅只在 `node` 变化时重建。                                                     | 新增 `tests/web/row-window.spec.mjs` 3 项（闲置资产列表、闲置资源库列表、滚动+缩放+切换+键盘）均断言递归更新 0 次；协调者探针由 `IDLE_MAXIMUM_UPDATE_DEPTH_ERRORS=7` 变为 **0 并通过**；反向探针恢复缺陷后 3 项全失败。 |
| 2 缺真实浏览器流程              | 新增 `tests/integration/read-only-trial/dedup-browser.mjs` + `DedupTrialBrowser`/`TrialBrowserProcess`：真实 Chromium 经 SPKI 固定证书打真实 Core，页面自身点击完成登录 → 选库 → 分析 → 状态/结果 → 导出 → 重新核对 → 第二个库运行中取消；HTTP 契约用例保留并单列。                  | `trial_e2e_dedup` 实测 1/1 通过（约 1 分 24 秒），六个操作从线路逐一核对，导出文档 `grants_file_operation=false` 且版本与页面一致。                                                |
| 3 真实 PG 生命周期断言过松      | 新增只读 `DedupTrialLeaseReader`（模块自身会话读 `task_health.durable_task`）：续期改为在**仍持有租约**的两次采样间比较 `heartbeat_at`/`lease_until`；取消前确认已领取且仍在运行，取消后**必须** `cancelled`；重启前先断言报告可读，重启后断言同一 `task_id` 保留且报告不可读。      | 实测 `heartbeat_at` 由 `…26.700` 前进到 `…27.247`、`lease_until` 越过认领窗口；取消落 `cancelled`；重启后 `dedup_report_not_retained` + 404。产品侧**未加任何测试钩子**。           |
| 4 交付文档修正                  | 删除并不存在的 `DedupTrialLeaseProbe` 与无支撑的续期结论（C# 文件数 646 → 645）；`StoreEvidenceIfCurrent` 的 `Filed` 语义改为钉住并写清：成功时也是 `false`，靠 `Key` 与 `Superseded` 区分；调用它的分支因 `Recount` 恒返回非空计划而不可达，故不改签名。                          | 新增 `DedupReportRegistryEvidenceTests` 2 项；反向探针把返回值改成 `true` 后立即失败。                                                                                              |

第四轮合计 .NET **519 通过 / 54 跳过 / 0 失败**，Chromium **86 通过 / 0 失败**（本卡 20 项）。

## 第二轮返修（协调复验的 3 项阻塞 + 真实闭环）

| 项                                        | 修法                                                                                                                                                                                                                                                                          |
| ----------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| A 预算未按真实读取结算                    | `DedupScopeResolver` 的许可改为「允许读多少」而非预留，同时受单文件上限 / 计划中该条目长度 / 总额度三者约束，**只有返回的字节才计入**；`TooLarge` 不占额度；整份计划超预算时无论分几批都有界；读取器超许可返回即失败；`ReadBytes` 只累加 `ContentVerified`，与已读条目长度之和恒等。 |
| B 复核扫描持 Postgres 行锁遍历目录        | `DedupRecheckRunner` 拆 `PrepareAsync`（校验版本与源可读性，不碰文件系统）/ `ScanAsync`（**无围栏**扫描）/ `File`（围栏内重读同一版本后落盘，版本已变记 `Superseded`）；新增 `DedupRecheckRefusals` 统一四种拒绝；源不可读在准备阶段即以 `source_unavailable` 拒绝。             |
| C 恢复原构建门禁                          | `size_budgets_bytes` 恢复 327680 / 360448 并删除自行加入的 `size_budget_notes`；同时做行为等价的真实复用（共享响应原语、共享失败助手、关闭 modulePreload polyfill），实测 340347 → **339338**（−1009）。**门禁仍差 11658 B**，未豁免、未改口径，交裁决。                     |
| D 真实临时 Core / 数据库 / HTTPS / 浏览器 | **已执行**（PostgreSQL 16.15 二进制本机存在，此前记载有误）：临时集群 + 真实迁移 + 真实 HTTPS Core + 真实 Chromium，登录/登记/首次扫描/浏览/搜索/桌面详情全部通过；**最后一步 390px 移动详情抽屉被既有（非本卡）缺陷阻断**，未记为通过。                                        |

新增用例：.NET 3 项复核围栏（真实 `DedupJobWorker` + 围栏探针）+ 4 项增长/预算；合计 .NET **516 通过 /
53 跳过 / 0 失败**，Chromium **83 通过 / 0 失败**。

## 第一轮返修（7 项必须修复）

| 项                          | 修法                                                                                                                                                                    |
| --------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 1 Host 缺协调器注册         | `TrialDedupComposition.Register` 从同一 `TrialDedupServices` 实例注册协调/检查/围栏三个公开端口，`TrialHostFactory` 装配后调用。                                        |
| 2 `task_id` 无条件必填      | 改为按操作校验：`start`/`cancel` 用「库 + 操作键」寻址，其余四个操作才要求 `task_id`。                                                                                  |
| 3 预算两套                  | 预算只有服务端一个权威来源：受理回执 → 任务载荷 → worker → 报告/导出；页面不自造数字，再次 start 时原样重申受理值。                                                     |
| 4 复核事实不可靠            | `DedupReport.Facts` 成为唯一可重建事实；分类互斥（不可读 > 未验证 > 唯一）；源未变不再报 `NewContent`（复核预算不得低于预览预算）；不可读项不再重复出现；截断显式告知。 |
| 5 revalidate 走 HTTP 内枚举 | 改为 TaskHealth 后台任务（幂等键 `dedup-recheck:{taskId}:{generation}`），绑定原报告版本与摘要；报告被替换即拒绝执行，迟到不覆盖；页面三态呈现。                        |
| 6 共享预算                  | 工作台与资源库管理页改为路由级动态导入（入口分包 336165 → 297973）；但门禁对全部产物求和，**总和不降**。第二轮已恢复原值并继续削减，见上表 C。                          |
| 7 移动列 select 巨高        | 240px 只作横向 basis，纵向固定 44px 触控高度；390/320/1024 均由用例断言实际高度。                                                                                       |

第一轮新增测试：.NET 端 13 项线格式契约（含复核三态、`limits` 子对象）+ 6 项端点路由（含按操作校验 `task_id`）；
浏览器端 17 项（新增「未完成复核不得显示成没有变化」「被拒复核明示未执行」「预算重申」「未滚动概览截图」）。

## 做了什么

- **后端（AssetIdentity 模块，复用 TS065 `DedupAnalyzer`，未新增扫描器/调度器）**
  - `Contracts/DedupJobContracts.cs`：作业视图、结果页、复核视图、导出文档、`DedupReportKey`、
    `DedupJobContractText`（8 份 / 每份 ≤20000 项 / 页 ≤200 / 超时 1h–6h）与中文用词。
  - `Application/`：`DedupJobService`（六操作门面）、`DedupJobStarter`（一库一操作键一作业、活动尝试
    互斥、载荷只带上限与库 id）、`DedupJobWorker`（心跳续租、取消发现、围栏提交）、
    `DedupReportRegistry`（有界保留、HMAC 游标、版本绑定、复核证据）、`DedupResultsReader`、
    `DedupFindingProjection`/`DedupFindingClassifier`、`DedupRecheckRunner`（只用报告自身已接受来源）、
    `DedupExportBuilder`、`DedupJobViewFactory`、`DedupReportRehydrator`、`DedupJobIdentity`。
  - `Host/Trial/Dedup/`：六个授权 POST 操作 + 组合根 + 后台工作器；`Host/Trial` 接线。
  - `Adapters/AssetLink/AssetLinkRequestBody.cs`：共享有界请求体读取，消除与既有端点的重复实现。
- **前端（React DOM + 浏览器 CSS，仅复用既有组件与样式变量）**
  - 侧栏「精确查重」入口 + `/dedup?library&section&group&cursor` 路由。
  - `dedup/`：类型、严格响应解码、客户端（经唯一 `AssetLinkClient` 适配器）、分析表单、任务状态、
    结果列表、重复组详情；`hooks/useDedupJob.ts` 负责请求、分页、单一在途读取与切片记忆。
  - `styles/dedup.css`：只用 `dedup` 类与既有颜色变量；≥1200px 双列（详情 360px）、768–1199px 下置、
    ≤767px 纵排；效果只有颜色/透明度；`prefers-reduced-motion` 降级。
- **测试**：`tests/web/dedup-workbench.spec.mjs` 17 项 + `tests/web/dedup-fixtures.mjs`；
  `tests/dotnet/AssetLibrary.WebGateway.Tests/` 内 13 项线格式契约用例（断言真实 Host 序列化出的键集合与
  页面解码器一致，含复核三态与 `limits`）与 6 项端点路由用例（真实 HTTPS 环回宿主上验证六个操作存在且对
  未登录调用返回 403、未知路径 404、GET 405、请求体超限/类型错误被拒、`task_id` 按操作校验）；
  两组断言均以反向探针确认有效。
- **返修轮新增的结构调整**：`apps/web/src/dedup/DedupWorkbench.tsx` 成为工作台的入口（壳只做路由级
  `lazy` 挂载），`apps/web/src/LibraryAdmin.tsx` 承载资源库目录/登记/分类/扫描任务页；`DedupJobWorker`
  按载荷类型分派分析与复核，`DedupRecheckRunner` 内的 `DedupRecheckScheduler` 负责受理与幂等。

## 验证结论

| 门禁                                                                                | 结果                                                                                                       |
| ----------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------- |
| Release 构建（`TreatWarningsAsErrors`）                                             | 0 警告 0 错误                                                                                              |
| 全量 .NET 测试                                                                      | 通过（**519 通过 / 54 跳过 / 0 失败**，跳过均为既有 PostgreSQL/POSIX 条件用例）                            |
| `verify_repository.py`                                                              | 通过（第四轮起，含 `.codex` JSON 解析；第二轮 `result.json` 的 UTF-8 BOM 已重写）                          |
| `validate_dotnet_source.py`                                                         | 通过（649 个 C# 文件）                                                                                     |
| `check_release_gates.py --target v0.1-start`                                        | `RELEASE_GATE_ALLOWED`                                                                                     |
| `validate_web_source.py`                                                            | 通过（未修改该脚本）                                                                                       |
| `validate_web_dependencies.py --require-build-artifacts`                            | 通过（体积预算：JS 316586 ≤ 327680、JS+CSS 348980 ≤ 360448）                                               |
| `validate_handoff.py`                                                               | 通过（37 项必需产物）                                                                                      |
| prettier / tsc / vite build                                                         | 通过（入口 274777 + 33862 + 7947 = **316586 B**；CSS 32394 B）                                             |
| Chromium 回归（全量）                                                               | 86 通过 0 失败（本卡 20 项）                                                                               |
| 真实 PostgreSQL/HTTPS/Chromium 端到端                                               | **查重试运行通过**（HTTP 契约 + 真实浏览器页面 + 真实租约/取消/重启断言，1/1）；旧浏览试运行仍在 390px 移动详情抽屉失败（既有缺陷），整轮退出码为 1，见第 7.4 与第 9 节 |

## 需要协调裁决

1. 断点：规格写「≥1200px 双列」，壳的折叠断点是 1199px；当前按规格字面实现，如需严格对齐只改一处媒体查询。
2. **体积预算已闭合**：门禁保持协调要求恢复的原值 327680 / 360448，第四轮实测 JS **316586**（余量 11094）、
   JS+CSS **348980**（余量 11468），`validate_web_dependencies.py --require-build-artifacts` 与
   `verify_repository.py` 均通过。唯一的结构性取舍是 `@tanstack/react-virtual` 不再被运行时导入
   （依赖仍在 `package.json` 与锁文件里，锁未改），由共用的 `useRowWindow` 承担两个长列表的窗口计算。
3. **真实闭环的最后一步被既有缺陷阻断**：390px 下双击结果条目会进入快速预览，详情抽屉不渲染
   （`EntryDetails.tsx` 窄屏分支与 `useNarrowWorkspace` 在基线 `5d9dc39` 已存在，非本卡引入）。
   建议单开一张卡修窄屏双击的交互归属；在该缺陷修复前 `read-only-trial-e2e` 对任何分支都为红。
4. **本轮新发现的既有缺口**：落一次复核会让服务端把复核自己的报告作为该库结果的新版本落盘，而页面
   判定「要不要重读切片」的键没有变化，于是页面继续显示复核前的版本，此时导出会被 409
   `dedup_version_conflict` 拒绝。浏览器试运行因此把导出排在复核之前；页面状态刷新策略不在本轮四张卡
   范围内，**未修**，建议单开卡。
5. 真实租约下对着 PostgreSQL 的复核链路已在本轮补齐（见第四轮第 3 项）；**「迟到复核不得覆盖新报告」
   这一竞争仍不在试运行里**（worker 单线程串行认领），继续由进程内确定性用例
   `DedupRecheckFenceTests.ALateVerdictNeverReplacesANewerAnalysisOfTheSameLibrary` 覆盖（含反向探针）。

## 关键取舍

报告保留是**有界进程内**的（8 份 / 每份 ≤20000 项），未新增迁移、未新增持久表；服务重启后报告不可读，
页面显示「本次结果已不可读取」而非空结果。这是本卡明确记录的边界，跨重启可读需要一次新迁移并由协调裁决。
