# V01-026 — Web 浏览与资源库分类支撑

状态：partial，后端受影响验证已通过；真实Web闭环发现窄屏首次选择导致工具栏换行、条目移位，等待V01-025修复后只复验该E2E。分支 `codex/v01-026-library-browse-interaction-contracts`；产品代码 `2eedeb5`，E2E分类调用者补充 `2fc3b1f`。尚不声明整个里程碑完成。

按 ADR-0016 / `contracts/assetlink/web-interaction-v1.md` 完成显式8分类（旧库 general）、分类列表过滤、普通单库/稳定条目详情、完整目录名称/修改时间/大小排序、类型及名称 literal 过滤、指定条目起始页和库/物理子树搜索。管理员可通过现有管理入口做分类 CAS/幂等更新；存储源提供已配置默认登记根。未修改 Web、认证机制、Worker 或物理资产写入。

继续复用 Gateway 当前身份/管理员策略、LibraryStorage 管理端口与模块 PostgreSQL 会话、GatewayAuth 有界授权读事务/结果验证、保护游标、AssetIdentity present 投影。公开详情不返回绝对根或内容 URL，离线库仍可读已提交快照。Gateway 每次校验当前管理员；LibraryStorage 校验命令、非空 actor、CAS 和幂等，不反向依赖 GatewayAuth，actor 不是权限凭证。

三份追加迁移由各自固定 owner 执行：0019 LibraryStorage 分类/审计幂等；0020 AssetIdentity 排序索引、点查及范围查询；0021 GatewayAuth 授权入口。未修改旧迁移、runner、角色政策或历史函数签名；旧入口复用新授权路径与默认选项。旧 general 注册序列化继续省略新增默认字段，旧幂等请求可重放且不会覆盖后续分类编辑。没有新增语言、框架或依赖。

名称/修改时间使用现有或新增 B-tree 范围；大小分别对非 NULL 键范围和 NULL 名称键范围限取，每个范围最多101条，最后合并最多202条。避免“after OR NULL”造成晚页从索引头扫描。类型和包含名称过滤仍可能检查范围内较多条目，保留期限与最多100条公开页，不承诺所有不选择性筛选都达到容量发布指标。首次锚点按 PK 定位并验证目录/过滤，包含锚点；后续游标严格 after。SQL 按绑定参数处理路径/名称，拼接部分仅含固定白名单排序 token。

已通过固定SDK locked restore、format verify、Release build（零警告/错误）、源码门禁（353个C#文件）、协议/指纹5项、迁移manifest 21项及真实PG升级驱动1项。PG驱动验证v18→21保存200157条事实及原scanID/count/指纹，6个首/后页范围计划无全量排序，并通过TRX确认真实.NET查询total=executed=passed=1、notExecuted=failed=0，覆盖全排序、NULL桶跨页、literal/类型过滤、锚点、详情、离线、权限及scope/category游标。未重复旧69项数据库套件。

首次规范检查揭示耦合与重复分页块；修正为分开envelope鉴权、查询body和JSON字段读取，按Library管理control分离Host输入输出，并让独立分类适配器实现精确Application存储端口。列表/浏览/搜索三个真实消费者共用有限lookahead处理。没有改analyzer或重复阈值。

真实E2E首轮证据在 `.runtime/V01-026/real-web-e2e/20260907T171445Z-dd900b29`：真实登记images、首次扫描4项、桌面详情/历史、搜索均已走真实接口成功；移动双击详情失败，后续Host恢复/资源安全阶段未执行，不能计通过。协调器确认是实际窄屏布局跳动并交V01-025修复，保留原失败动作。仓库门禁暂报三个SDK consumer指纹stale，由共享owner基于新增合同文档再生；本任务没有修改生成产物。

兼容风险：v2保护游标不复用旧v1锚点，升级后旧页面需刷新；Host仍要求精确manifest，回退旧Host须配套迁移前数据库/私密状态备份。所有资产原文件、NAS实际库ID/scanID/索引均未由本任务触碰。完整 Alpha、内容预览、重扫和文件写入继续受原门禁限制。

建议先合入已冻结文档/SPA入口，再在集中 .NET/真实PG与Web集成验收通过后合并本分支及V01-025。NAS升级和浏览器验收由主协调器负责。
