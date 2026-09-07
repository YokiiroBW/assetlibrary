# V01-026 — Web 浏览与资源库分类支撑

状态：ready_for_review，后端受影响验证及原真实Web闭环已通过。分支 `codex/v01-026-library-browse-interaction-contracts`；产品代码 `2eedeb5`，E2E分类调用者与请求复用修正为 `2fc3b1f` / `a3f8bcd`，最终真实E2E在整合版本 `2d2bef2` 的干净工作区执行。此交接不声明整个里程碑或完整Alpha完成。

按 ADR-0016 / `contracts/assetlink/web-interaction-v1.md` 完成显式8分类（旧库 general）、分类列表过滤、普通单库/稳定条目详情、完整目录名称/修改时间/大小排序、类型及名称 literal 过滤、指定条目起始页和库/物理子树搜索。管理员可通过现有管理入口做分类 CAS/幂等更新；存储源提供已配置默认登记根。未修改 Web、认证机制、Worker 或物理资产写入。

继续复用 Gateway 当前身份/管理员策略、LibraryStorage 管理端口与模块 PostgreSQL 会话、GatewayAuth 有界授权读事务/结果验证、保护游标、AssetIdentity present 投影。公开详情不返回绝对根或内容 URL，离线库仍可读已提交快照。Gateway 每次校验当前管理员；LibraryStorage 校验命令、非空 actor、CAS 和幂等，不反向依赖 GatewayAuth，actor 不是权限凭证。

三份追加迁移由各自固定 owner 执行：0019 LibraryStorage 分类/审计幂等；0020 AssetIdentity 排序索引、点查及范围查询；0021 GatewayAuth 授权入口。未修改旧迁移、runner、角色政策或历史函数签名；旧入口复用新授权路径与默认选项。旧 general 注册序列化继续省略新增默认字段，旧幂等请求可重放且不会覆盖后续分类编辑。没有新增语言、框架或依赖。

名称/修改时间使用现有或新增 B-tree 范围；大小分别对非 NULL 键范围和 NULL 名称键范围限取，每个范围最多101条，最后合并最多202条。避免“after OR NULL”造成晚页从索引头扫描。类型和包含名称过滤仍可能检查范围内较多条目，保留期限与最多100条公开页，不承诺所有不选择性筛选都达到容量发布指标。首次锚点按 PK 定位并验证目录/过滤，包含锚点；后续游标严格 after。SQL 按绑定参数处理路径/名称，拼接部分仅含固定白名单排序 token。

已通过固定SDK locked restore、format verify、Release build（零警告/错误）、源码门禁（353个C#文件）、协议/指纹5项、迁移manifest 21项及真实PG升级驱动1项。PG驱动验证v18→21保存200157条事实及原scanID/count/指纹，6个首/后页范围计划无全量排序，并通过TRX确认真实.NET查询total=executed=passed=1、notExecuted=failed=0，覆盖全排序、NULL桶跨页、literal/类型过滤、锚点、详情、离线、权限及scope/category游标。未重复旧69项数据库套件。

首次规范检查揭示耦合与重复分页块；修正为分开envelope鉴权、查询body和JSON字段读取，按Library管理control分离Host输入输出，并让独立分类适配器实现精确Application存储端口。列表/浏览/搜索三个真实消费者共用有限lookahead处理。没有改analyzer或重复阈值。

真实E2E最终证据在 `.runtime/V01-026/real-web-e2e/20260907T174230Z-0d158ac9`：1/1通过、零跳过、21.06秒，`acceptance.json`确认资源回收。实际HTTPS/PG/隔离Worker完成images登记、首次扫描、桌面及手机深色详情、历史/刷新、搜索、权限与Origin/CSRF拒绝、重启会话、离线快照、失败/取消重试、管理员恢复重放、旧会话撤销、原文件哈希/mtime不变及私密TLS容器回收；新增分类CAS、重放、冲突和普通用户403也执行通过。

保留两次发现与修复证据：`20260907T171445Z-dd900b29` 暴露手机首击选中造成toolbar换行/条目下移，V01-025固定布局后原双击动作通过；`20260907T172906Z-b26215c0` 已通过初次及重启浏览器阶段，随后暴露测试HTTP helper把同一JsonObject重复挂入envelope的问题，以DeepClone保留调用者请求实现真实幂等重放。没有用改动作或放宽断言绕过失败。SDK指纹由协调器原生成器同步；超长CSS由V01-025按职责拆分且构建四项hash逐项不变。本任务未修改Web或生成SDK。

兼容风险：v2保护游标不复用旧v1锚点，升级后旧页面需刷新；Host仍要求精确manifest，回退旧Host须配套迁移前数据库/私密状态备份。所有资产原文件、NAS实际库ID/scanID/索引均未由本任务触碰。完整 Alpha、内容预览、重扫和文件写入继续受原门禁限制。

最终 `verify_repository.py` 已通过，包含交接、架构、源码边界、当前SDK/依赖、迁移及14项架构回归；完整Alpha审计按要求保持blocked。独立回归28项加架构回归14项，共42项通过，零失败/跳过（嵌套和重复的同一测试不重复计数）。建议与已冻结文档/SPA入口及V01-025一并集成；本分支不再有未完成的后端实现。真实NAS升级、真实库ID/scanID/count保留读回及总体发布判断由主协调器负责。
