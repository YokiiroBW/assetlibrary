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

## 边界规则

文件所有权不等于允许绕过模块公开接口。即使某线程拥有多个目录，也必须遵守依赖方向和模块数据所有权。
