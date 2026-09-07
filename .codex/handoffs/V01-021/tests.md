# V01-021 统一验收

2026-09-07。遵循用户“集中实现、最后统一验收”的节奏：未按零碎编辑跑测试；环境和完整diff稳定后执行，实际失败集中修正，仅复验对应失败路径。未变成功检查不重复。

## 已执行的构建与受影响回归

| 命令/范围 | 实际结果 |
| --- | --- |
| 固定.NET10.0.111 `dotnet format AssetLibrary.slnx --verify-no-changes --no-restore` 与 Release build | 格式通过，Release 0 warning/0 error |
| `dotnet test tests/dotnet/AssetLibrary.Packaging.Tests/AssetLibrary.Packaging.Tests.csproj -c Release --no-build --no-restore` | 63/63，含新增健康探针本机路由及TLS/身份拒绝 |
| WebGateway既有Pwned过滤回归 | 25/25，覆盖4秒预算上限及现有协议/失败关闭 |
| Linux ReadCore既有PosixBackslashNamesAreRejectedWithoutChangingPhysicalIdentity过滤回归 | 2/2，0skip，实际Linux固定SDK；原TRX在V01-022 |
| 现有pnpm格式、lint、typecheck、build、test:browser | 40/40浏览器，一次完整执行，0失败/重试/skip；详见V01-023/tests.md |
| Docker Core/setup与PG离线交付 | 实际daemon构建；SHA256SUMS在构建/传输/实际部署目录读回通过。最终缓存更新仅变运行层/配方，应用输入逐文件比较不变，没有重复编译测试 |

原日志位于本worktree `.runtime/V01-021/{format-final,build-final,packaging-tests,risk-budget-tests,repository-final}.log` 和子任务交接。首次仓库/架构/契约/源码检查通过；本轮最后的门禁与交接元数据变更单独按下列现有窄入口验证，不重复.NET/Web成功检查。

## 实际NAS验收

NAS Linux5.10.55+ x86_64、Docker24.0.2、Compose2.20.1；实际Core image085c7fe5、setup aed6cc7d、PG5f71c21b。镜像完整身份、源commit/tree与不可变base见build-evidence，不能把dev-230当NAS。

1. 复用未改动 `tests/integration/read-only-trial/browser.mjs`，通过真实HTTPS/PG登录→登记自有样例→首次扫描→album目录→文件名/相对路径搜索。Chromium151.0.7922.34，桌面1440×900与手机390×844暗色已看图；local/session storage为空，无横向溢出。
2. 实际API匿名401、Origin/CSRF403、已完成快照重扫409；POSIX根字面反斜杠拒绝，非法文件名扫描明确entry_path_unsupported、零提交，未映射成别名目录。
3. 实际NAS stop/start后同一认证Cookie仍有效、索引保留。带外recover使用标准CA与真实风险检查成功，旧会话401，新登录200。
4. 实际容器非root、read-only root、cap_drop ALL、no-new-privileges；资产两bind均RO、PG无宿主端口。对自有样例和容器系统目录写入返回EROFS且无新文件，私密state写入及自有探针回收成功。9个自有文件hash/size/mtime均不变。
5. 自有acceptance project down后删除4个带匹配deployment标签的卷，按标签和确切名称读回容器/网络/卷均不存在。无全局prune或其他服务操作；9个样例在核验不变后按marker和绝对边界回收。
6. 正式assetlibrary-nas独立初始化18条迁移、6个模块登录和管理员成功，两个长期容器healthy。图片/文档各一库已登记且scan=null；实际正式账号桌面/手机登录切库通过，记录scan-start请求0。临时relay停止后登录与列库仍通过。

上述最终机器记录在nas-deployment-evidence.json；截图/任务编排在ignored `.runtime/V01-021/nas-browser`，没有新建生产测试框架。初始发现的网络、标准CA、非标准运行时与Synology共享读取组问题均已修复后复验；没有禁用TLS/风险检查或扩大资产写权限。

## 最后门禁对齐的现有入口

`python -I -B scripts/verify_repository.py`（包含架构、契约、Alpha、迁移清单、生成与源码策略）；`python -I -B -m unittest discover -s tests/release -p test_v0_1_alpha_readiness.py -v`；两个受影响repository文件test_v0_1_alpha_foundation.py与test_server_packaging_foundation.py；发行定义既有test_repository_release_definitions_pass。最后执行结果补充在本记录末尾。

只关闭M0-004-G2。Docker target应返回0；完整Alpha --require-ready应返回3，Windows/Linux/容量/生产写入继续阻断。未运行完整历史V01-008打包、所有.NET/数据库套件、大容量或破坏性个人资产测试。

## 最后对齐结果

- 最后完整diff审查与 `git diff --check` 通过，每个变更追溯本里程碑；未新增共享SQL或wire契约，测试调整保留拒绝与失败关闭断言。
- repository-delivery.log：仓库/交接/架构/契约/源码检查通过，内含21条迁移清单与14条架构规则测试。
- alpha-delivery.log：14/14；alpha-foundation-delivery.log：5/5；packaging-foundation-delivery.log：6/6；release-definition-delivery.log：1/1。现有Alpha CLI回归确认审计0、完整发布3且Docker不再在阻断列表，其他状态保持失败关闭。
- 合计191项独立自动检查通过（130受影响native/Web + 35仓库内规则 + 26门禁回归），0失败/skip；CLI、构建、真实NAS流程另列，不混入计数。
- 部署配置最终移至 `/volume2/homes/agent/assetlibrary/V01-021/live`；26文件hash核验后移除旧自有目录，Compose管理标签随两容器重建更新，原Cookie与两库保留。9个自有样例已回收，防止非法名称夹具影响用户Documents首次扫描。证据见nas-deployment-evidence.json的deployment_relocation/owned_fixture_cleanup。
- 此后仅追加验收元数据并执行交接校验，不重跑已成功且输入不变的产品测试。
