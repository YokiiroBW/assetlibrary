# TS-066 摘要

**任务**：资产库精确查重网页工作台 — 在 AssetLibrary 自己的网页里完成「选已登记库 → 后台有界精确
查重 → 分页查看重复组与未验证项 → 重新核对 → 导出绑定版本的 JSON 计划」。

**状态**：`ready_for_review`（第二轮返修）。分支 `work/ts-066`，基线
`5d9dc39b926366076bb419c040cb000dd01455b5`；第一轮从 `2d890e72d9a49cc49d1ce91b014c25b433e0bb7e`
接续（依据 `TS-066-review-fixes.md`），第二轮从 `e492d28` 接续（依据 `TS-066-review-fixes-2.md`），
第二轮提交 `eacf26edd60b15a52733431610ec87d3a4c660c0`。本地提交，未推送、未合并、未部署。

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
| 全量 .NET 测试                                                                      | 通过（**516 通过 / 53 跳过 / 0 失败**，跳过均为既有 PostgreSQL/POSIX 条件用例）                            |
| `verify_repository.py`                                                              | **失败，且只有体积预算一个叶子**（其余架构检查全通过）                                                     |
| `validate_dotnet_source.py`                                                         | 通过（642 个 C# 文件）                                                                                     |
| `check_release_gates.py --target v0.1-start`                                        | `RELEASE_GATE_ALLOWED`                                                                                     |
| `validate_web_source.py`                                                            | 通过                                                                                                       |
| `validate_web_dependencies.py --require-build-artifacts`                            | **失败**：仅体积预算（依赖/许可证/允许清单部分通过）                                                       |
| prettier / tsc / vite build                                                         | 通过（入口 297503 + 33876 + 7959 = **339338 B**；CSS 32394 B）                                             |
| Chromium 回归（全量）                                                               | 83 通过 0 失败（本卡 17 项）                                                                               |
| 真实 PostgreSQL/HTTPS/Chromium 端到端                                               | **已执行，最后一步被既有缺陷阻断**（见 `docs/handoffs/TS-066.md` 第 7.4 节，探针输出在案），未记为通过     |

## 需要协调裁决

1. 断点：规格写「≥1200px 双列」，壳的折叠断点是 1199px；当前按规格字面实现，如需严格对齐只改一处媒体查询。
2. **体积预算仍差 11658 B**：门禁已按要求恢复 327680 / 360448 且删除了上一轮自行加入的说明，口径未改。
   门禁（`validate_web_dependencies.py`）对 `dist/assets/*.js` **全量求和**，因此路由级动态导入只把字节从
   入口分包搬到异步分包。第二轮把所有行为等价的削减做完后实测：入口 297503 + `DedupWorkbench` 33876 +
   `LibraryAdmin` 7959 = **339338**（对 327680 超出 11658）。查重功能的净边际成本实测 34575 B，而基线的
   余量只有 25587 B，因此**无法只靠消除浪费闭合**。恢复绿只剩三条路，均超本卡授权：
   ①把门禁改成「入口分包 + 各自异步分包」的分项预算；②明确退役一个规格要求的既有视图；③批准一次有记录的
   预算上调。逐项实测（含被否决项）在 `.codex/handoffs/TS-066/tests.md` 第 3.2 节。
3. **真实闭环的最后一步被既有缺陷阻断**：390px 下双击结果条目会进入快速预览，详情抽屉不渲染
   （`EntryDetails.tsx` 窄屏分支与 `useNarrowWorkspace` 在基线 `5d9dc39` 已存在，非本卡引入）。
   建议单开一张卡修窄屏双击的交互归属；在该缺陷修复前 `read-only-trial-e2e` 对任何分支都为红。
4. 真实租约下对着 PostgreSQL 的复核链路端到端验证仍缺失（围栏分工已由合成探针覆盖）。

## 关键取舍

报告保留是**有界进程内**的（8 份 / 每份 ≤20000 项），未新增迁移、未新增持久表；服务重启后报告不可读，
页面显示「本次结果已不可读取」而非空结果。这是本卡明确记录的边界，跨重启可读需要一次新迁移并由协调裁决。
