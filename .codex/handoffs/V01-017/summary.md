# V01-017 — 持久资源库与隔离首次扫描

状态：ready_for_review。2026-09-07。分支 `codex/v01-017-persistent-read-only-library-scans`。

实现提交：`4a4df6a`（迁移14）、`9856230`（worker）、`09db868`（持久模块）、`25aba97`（接线与原生验证）。后续交接提交记录测试断言修正，不修改实现语义。已合入协调器 `1f39755` 的真实 worker 入口用于验证；没有自行修改 Host、Web、GatewayAuth C# 或根依赖。

## 已完成

- 管理员入口可调用 LibraryStorage 登记已配置 source_key 下的物理目录。存储源ID、大小写与允许根由部署者配置；调用者+操作幂等键绑定请求，同根/父子重叠由预检及SQL并发锁共同拒绝。
- 每库持久可用性独立于同源其他库；后台探测和扫描启动使用受限子进程重新探测，NAS/目录恢复后无需等待缓存周期才能重试。
- 首次观察分批短事务暂存，完整发现后才原子提交；AssetIdentity会话独占每库锁。已提交索引拒绝新首次扫描，保留稳定ID。
- 持久接受→幂等入队→限量领取→心跳/取消→最终提交guard→终态。崩溃可补投；过期未提交尝试仅清本次stage；已提交snapshot按scan_id与task/run关联核实后修复结果。
- 同一发布Host在配置/DB/密钥加载前调用 `--read-only-worker probe|scan`。请求16KiB、输出帧64KiB；必须终帧、计数、exit0同时成立。入口不读资产内容、不写原文件；stdin写入、管道无响应、取消与退出均有截止、杀树和reap。宽目录树改为按深度持有枚举器。

## 组合与所有权

模块跨界仅通过 LibraryStorage/AssetIdentity/TaskHealth 的 Contracts。Root组合 LibraryRegistrationService、LibraryAvailabilityService、InitialScanCoordinator；HTTP管理员鉴权仍由 GatewayAuth 唯一策略负责。

最终 coordinator 构造顺序为 `(scanStore, libraryStore, availability, snapshot, tasks, taskExecution, recovery, executor, scanOptions)`；操作上下文为 `ManagementOperation(principalId, idempotencyKey)`。TaskHealth store应传 `InitialScanExecutionOptions.TaskType` 过滤领取类型。详细可编译组合已发协调器；ReadOnlyTrialFixture保存同一真实模块接线。

三个已发生的Npgsql连接/事务模板按协调器批准提取到 `Infrastructure/Postgres/ModulePostgresSession`，固定角色枚举不接收外部角色字符串。各模块保留SQL、参数与返回模型；真实负向测试证明仍不能写其他schema。没有新增语言、框架、主要依赖、数据库或通用Repository。

## 数据库与兼容性

连续追加14–18，分别由GatewayAuth、LibraryStorage、AssetIdentity、ScanReconciliation、TaskHealth owner执行。旧1–13和固定角色定义完全不变，遵循备份优先、前进迁移及空库恢复。

普通catalog/read投影列、AssetLink envelope、生成SDK保持兼容。准备管理员恢复只读、支持停用管理员、幂等重放取原expected version，最终恢复仍走原CAS。TaskHealth guard持有自己的租约行锁期间，通过AssetIdentity公开端口提交其自身事务；提交响应丢失依靠不可变snapshot恢复，不声称跨模块分布式原子事务。

## 验证

精确环境与命令见 tests.md。锁定restore、format验证、零警告Release build、仓库/源码/架构/契约检查均通过。完整ReadCore 77/77、TaskHealth 32/32、AssetLink 13/13；真实PostgreSQL数据库全套69/69。ReadCore由数据库测试以真实Host子进程执行，零跳过；包含26项新增原生/故障场景。不同层的嵌套测试不重复汇总为产品完成比例。

## 风险和剩余范围

- 本任务仅完成首次只读索引的持久集成；实时监听、增量/通用重扫、完整哈希、内容下载、预览与真实文件整理仍未开放。HTTPS/Web最终浏览器闭环与交付包由协调器验收。
- observation默认256、上限1024；DFS额外状态O(深度)，元数据与祖先复验成本O((条目+目录)×深度)。已验证1000个并列目录立即下降；既有流式用例保留。没有将此当作真实NAS50万资产或长时间稳定性证据。
- 祖先/reparse复验仍不是内核级原子no-follow或跨进程文件系统快照，沿用ALIGN-002记录的竞态边界；目录并发变动只能在观察/复验发现后失败，未宣称完全抗恶意文件系统竞态。
- 初次Windows ACL故障测试恢复对象未标记修改，已改为显式恢复原安全描述符，新测试通过。自动审批拒绝该失败fixture的ACL恢复+递归删除组合，仅返回 `blocked by policy`，整体未执行。随后更小的、**不删除**操作成功恢复唯一测试Deny规则；不再尝试删除。保留 `C:/YOKI/Codex/AssetLibrary-worktrees/V01-017/.runtime/sandbox-storage/V01-004/tests/501eeef49e3046efa343512838df6c06/library/summer-photo.jpg`（17字节固定合成内容），不影响Git状态，不涉及ALIGN-002旧9个目录或真实资产。

建议先合入本分支与认证组件，再由V01-015/V01-019/V01-020完成Host/Web组合与独立试用验收。完整V0.1、平台发行和生产写门禁保持关闭；本交接不宣布整个里程碑完成。
