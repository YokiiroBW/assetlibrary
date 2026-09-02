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

## V0.1 启动后的共享 owner

- 根 solution、集中版本和构建锁：`build-foundation-owner`；
- `database/migrations/**` 与 `migration` ledger：`database-migration-owner`；
- `contracts/assetlink/**`：`assetlink-contract-owner`；
- `contracts/providers/**`：`provider-contract-owner`；
- `packages/sdk/assetlink/**`：`sdk-generation-owner`，消费者禁止手改生成代码；
- 统一错误码、权限和操作计划契约：由主协调线程在创建首个任务前各指定一个 owner；
- `.codex` 项目状态、任务图、注册表和发布版本：仅主协调线程。

V0.1 任务图和建议合并顺序见 `docs/23_M0架构冻结与质量门禁.md`。同一阶段也不得并行修改以上单一 owner 区域；跨 owner 修改先提交 Contract Change Proposal。

## M0 首轮任务边界

M0-002、M0-003、M0-004 与 M0-007 可在 M0-001 完成后并行启动：

| 任务 | 独占验证区域 | 明确不拥有 |
|---|---|---|
| M0-002 | `tests/spikes/windows-shell/**`、Shell Spike 记录 | `contracts/**`、核心服务实现 |
| M0-003 | `contracts/assetlink/**`、官方/生成 SDK 适配、AssetLink Spike 测试 | 其他契约、数据库迁移、客户端实现 |
| M0-004 | `tests/spikes/server-packaging/**`、发行 Spike 记录 | 根级依赖锁、发布版本、运行时实现 |
| M0-007 | `tests/spikes/performance/**`、模拟器与故障夹具 | 领域模型、生产文件操作、共享契约 |

以上目录当前可为空；表格是首轮任务启动时的所有权约定，不代表技术栈或 M0-009 质量门禁已经冻结。

## M0 第二轮任务边界

M0-003 完成后，M0-005 与 M0-008 可并行，且不与仍在收口的 M0-002、M0-004
争用文件：

| 任务 | 独占验证区域 | 明确不拥有 |
|---|---|---|
| M0-005 | `database/**`、`tests/spikes/postgres/**`、PostgreSQL Spike 记录 | 既有共享契约、生产服务实现、根级依赖 |
| M0-008 | `contracts/providers/**`、`tests/spikes/provider-sandbox/**`、Provider 隔离 Spike 记录 | `services/**`、其他共享契约、生产 Provider 实现 |

M0-005 是本轮数据库迁移与数据库对象命名的单一所有者；M0-008 是 Provider
候选契约的单一所有者。任何超出表格的共享变化仍由主协调线程裁决。

## 边界规则

文件所有权不等于允许绕过模块公开接口。即使某线程拥有多个目录，也必须遵守依赖方向和模块数据所有权。
