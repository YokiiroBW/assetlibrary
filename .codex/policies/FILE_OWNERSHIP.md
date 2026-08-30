# 并行任务文件所有权

## 单一所有者区域

以下内容不能被多个工作线程同时修改：

- `contracts/assetlink/**`
- `contracts/operations/**`
- `docs/adr/**`
- 数据库迁移目录（后续创建）
- 根级依赖锁、版本号与发布配置
- `AGENTS.md`
- `docs/22_编码与架构开发原则.md`
- `.codex/policies/**`
- `tests/architecture/**`
- `.codex/project-state.json`
- `.codex/task-registry.json`

## 模块默认所有权

- 服务端核心：`services/core-server/**`
- Worker 监督与 Provider 宿主：`services/worker-supervisor/**`、`providers/**`
- Web：`apps/web/**`
- Windows 独立客户端：`apps/windows-client/**`
- Windows Shell：`apps/windows-shell/**`
- Android：`apps/android/**`
- 浏览器扩展：`apps/browser-extension/**`
- WebDAV：`gateways/webdav/**`
- MCP：`integrations/mcp/**`
- SDK：`packages/sdk/**`
- UI 共享规范：`packages/ui/**`

确需跨边界修改时，先提交变更提案，由主协调线程重新分配所有权。

## M0 首轮任务边界

M0-002、M0-003、M0-004 与 M0-007 可在 M0-001 完成后并行启动：

| 任务 | 独占验证区域 | 明确不拥有 |
|---|---|---|
| M0-002 | `tests/spikes/windows-shell/**`、Shell Spike 记录 | `contracts/**`、核心服务实现 |
| M0-003 | `contracts/**`（仅由该任务及协调线程批准后修改） | 数据库迁移、客户端实现 |
| M0-004 | `tests/spikes/server-packaging/**`、发行 Spike 记录 | 根级依赖锁、发布版本 |
| M0-007 | `tests/spikes/performance/**`、模拟器与故障夹具 | 领域模型、生产文件操作 |

以上目录当前可为空；表格是首轮任务启动时的所有权约定，不代表技术栈或 M0-009 质量门禁已经冻结。

## 边界规则

文件所有权不等于允许绕过模块公开接口。即使某线程拥有多个目录，也必须遵守依赖方向和模块数据所有权。
