# ALIGN-001 — 仓库与旧架构实现对齐

- 里程碑：V0.1（审查与对齐，不是发布验收）
- 所有者：architecture-coordinator
- 分支：codex/align-001-repository-architecture-alignment
- Worktree：C:\YOKI\Codex\AssetLibrary-worktrees\ALIGN-001
- 依赖：M0-009、V01-014；集成 ALIGN-002、ALIGN-003、ALIGN-004 的独立修复。

## 目标与验收

盘点全部受版本控制的代码，对照 NAS 旧架构、旧实现和当前 ADR；保留两边 Git 历史，合并遗漏的保护性修复，修正已证实的偏差和过时说明，提交逐模块审查、代码清单和证据。最终本地 main 与 NAS main 指向相同提交，既有工作区的未提交内容和历史证据得到保留。

## 必读上下文

按 AGENTS.md 顺序读取启动页、需求、总体架构、版本门禁、并行工作流、编码原则与质量策略；结合 docs/23_M0架构冻结与质量门禁.md、docs/adr/、contracts/、eng/v0.1-alpha-readiness.json 及各任务交接。

## 所有权与允许修改

- 协调状态、任务图与登记、启动说明和审查报告。
- 整合 origin/main 的 M0-002/M0-004 实现与测试，保留后续 Windows 证据及 partial 状态。
- 评审并合并 ALIGN-002 扫描边界、ALIGN-003 Web 请求/会话修复及 ALIGN-004 服务 Spike 保护。
- 修复证据充分的仓库检查与文档一致性问题；公共契约、数据库迁移另行裁决。

禁止覆盖独有提交、修改真实资产、启用生产写/Explorer/Provider/发布功能、删除历史证据、以 skipped 充当通过、引入新技术栈或依赖。

## 复用与验证

复用现有端口、生成 SDK、迁移清单及架构/发布门禁，不复制业务规则。基线使用 python -I -B scripts/verify_repository.py。稳定后 review 完整 diff，再按 tests/architecture/ci-tiers.json 执行 fast-merge 和受影响平台验证。破坏性夹具限 .runtime/sandbox-storage/ 或系统临时目录。缺失平台、大文件与 soak 证据继续保持阻断。

完成时更新状态，写入 .codex/handoffs/ALIGN-001/summary.md、result.json、tests.md，记录起始提交、合并决策、结果、风险、优化次序和同步读回。

## 数据库单一所有权裁决

主协调器在 ALIGN-001 中独占追加 AssetIdentity 迁移 13 及 manifest/对应测试：真实 PostgreSQL 16.15 已复现完整文件名搜索两侧 token 不一致。仅修正查询分词以匹配既有生成列，不修改迁移 1–12、不变更公开函数签名、索引、权限、角色或 Host 功能。用真实查询、撤权、100k 索引及备份/恢复/升级回归验证。
