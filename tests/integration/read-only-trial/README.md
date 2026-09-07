# 真实只读试用验收

该入口创建自己的短路径临时PostgreSQL16.15集群、应用完整生产迁移、创建6个不同的NOINHERIT最小权限LOGIN，然后启动真正的Root TrialHostFactory、Kestrel HTTPS、实际文件发现子进程和Chromium。浏览器直接使用Web构建产物；没有route.fulfill或生产认证绕过开关。

先按仓库入口完成locked restore和Release build，并构建与当前源码一致的apps/web/dist。随后从仓库根运行：

```powershell
python -I -B tests/integration/read-only-trial/run_e2e.py --execute `
  --dotnet <精确SDK的dotnet.exe> `
  --postgres-bin <PostgreSQL16.15的bin目录> `
  --node <Node24.20.0的node.exe> `
  --web-root <当前Web的dist目录> `
  --playwright-module <apps/web/node_modules/@playwright/test/index.mjs> `
  --evidence .runtime/real-trial-evidence
```

Playwright版本来自Web锁文件（本轮1.62.1）；需要事先安装匹配的Chromium。入口不下载浏览器。未传--execute返回77并明确not_executed；缺少必需工具、迁移失败、测试未实际执行或清理失败均失败关闭。

CI使用已隔离的PostgreSQL16.15服务时加 `--postgres-external`，连接从既有 `ASSETLIBRARY_TEST_POSTGRES_HOST/PORT/ADMIN` 读取。测试数据库及六个唯一LOGIN均由本次创建并清理；不得将该选项指向用户生产数据库实例。

每次运行分配新的证据子目录，保存run.json、runner.log、TRX、两个阶段的浏览器JSON、1440px桌面与390px暗色截图、acceptance.json。只有实际聚合测试通过且自身数据库/临时目录清理后才标记resource_cleanup=verified。不会保存含密码的网络trace或浏览器storage state。

## 实际覆盖

- 加密operator key初始化、本地管理员bootstrap；真实运行角色检查及交换gateway/asset LOGIN后not_ready。
- 浏览器登录、登记允许根、首次扫描、物理目录浏览、完整文件名/相对路径搜索。
- 真实普通账号预配、直接管理403、隐藏库404、不可见列表为空，错误Origin/CSRF拒绝。
- Host正常重启后原Cookie/服务端会话有效、索引保留；风险源不可用时已有账号仍可登录/浏览。
- 隔离目录暂时移走时保留上次索引；扫描源失效产生failed任务，恢复后新任务成功。
- 暂停测试Host内的扫描消费者，验证queued任务取消、重启后的cancelled终态及重新扫描；不将此证据冒充正在读取文件时的取消（该场景归V01-017故障套件）。
- 风险不可用时管理员恢复零变更；授权key轮换、真实恢复/重放、旧Cookie失效、新口令登录。
- 5份合成原文件SHA-256与mtime始终不变；Windows仅按本测试证书自己的CNG/CSP容器路径检查分配及正常Dispose后的删除。

HIBP只替换测试进程内部HttpMessageHandler，仍运行真实PwnedPasswords解析器、风险门禁、密钥/verifier和PostgreSQL状态机。正式CLI没有测试环境开关。浏览器仅接受自己生成证书的SPKI，HttpClient仅pin本测试证书；不关闭全部TLS验证、不修改系统证书信任。

## 范围限制

目录改名模拟存储不可达，不是实机NAS验收。Windows异常进程终止的私钥容器生命周期由根Host故障验收负责；这里只证明正常退出。数据库fixture为测试性能关闭fsync等，不能作为断电耐久证据。全部聚合场景算1个真实E2E，不把内部检查数虚增成独立测试数。

V01-020首次实测发现默认NOINHERIT gateway LOGIN读取目录时报42501，原因是旧PostgresReadExecutor未切换runtime角色；根9076e53在共用只读事务入口修复。此入口保留直接空库读取断言，能在回归时于浏览器之前给出数据库定位。
