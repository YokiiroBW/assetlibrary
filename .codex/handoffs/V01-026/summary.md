# V01-026 — Web 浏览与资源库分类支撑

状态：partial，代码及必要回归已实现，等待协调器发起集中验证；尚不声明可合并或里程碑完成。分支 `codex/v01-026-library-browse-interaction-contracts`。

按 ADR-0016 / `contracts/assetlink/web-interaction-v1.md` 完成显式8分类（旧库 general）、分类列表过滤、普通单库/稳定条目详情、完整目录名称/修改时间/大小排序、类型及名称 literal 过滤、指定条目起始页和库/物理子树搜索。管理员可通过现有管理入口做分类 CAS/幂等更新；存储源提供已配置默认登记根。未修改 Web、认证机制、Worker 或物理资产写入。

继续复用 Gateway 当前身份/管理员策略、LibraryStorage 管理端口与模块 PostgreSQL 会话、GatewayAuth 有界授权读事务/结果验证、保护游标、AssetIdentity present 投影。公开详情不返回绝对根或内容 URL，离线库仍可读已提交快照。Gateway 每次校验当前管理员；LibraryStorage 校验命令、非空 actor、CAS 和幂等，不反向依赖 GatewayAuth，actor 不是权限凭证。

三份追加迁移由各自固定 owner 执行：0019 LibraryStorage 分类/审计幂等；0020 AssetIdentity 排序索引、点查及范围查询；0021 GatewayAuth 授权入口。未修改旧迁移、runner、角色政策或历史函数签名；旧入口复用新授权路径与默认选项。旧 general 注册序列化继续省略新增默认字段，旧幂等请求可重放且不会覆盖后续分类编辑。没有新增语言、框架或依赖。

名称/修改时间使用现有或新增 B-tree 范围；大小分别对非 NULL 键范围和 NULL 名称键范围限取，每个范围最多101条，最后合并最多202条。避免“after OR NULL”造成晚页从索引头扫描。类型和包含名称过滤仍可能检查范围内较多条目，保留期限与最多100条公开页，不承诺所有不选择性筛选都达到容量发布指标。首次锚点按 PK 定位并验证目录/过滤，包含锚点；后续游标严格 after。SQL 按绑定参数处理路径/名称，拼接部分仅含固定白名单排序 token。

必要验证已编写：旧 general 幂等指纹、协议字段/拒绝矛盾 scope、现有 HTTPS Host 管理成功/重放/CAS/非管理员拒绝，以及复用 PostgreSQL 驱动的 v18→21 升级（200157 条合成事实、原 scanID/count/事实指纹不变）、索引计划和真实 .NET 全排序/NULL桶跨页/筛选/锚点/详情/权限/scope游标。当前均待运行，不能把计划计为通过。

兼容风险：v2保护游标不复用旧v1锚点，升级后旧页面需刷新；Host仍要求精确manifest，回退旧Host须配套迁移前数据库/私密状态备份。所有资产原文件、NAS实际库ID/scanID/索引均未由本任务触碰。完整 Alpha、内容预览、重扫和文件写入继续受原门禁限制。

建议先合入已冻结文档/SPA入口，再在集中 .NET/真实PG与Web集成验收通过后合并本分支及V01-025。NAS升级和浏览器验收由主协调器负责。
