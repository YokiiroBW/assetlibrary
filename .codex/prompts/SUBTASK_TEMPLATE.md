# Codex 子任务模板

## 任务

- Task ID：`<TASK-ID>`
- 标题：`<TITLE>`
- 所属里程碑：`<MILESTONE>`
- 负责人线程：`<OWNER>`
- 分支：`codex/<task-id>-<slug>`
- Worktree：`../worktrees/<task-id>`

## 目标

用一句话描述要交付的可验证结果。

## 必读上下文

- `AGENTS.md`
- 相关需求文档：
- 相关 ADR：
- 相关 contracts：

## 允许修改

- `<path>/**`

## 禁止修改

- 数据库迁移（除非任务明确拥有）
- `contracts/**`（除非已批准）
- 根级版本与依赖锁
- 其他任务拥有的目录

## 架构与复用说明

- 所属模块及数据所有者：
- 允许依赖的公开接口/契约：
- 计划复用的既有用例、SDK或组件：
- 禁止直接引用的内部模块：
- 是否新增语言、框架、运行时或重大依赖：否；若是必须附已批准 ADR。
- 需要执行的架构测试：

## 约束

- 物理文件权威；
- 原文件安全；
- 50 万资产性能；
- 不阻塞 Explorer；
- 无 AI 也可用；
- 使用隔离测试目录；
- 遵守 `docs/22_编码与架构开发原则.md` 与 `.codex/policies/`；
- 不复制权限、传输、同步、垃圾桶等核心业务逻辑。

## 完成标准

- [ ] 功能或验证结果完成
- [ ] 自动测试通过
- [ ] 架构边界与契约测试通过
- [ ] 未引入重复核心逻辑或无批准技术栈
- [ ] 失败与恢复路径覆盖
- [ ] 文档与契约更新
- [ ] 标准交接文件已生成
- [ ] 已提交 commit

## 交接要求

写入：

- `.codex/handoffs/<task-id>/summary.md`
- `.codex/handoffs/<task-id>/result.json`
- `.codex/handoffs/<task-id>/tests.md`
