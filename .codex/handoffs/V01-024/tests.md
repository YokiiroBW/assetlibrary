# V01-024 统一验收记录

先集中实现，再统一审查、构建与验收；失败按相关原因批量修正，未重复无变化的成功用例。没有新增测试框架、截图基线、全库个人资产测试或新生产开关。

## 已验证

- V01-025：固定Node24.20/pnpm11.19，frozen/offline install、format、lint/type/build通过。原40例+3个关键交互例共43个不同浏览器例通过；首轮37通过，集中修6失败后受影响7通过，未重跑其他36。
- 实际E2E另发现390px选择toolbar换行使第二击落空，固定两行，原例验证row y不变及原double-click打开drawer，1例复验通过。保留真实E2E动作；没有用改动作掩盖产品缺陷。
- CSS超出原per-file源码门禁，按原顺序拆6块（最大8546B），阈值未改；source/format/build通过。拆分前后CSS/JS/HTML/map逐项hash一致，不重复浏览器。
- V01-026：SDK10.0.111 locked restore、最终format/Release/source通过，零warning/error；协议/旧general幂等5、manifest21、真实PG升级驱动1、实际HTTPS/PG/Chromium E2E1、架构14，共42不同检查通过。source模块边界、Alpha有效性（仍blocked）、SDK/依赖、Web源门禁通过。
- PG在自有沙箱验证18→21保留200157条事实、原scan快照/整表fingerprint；6个首/后页范围索引计划没有全量sort。nested .NET TRX total=executed=passed=1、notExecuted=failed=0；覆盖6种排序/NULL跨页、literal筛选、远处anchor、scope/category游标、离线与权限，旧SQL签名也验证。没有执行旧PG69全量。
- 原真实E2E最终21.06s、1/1、零skip，acceptance.resource_cleanup=verified。覆盖17个原场景及显式images登记→photos CAS/重放/冲突、普通用户403。中间两次失败（上述移动布局、测试JsonObject父节点复用）各批修正后仅重跑该E2E。

命令与原日志详见 [V025测试](../V01-025/tests.md)、[V026测试](../V01-026/tests.md)。最终E2E目录为V026 `.runtime/V01-026/real-web-e2e/20260907T174230Z-0d158ac9`，initial/resumed两阶段桌面与手机图已查看。SDK指纹更新由原generator执行，3种语言源仅头部comment改变，无执行代码变化。

## 包与真实NAS

本轮新源码747由真实Docker daemon编译服务端和Web；最终61e6再次构建Linux Web，其CSS/JS/HTML/map与Linux747逐项相同（Windows map因源码表示不同单独记录，不当作运行逻辑差异）。精确源码/tree/base/imageIDs及验证SHA在build-evidence.json；新包755318272 bytes，两端完整校验。

旧live正常停止后冷备4卷与配置；tar保留numeric owner/mode/ACL/xattr/links并compare/hash，通过后才更新。新setup读回already_prepared、migrations21/module_logins6、authorization_key_present。Core/PG healthy，原管理员可登录，两个库ID/名称/权限和原scan记录逐项不变，图片195792、文档scan=null，旧分类general。只读挂载和硬化属性再次读回。

最终NAS浏览器是正式账号的只读上线检查：1440桌面首页/管理，库深链→刷新→扫描页→后退，390暗色库管理，无横向溢出；13个control请求均200，管理/scan-start请求0，退出成功，local/sessionStorage空。未记录个人资产列表截图；只保留库首页与管理页。截图/动作JSON在本worktree `.runtime/V01-024/nas-web/`。证据汇总于integration-evidence.json。

## 最后审查与清洁

产品静态diff、调用方、部署来源和权限/数据边界已审查；85不同自动检查全部通过，构建/部署/CLI另列不混入计数。最后只更新交接/项目状态并验证这些元数据，不重跑已经成功且输入未变的业务测试。Git clean不代表删除ignored回退包、私密备份和日志；历史拒绝清理项不再触碰。
