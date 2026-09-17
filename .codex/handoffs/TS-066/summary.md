# TS-066 摘要

**任务**：资产库精确查重网页工作台 — 在 AssetLibrary 自己的网页里完成「选已登记库 → 后台有界精确
查重 → 分页查看重复组与未验证项 → 重新核对 → 导出绑定版本的 JSON 计划」。

**状态**：`ready_for_review`。分支 `work/ts-066`，基线
`5d9dc39b926366076bb419c040cb000dd01455b5`，提交 `91176e44483b83460f051f2f760559e74247763f`。
本地提交，未推送、未合并、未部署。

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
- **测试**：`tests/web/dedup-workbench.spec.mjs` 13 项 + `tests/web/dedup-fixtures.mjs`；
  `tests/dotnet/AssetLibrary.WebGateway.Tests/Dedup*WireTests.cs` 8 项线格式契约用例（直接断言真实 Host
  序列化出的键集合与页面解码器一致，并以反向探针确认断言有效）。

## 验证结论

| 门禁 | 结果 |
| --- | --- |
| Release 构建（`TreatWarningsAsErrors`） | 0 警告 0 错误 |
| 全量 .NET 测试 | 通过（503 通过 / 53 跳过，跳过均为既有 PostgreSQL/POSIX 条件用例） |
| `verify_repository.py` | 通过 |
| `check_release_gates.py --target v0.1-start` | `RELEASE_GATE_ALLOWED` |
| `validate_web_source.py` / `validate_web_dependencies.py` | 通过 |
| prettier / tsc / vite build | 通过 |
| Chromium 回归（全量） | 79 通过 0 失败（本卡 13 项） |
| 真实 PostgreSQL/HTTPS/Chromium 端到端 | **未执行，证据缺失**（本机无 PostgreSQL 与 docker） |

## 需要协调裁决

1. 断点：规格写「≥1200px 双列」，壳的折叠断点是 1199px；当前按规格字面实现，如需严格对齐只改一处媒体查询。
2. `eng/web-dependency-policy.json` 的体积预算按实测上调（JS 327680→344064、合计 360448→376832），
   并把实测依据写入同文件 `size_budget_notes`。未删减规格要求的视图。
3. 真实端到端证据缺失（见上表末行）。

## 关键取舍

报告保留是**有界进程内**的（8 份 / 每份 ≤20000 项），未新增迁移、未新增持久表；服务重启后报告不可读，
页面显示「本次结果已不可读取」而非空结果。这是本卡明确记录的边界，跨重启可读需要一次新迁移并由协调裁决。
