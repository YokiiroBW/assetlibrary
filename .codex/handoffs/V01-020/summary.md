# V01-020 真实只读试用闭环交接

## 状态

ready_for_review，2026-09-07。分支codex/v01-020-read-only-trial-end-to-end；集成基线07e4814（含根9076e53修复），测试实现ce4cafa。没有自行修改产品业务代码；Host唯一变更是获批的WebGateway.Tests friend，另按批准范围移动共享TLS测试工具和增加测试ProjectReference。

## 完成内容

可重跑入口tests/integration/read-only-trial/run_e2e.py复用现有PostgreSQL集群、角色和迁移fixture，创建自己的短路径临时集群/数据库，应用18个生产迁移，并创建6个不同的NOINHERIT、唯一SET membership LOGIN。Root TrialHostFactory、RuntimeReadiness、Kestrel TLS、真实dotnet Host文件发现子进程、生产Web产物及Chromium完整参与。

1个真实聚合E2E通过以下场景：本机加密key/bootstrap；错误module LOGIN不ready；浏览器登录、注册、首次扫描、目录和完整路径搜索；普通用户管理403、隐藏库404/空列表；Origin/CSRF拒绝；原Cookie跨Host正常重启有效；离线风险源不妨碍既有登录/浏览；存储移走时保留索引；扫描失败后恢复并新任务成功；queued取消、重启终态、重新扫描；风险不可用恢复零变更、key轮换、真实恢复与重放、旧会话失效、新密码登录。5份合成原文件SHA-256及mtime不变。

浏览器没有route.fulfill。HIBP仅替换测试进程内部受控HttpMessageHandler，真实解析器、风险门禁、key/verifier、PG状态机不变；正式CLI没有绕过开关。浏览器只允许本测试证书SPKI，HttpClient只pin本证书。没有修改系统信任、注册表、服务、防火墙或用户NAS。

## 关键发现

首次真实登录成功后目录读取503；直接端口精确复现PG42501 permission denied for schema gateway_auth。根因是旧PostgresReadExecutor未执行SET LOCAL ROLE；根9076e53修复公共只读事务，保留LOGIN的NOINHERIT政策。修复后完整流程通过；直接空库断言作为回归保留。

## 复用与边界

复用tests/database/test_migration_integration.py的setup/apply/cleanup，不继承运行整个旧suite。复用既有受控PwnedPasswords测试响应以及GatewayAuth真实组合。TrialAuthenticationTestTls移至Shared/TrialTestTls，由两个测试项目Link使用，未复制或引用整个测试项目。

WebGateway.Tests增加Host ProjectReference及相应既有WindowsServices传递锁记录；Host仅增加friend。新测试通过原模块公开接口/已批准测试composition，不引入生产测试开关、数据库迁移、角色变更、依赖框架或新语言。部署来源和业务语义不在harness重写。

## 证据

最终证据：.runtime/real-trial-evidence/accepted/20260907T100710Z-7fb75fbd，source_revision=ce4cafa。run.json记录dirty=true是因为当时只有未跟踪handoff模板；测试源码均已提交。acceptance.json为passed、aggregate_tests_passed=1、skipped=0、resource_cleanup=verified。

Chromium151.0.7922.34，Playwright1.62.1/build1234，Node24.20.0，.NET10.0.111，PostgreSQL16.15。Web/src、package与lock核对等同V01-018 f1dfaba7；复用其dist，index.html SHA256为8391aa8ccf4bdd7146fe92997da65f22d4b6db9509ff330438ca461d239fc170。1440px桌面和390px暗色截图已实际查看，无横向溢出，真实路径搜索结果可见。

## 测试与限制

required真实E2E1/1；共享TLS16/16；WebGateway默认84通过、1个E2E显式skip，其中2个既有PG条件body未配置连接而return，不能作为DB证据。排除这2项并以required E2E替代其默认skip，实际.NET去重99项；manifest21、architecture14，总134通过、0功能失败、2个旧条件body未执行。

本任务include格式与Prettier通过；全solution Release构建零警告；verify_repository通过（332 C#、169架构inputs）。全solution format在根拥有的TrialManagementGateway:71、TrialReadinessEndpoint:9仍有4处换行，已交根处理，未越权修改。

正常退出按本证书自己的CNG/CSP路径验证分配和删除；owned PG及临时目录清理通过，al20-*残留0。目录改名模拟存储不可达，不是实机NAS证据；取消覆盖queued，正在读取时取消/进程崩溃由V01-017故障套件负责。PG fixture关闭fsync等，不代表断电耐久。Windows异常进程终止私钥容器由根故障验收负责。完整Alpha、平台发行与资产写入门禁不改变。

建议合并ce4cafa及后续交接，再由根完成全局格式/最终包验收和主线同步。
