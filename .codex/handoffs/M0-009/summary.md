# M0-009 交接摘要

## 完成状态

`ready_for_review`

M0-009 已完成架构、语言预算、所有权和 CI 合同收敛。当前不存在 `blocking_v0_1_start`，因此主协调线程复核并合并后可创建受控 V0.1 实现任务；这不等于 V0.1 可发布，也不改变 M0-002、M0-004、M0-006、M0-008 的 `partial` 状态。

## 完成内容

- 审计 M0-001 至 M0-008 的实现 commit、交接状态、测试计数、平台跳过、风险与 blockers。
- 新增 `docs/23_M0架构冻结与质量门禁.md`，明确区分允许编码、允许启用、允许发布和完成最终验收。
- 将 4 个 partial 任务的 12 个 blocker 按来源索引、审计 commit、分类、里程碑、owner、后续任务、解除条件和可执行 target 一一冻结到 `tests/architecture/m0-gates.json`。
- 冻结服务端、数据库、Web、Windows、Shell、Android、扩展、适配器和工具的单一语言/框架技术族，并区分 Spike 证据与预算级选择。
- 冻结 11 个核心模块的数据/政策所有权、迁移 owner、AssetLink/Provider 契约 owner 和生成 SDK owner。
- 将架构基线升级为真实阻断门禁，增加 13 个正反夹具测试和 fail-closed release gate 工具。
- 冻结 `fast-merge`、`platform`、`scheduled-release` 三层 CI，并新增对应 workflow 与原生工具链激活合同。
- 形成 V0.1 任务图、独占范围、依赖和建议合并顺序提案，供主协调线程在合并后创建。

## 关键决策

- 授权范围为 `authorized_for_scoped_implementation`；生产文件写入、不可信 Provider、Explorer、Docker/V0.1 发布仍默认关闭。
- 当前没有 `blocking_v0_1_start`。开放项分类为 7 个 `blocking_release_or_later_milestone` 和 5 个 `deferred_fail_closed`，没有 blocker 被丢弃或改写成通过。
- 服务端采用 C# 14 / .NET 10 LTS / ASP.NET Core 10；数据库保持 PostgreSQL 16.x，V0.1 不引入 Redis、第二数据库、独立搜索或消息集群。
- Web、Windows UI 和 Android 的版本族是预算级选择；首个生产清单必须固定精确补丁并同步激活原生编译、静态/类型、漏洞和许可证门禁。
- M0-002、M0-004、M0-006、M0-008 必须继续保持 `partial`，直到其各自证据和 gate 被后续任务真实关闭。

## 修改范围

修改仅涉及本任务 handoff、架构/质量政策、冻结文档与 ADR、仓库验证脚本、架构测试及 CI workflow。未修改产品实现、既有 Spike 证据、共享协议、数据库迁移或协调器状态。

完整文件列表见 `result.json.changed_files`。

## 模块边界、依赖方向与复用

模块：`architecture-quality-gate`，owner 为 `codex-agent-m0-009`。门禁只读取仓库文件，不被产品 Domain 或运行时依赖。强制方向保持 `Adapters/UI/Gateways -> Application -> Domain`，Infrastructure 只实现核心端口。

复用了现有 `validate_handoff.py`、`verify_repository.py`、M0 handoff、AssetLink/Provider 合同和标准库；没有复制权限、路径、哈希、传输、同步、垃圾桶、保护、审计或 Provider 调度业务逻辑。

## 新语言、框架或重大依赖

无。门禁和测试只使用现有 Python 标准库；workflow 使用仓库既有 GitHub 官方 checkout/setup-python action，并增加官方 upload-artifact action保存测试摘要。没有新增产品运行时包、根锁文件或第二构建系统。

## 共享契约或数据库变化

无。`contracts/**`、`database/migrations/**` 未修改。M0-009 只冻结其单一 owner、生成摘要规则和未来迁移门禁。

## 测试结果

- M0-009 架构正反夹具：13/13 通过。
- repository workflow：8/8 通过。
- AssetLink：21/21 通过。
- Windows Shell contract：16/16 通过；没有执行注册或真实 Explorer 操作。
- Windows file-safety：6/6 适用项通过，Linux 模块 1 项平台跳过。
- Windows Provider：12/12 适用项通过，18 项 Linux/POSIX 平台跳过。
- handoff、架构基线、repository verification、JSON、预期允许/阻断 target 和 `git diff --check` 通过。

详细命令、平台限制和一次非门禁诊断错误见 `tests.md`。

## 文件安全、权限、性能与兼容性

- 本任务未运行管理员、SCM/HKLM、Shell 注册、Docker、AppContainer、Linux namespace/cgroup、真实 NAS、1/20/100 GiB、断电或 soak 操作。
- 文件测试只使用系统临时目录；未触碰用户资产。
- 500k 定时 workflow 只生成元数据清单到 `.runtime/sandbox-storage/M0-007`，不物化资产原文件；当前 Windows 未重复声明其 Linux 性能基线。
- Shell 生产目录保持 inactive；Provider 和生产物理写入由机器 gate 默认关闭。

## 技术债、已知问题与风险

- 标准库文本/路径门禁是首道防线，不能取代 Roslyn、TypeScript、Gradle 和链接器依赖图；首个生产清单必须在同一提交激活对应原生门禁。
- 新增 GitHub workflow 尚未在远端 runner 执行；本机没有 YAML parser/actionlint，因此只完成了机器合同内容检查、人工语法审查和其中实际 Python 命令的本地验证。
- Windows 上执行 M0-007 全部单元测试时，5 项先通过，symlink 用例因当前令牌缺少 Windows 创建符号链接权限报 `WinError 1314`；没有为绕过环境边界修改既有 Spike。其 canonical Linux 证据仍为 M0-007 的 6/6 与 500k profile。
- V0.1 release 仍被 Docker、1/20/100 GiB 和写入持久性三项 gate 阻断；其他平台/功能 gate 详见 `m0-gates.json`。
- Web、WinUI 和 Android 为未激活技术预算，不得描述为已完成可运行验证。

## 建议合并顺序

1. 主协调线程独立检查本分支 diff、13 个架构夹具和 release target 输出。
2. 合并 M0-009 实现与交接提交。
3. 在主线把 M0-009 标记完成，并把项目状态更新为“允许 scoped V0.1 implementation，release gates open”；不得把四个 partial 上游改为完成。
4. 按 `docs/23` 的 V01-001 至 V01-009 提案创建任务，先建立 solution/原生 CI、SDK 生成和迁移 owner，再并行只读核心。

## Codex 线程链接（可选）

仅作为导航，不是唯一交接依据。
