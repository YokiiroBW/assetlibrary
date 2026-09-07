# ALIGN-003 — Web 请求截止时间与权限缓存对齐

## 任务

- Task ID：`ALIGN-003`
- 里程碑：`V0.1`，不声明版本完成或解除任何发布门禁
- 模块 / owner：`Web read-only adapter` / `web-gateway-owner`
- 分支：`codex/align-003-web-request-deadline`
- Worktree：`C:\YOKI\Codex\AssetLibrary-worktrees\ALIGN-003`
- 依赖：已合并的 `V01-006`；主协调线程批准本次独占修改范围
- 状态：`ready_for_review`；实现 commit `7a1ed4203f8fd2de695e270ef543652839abe551`

## 目标与验收

给包含响应正文读取的 AssetLink 浏览器请求施加实际 5 秒截止时间；超时必须中断请求、显示可重试错误，调用方取消继续保持取消语义。复现并修复认证失效、明确拒绝或不存在响应后的旧条目/详情残留；重新认证后重新建立所有查询状态，不能显示上一身份的数据。暂时网络错误仍是错误，不能伪装成空目录成功。

## 必读上下文

- `AGENTS.md`、`.codex/START_HERE.md`
- `docs/01_核心原则与范围边界.md`、`docs/02_已确认需求基线.md`
- `docs/05_总体架构与技术框架.md`、`docs/06_AssetLink_WebDAV_API_MCP.md`
- `docs/13_权限分享安全通知与WebDAV.md`、`docs/16_版本路线与验收门禁.md`
- `docs/17_Codex并行开发工作流.md`、`docs/22_编码与架构开发原则.md`
- `.codex/policies/CODE_QUALITY.md`、`.codex/tasks/V01-006.md`、对应 handoff
- `contracts/assetlink/**`、生成 TypeScript SDK、`apps/web/**`、`tests/web/**`

## 允许修改

- `apps/web/src/**`
- `tests/web/**`
- 本任务包与 `.codex/handoffs/ALIGN-003/**`

## 禁止修改

- 数据库、GatewayAuth、所有公共契约与生成 SDK
- 根级或 Web 依赖清单、锁文件、工具版本
- 协调器状态登记、其他任务目录、Host 与发布/功能门禁

## 架构、复用与约束

- 复用既有 `AssetLinkClient`、生成 SDK、AbortController、query-state 与 React 工作界面。
- 网络仍只在现有 AssetLink 适配器内；保留 `timeout_ms`，不新增第二客户端或服务端业务规则。
- 状态清理只消费服务端返回的 HTTP 结果，不在客户端重新判断资源库授权。
- 不新增语言、框架、运行时或依赖；不读取或修改真实资产，不连接生产数据库。
- 单个截止时间与取消监听为 `O(1)`；分页与虚拟化保持现有 50 万资产边界。
- 仅在当前独立 worktree 写入；浏览器测试使用已有隔离运行目录与本地 fixture。

## 验证

- 先复现跨身份缓存残留，随后增加无响应、迟响应正文、调用方取消、重试、401/403/404 清理和暂时网络错误测试。
- 稳定实现后做一次完整 diff review，再运行真实 Web 格式、lint、typecheck、build、完整 Chromium 浏览器回归、依赖审计/许可证/体积、源码与架构/契约门禁。
- 使用固定 Node.js 24.20.0 与 pnpm 11.19.0；依赖 frozen install；覆盖桌面 1440px 与窄屏 390px。
- 不运行无关 .NET 或数据库全套；数据库搜索疑点只读交回迁移 owner 裁决。

## 完成标准

- [x] 实际截止、正文读取取消、错误与重试完成
- [x] 跨身份和明确拒绝后的旧数据显示已修复
- [x] 自动化负向测试会在原实现失败，在新实现通过
- [x] Web 原生命令、源码/依赖/架构/契约检查通过
- [x] 最终 diff、标准交接与可引用 commit 完成

## 交接

提交 `.codex/handoffs/ALIGN-003/{summary.md,result.json,tests.md}`，列出变更、复用、验证、风险、技术债和合并次序。未启用的认证 Host 与发布门禁继续保持原状。
