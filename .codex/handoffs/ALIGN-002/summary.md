# ALIGN-002 交接摘要

## 完成状态

`ready_for_review`。实现提交 `4f0fcc2ffe0c29709d212baae71401d01e9e5d41`，分支 `codex/align-002-scan-physical-boundary`。只读核心修复可独立合并；不表示 V0.1、生产写入或任何平台完成。初版测试遗留的 7 个无文件 fixture 根和 2 个空子目录清理被自动批准审查拒绝，详见 tests.md；最终回归未新增残留。

交接提交是本交接三件套提交后的分支 tip，精确值由最终 `git log -1` 和协调器合并记录提供，避免提交内容自引用。`result.json.commit` 指向已经验证的实现提交。

## 完成内容

旧代码只做词法根规范化，并只在发现子目录时读取一次 reparse 属性。根或中间祖先为 junction 时，探测返回 Available；已 yield 的普通目录被替换为库外 junction 后，首次扫描仍会提交完整快照。隔离 Windows 实证获得 6 个回归失败和 1 个原有行为通过。

- LibraryStorage 在枚举前检查根及祖先，以既有 Inaccessible 状态拒绝联接别名根。
- ScanReconciliation 在打开目录、读取下一条和确认 EOF 前复核目录及祖先；替换后返回 directory_reparse_point，既有初扫用例中止 staged observations。
- 普通子联接仍记录为 ReparseDirectory，绝不递归。
- 新增 8 个沙箱回归：根/祖先联接、yield 后子目录替换、枚举根替换（下一条/EOF）和正常子联接。
- 测试夹具登记自建联接，先非递归清理联接，避免 Windows 递归清理异常遮蔽断言。

## 关键决策

只收紧只读物理边界，不新增公开契约；两模块各自保留最小祖先属性检查，使用各自错误语义，不建立跨模块 Infrastructure 引用或万能工具层。

## 修改文件

两个模块 Infrastructure、ReadCore 的 RepositorySandbox 与三个新测试支持/测试文件，以及本任务包和交接。完整列表在 result.json。

## 模块边界、依赖方向与复用

复用 ILibraryRootProbe、IReadOnlyFileDiscovery、FileDiscoveryException、InitialReadOnlyScanService 的 abort/日志，以及 RepositorySandbox、TestObservationSink、TestScanJournal。Application、Domain、Host 组合与状态机未修改。

## 新语言、框架或重大依赖

无。

## 共享契约或数据库变化

公开类型、字段、AssetLink 和数据库均无变化。发现器通过既有 `FileDiscoveryException.FailureCode` 增加 `directory_reparse_point`、`directory_unavailable`、`directory_metadata_failed` 失败结果；既有初扫用例将它们记录并返回为 `DiscoveryFailed`。

## 测试结果

最终 86/86，0 failed、0 skipped：ReadCore 51、architecture 14、AssetLink contract 21。locked restore、format verify、Release build（0 warning/error）通过。

## 架构测试与质量门禁

源码与架构基线通过。production-file-writes 仍被 M0-006-G2 阻断，读取原生命令退出码为 3；没有改公共门禁。交接补全阶段只核对结构、文件清单和 diff，不重复计入或重跑已经通过的源码测试。

## 文件安全、权限与性能影响

生产代码只增加元数据读取，不写、移、删资产。权限、身份、数据库和协议兼容性不变；联接根现在被拒绝是预期安全收紧。流式枚举与有界观察批次保持。N 个条目、D 个目录、最大路径深度 H 下，新增属性查询为 O((N + D) × H)，祖先游标内存 O(H)；没有整库物化。尚无 50 万真实 NAS 延迟证据。

## 技术债、已知问题与风险

- 属性检查与系统调用之间仍有微观 TOCTOU；未证明普通目录身份替换、其他根别名或非协作 Linux namespace mutation 安全。no-follow/句柄、Worker 隔离仍需后续证据。
- 同步文件系统元数据调用无法中止内核/NAS 卡死，保留 V01-004 限制。
- 本次实际运行 Windows junction 回归；Linux symbolic-link 分支未在此执行。
- 历史 fixture 目录清理被自动批准审查拒绝，不影响代码合并；准确路径见 tests.md，已交主协调处理。Git 工作树干净仅指版本控制层面，不表示忽略的运行时目录为空。

## 建议合并顺序

本轮建议顺序 `1`：先实现提交 `4f0fcc2`，再交接提交；可在协调文档对齐前合并。其他模块无需迁移或 SDK 再生成。任务登记与项目/里程碑状态由主协调线程更新。

## 下一步

主协调审查与合并；后续生产扫描仍需 no-follow/Worker、NAS 断连与容量证据，不能从此修复外推完整生产能力。

## Codex 线程链接（可选）

仅作为导航，不是唯一交接依据。
