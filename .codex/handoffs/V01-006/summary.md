# V01-006 交接摘要

## 完成状态

`ready_for_review`

- 分支：`codex/v01-006-web-api-read-only-browse`
- 实现 commit：`7776deccacd283d8618731fbb6cd6c6602496eeb`
- Worktree：`C:\YOKI\Codex\worktrees\V01-006`
- 交接 commit：本交接三件套提交后的分支 tip；精确值由最终 `git log -1` 和协调器合并记录给出，避免 commit 内容自引用。

## 完成内容

- 新增 GatewayAuth 只读合同、权限政策、输入/结果边界、超时与取消传播，以及 `IAuthorizedReadModelQuery` 端口。
- 新增 Npgsql 适配器：所有读取都使用参数化调用、显式 `REPEATABLE READ` 只读事务、最多 101 行的数据库边界和 Data Protection 保护的过滤条件绑定游标。
- 新增 AssetLink HTTP 适配器，只接受已认证主体的 `libraries.list`、`entries.browse`、`assets.search`；请求体最多 64 KiB，匿名、写操作、未知操作、畸形字段、错误媒体类型和无效 UTF-8 均失败关闭。
- 新增 React 只读工作界面：资源库切换、目录进入/返回、名称/相对路径搜索、分页、详情、加载/空/错误/登录失效状态，以及窗口化列表和窄屏重排。
- 新增生产迁移 7–9，分别由 LibraryStorage、AssetIdentity、GatewayAuth 拥有权限状态、物理事实只读投影/索引和最终授权查询入口。
- 激活 Web frozen install、格式、lint、类型、构建、Chromium、audit、许可证/完整性/体积和源码边界 CI 门禁。

## 关键决策

- 权限在数据库查询阶段执行：四个 `gateway_auth.*authorized*` 入口先解析未禁用主体，再把 `library_storage.library_permission_read_projection` 与资源库连接；系统管理员是显式分支，无权限和不存在都返回相同空结果/404。
- GatewayAuth runtime 只能执行最终 `SECURITY DEFINER` 函数，不能读取主体表、权限投影或 AssetIdentity 投影；源模块只把批准的只读视图/函数授予 GatewayAuth owner。
- 浏览、资源库列表和搜索都使用稳定 keyset 游标；游标以 Data Protection 防篡改并绑定操作与过滤条件，但每一页仍重新执行权限查询，游标从不充当授权凭证。
- 基础搜索只使用 PostgreSQL 16 同库 `tsvector`/GIN 索引查询文件名和规范相对路径，不建立 SearchDedup 写模型，也不读取文件内容。
- Web 只消费生成的 AssetLink TypeScript SDK；请求变化会 abort 旧请求并用 generation 防止陈旧响应覆盖，服务响应还须匹配 request ID 且最多 100 项。

## 修改文件

- Gateway/API：`services/core-server/Modules/GatewayAuth/**`、`services/core-server/Adapters/AssetLink/**`，以及最小 LibraryStorage 权限枚举、Npgsql/solution 接线。
- 数据库：迁移 `0007`–`0009`、manifest 和说明。
- Web：`apps/web/**`、`tests/web/**`、Web 依赖/源码策略。
- 验证/CI：WebGateway MSTest、数据库/repository 测试、CI tiers/workflow 及依赖锁。完整清单见 `result.json.changed_files`。

## 模块边界、依赖方向与复用

- 保持 `HTTP/Web -> GatewayAuth Application -> GatewayAuth Contracts/Domain`；Npgsql 与 Data Protection 只存在于 Infrastructure/adapter，Web 不直连 PostgreSQL。
- LibraryStorage 仍是资源库授权唯一 owner；AssetIdentity 仍是 present 物理事实唯一 owner；GatewayAuth 只编排经批准的只读投影，没有跨模块写表、外键或 Infrastructure 引用。
- 复用 V01-003 的连续 checksum manifest、模块 owner/最小权限、备份恢复与 PostgreSQL 16.15 harness；复用 V01-004/V01-005 的值对象、应用端口、结构化日志和 .NET 门禁。
- 复用冻结的 AssetLink .NET/TypeScript 生成 SDK，没有修改 `contracts/assetlink/**` 或手写第二套 envelope codec。

## 新语言、框架或重大依赖

- 无新主语言；在已批准 TypeScript 预算内启用 React `19.2.8`、React DOM `19.2.8`（MIT）和 TanStack React Virtual `3.14.10`（MIT）。Vite 报告生产构建为 230.68 kB JavaScript、8.92 kB CSS（gzip 72.20/2.76 kB），运行于标准 Chromium 类浏览器；移除路径是保留 AssetLink client/hooks 合同并替换视图层。
- Npgsql `10.0.3`（PostgreSQL License）是唯一新增服务端包，纯托管、跨 Windows/Linux；安全更新跟踪 `github.com/npgsql/npgsql/security/advisories`，可通过替换 `IAuthorizedReadModelQuery` adapter 后移除。
- Node.js `24.20.0`、pnpm `11.19.0`、Vite `8.2.2`、TypeScript `6.0.3`、Prettier `3.9.6`、Playwright `1.62.1` 及 React 类型包只用于构建/测试。精确许可证和官方 security advisory 地址锁定在 `eng/web-dependency-policy.json`；Vite/Playwright/Prettier 可随 Web 构建或浏览器门禁整体移除，不进入服务端运行时。

## 共享契约或数据库变化

- `LibraryStorage.Contracts` 新增 `LibraryAccessLevel`；GatewayAuth 新增模块内只读 V0.1 body/分页合同。
- production manifest 从 1–6 连续扩展为 1–9；既有迁移未修改。迁移 SHA-256：7=`000be3c80ffe3a45b907ce5f9755488411aacb40f174bfa722ea1fbcb6f0194d`，8=`41c3e9c8214963e3b6db77b5e9fff3ea0f5b2b324d94713f36f6505c51f3cb20`，9=`0da9794574a7ad251fb8163463a597cdf0d465a32a64ad5de2b274638d62405e`。
- canonical AssetLink wire contract 和生成 SDK 均未改变；GatewayAuth 操作 body 是模块拥有的应用合同。

## 测试结果

- 唯一自动测试计数：248 passed，0 failed，0 skipped（.NET 103、database 53、repository 37、architecture 14、SDK Python 14、AssetLink contract/spike 21、Chromium 6）。
- 另有 TypeScript 生成 SDK 5/5 和 Kotlin/JVM 生成 SDK build 通过；为保持既有 handoff 统计口径，不重复计入跨语言同合同测试。
- Web Gateway MSTest 13/13；真实 PostgreSQL integration 14/14；Release build 0 warning / 0 error；完整命令见 `tests.md`。

## 架构测试与质量门禁

- .NET 10.0.111 locked restore、format、Release build、全 solution 测试、NuGet vulnerability/license/source 门禁通过；8 个项目、18 个锁定包均已审计。
- PostgreSQL 16.15 required 套件、迁移 checksum、备份/恢复、并发迁移、最小权限、重启和 10 万目录索引计划通过。
- Web frozen install、format/lint/type/build、6 个真实 Chromium 场景、npm audit、许可证/哈希/体积和只读源码门禁通过。
- repository verifier、architecture baseline、AssetLink 生成/contract、`v0.1-start` 和 `git diff --check` 通过。

## 文件安全、权限与性能影响

- 没有读取或修改真实资产/NAS，没有触碰注册表、Explorer、Provider、系统服务或生产数据库；所有数据库、浏览器和工具链数据均在任务 `.runtime`，只监听 loopback。
- API 永不返回绝对根、主体信息、内容哈希、扫描内部字段或精确总数；日志仅包含 request ID、固定操作、结果码和耗时，不记录主体、搜索词、路径、body 或异常正文。
- 数据库单页最多读取 101 行、外部最多返回 100 行；10 万同目录 browse 命中 `filesystem_entry_browse_index`，搜索命中 `filesystem_entry_path_search_index`。
- 浏览器 100 行页实际 DOM 保持少于 40 个条目；Vite 报告 JS+CSS 239.60 kB，低于 286,720-byte 联合预算。

## 技术债、已知问题与风险

- 尚无可执行生产 host、身份提供者/登录签发、Npgsql connection composition 或部署配置；本切片是已验证 adapter/UI，不应描述为已部署产品。
- 生产多实例必须配置共享、持久的 ASP.NET Data Protection key ring；否则重启/实例切换会让未完成游标失效，客户端需从第一页重载。
- 权限授予/撤销写 API 与管理 UI未开放；当前权限种子只能由后续受审计管理用例写入，不能给 runtime 直接表写权限。
- 迁移 8 为既有 `filesystem_entry` 增加 stored generated columns 和索引；大库上线需独立评估锁时长、磁盘空间和可回滚部署窗口。
- 基础搜索不包含标签、评分、OCR、AI、正文或模糊排序；50 万真实 NAS/P95、生产数据库空间不足和发布演练仍由 scheduled-release 门禁关闭。

## 建议合并顺序

V0.1 顺序 `6`。分支基于已合并的 V01-002/V01-004/V01-005，可由主协调线程验证交接与分支差异后 fast-forward 合并。

## 下一步

- 协调器 fast-forward 合并并更新 `.codex/project-state.json`、`.codex/task-registry.json` 与任务图。
- 冻结后续单一 owner 任务，优先补生产 CoreServer host/认证组合与权限管理用例；不得顺带开放物理写、Provider、Explorer、Developer API 或发布。
- 在启用多实例或生产数据迁移前，完成 Data Protection key ring、迁移锁/空间、50 万规模和恢复演练。

## Codex 线程链接（可选）

未填写；仓库 commit 与交接文件是权威证据。
