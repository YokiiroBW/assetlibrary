# V01-020 测试记录

## 可重复命令

从V01-020仓库根先完成locked restore和Release build，随后运行：

```powershell
python -I -B tests/integration/read-only-trial/run_e2e.py --execute --dotnet "C:/Users/Administrator/AppData/Local/Temp/V01-014-tooling-and-tests/tooling/dotnet/dotnet.exe" --postgres-bin "C:/Users/Administrator/AppData/Local/Temp/V01-014-tooling-and-tests/tooling/postgresql/pgsql/bin" --node "C:/YOKI/Codex/worktrees/V01-006/.runtime/toolchains/node-v24.20.0-win-x64/node.exe" --web-root "C:/YOKI/Codex/AssetLibrary-worktrees/V01-018/apps/web/dist" --playwright-module "C:/YOKI/Codex/AssetLibrary-worktrees/V01-018/apps/web/node_modules/@playwright/test/index.mjs" --evidence .runtime/real-trial-evidence
```

本机python为Codex bundled Python；可执行路径见任务包。实际SDK10.0.111、PG16.15、Node24.20.0、Playwright1.62.1、Chromium151.0.7922.34。Web源码与f1dfaba7相同；构建产物来源已记录summary及run.json。

## 最终结果

| 验证 | 结果 |
|---|---|
| dotnet restore AssetLibrary.slnx --locked-mode | 通过 |
| dotnet build AssetLibrary.slnx --configuration Release --no-restore --disable-build-servers | 0 warning / 0 error |
| dotnet test Packaging.Tests --configuration Release --no-build --no-restore --filter FullyQualifiedName~TrialAuthentication | 16/16 |
| dotnet test WebGateway.Tests --configuration Release --no-build --no-restore | 84 passed、1 skip |
| required run_e2e.py --execute | 1/1真实聚合E2E，19秒，0 skip |
| python -I -B scripts/verify_repository.py | 通过；manifest21、architecture14；Alpha仍blocked |
| 所有本任务C# include格式、browser.mjs Prettier check | 通过 |
| 全solution format --verify-no-changes | 根2个产品文件4处换行待根处理，本任务未改 |

WebGateway默认skip就是未启用required driver的E2E；required driver随后实际执行。84个runner pass内，2个旧Postgres条件方法没有连接env而提前return，明确不计实际数据库通过。实际执行去重99个.NET（82+16+1），加35个架构/manifest=134；不把聚合E2E的17个检查族虚增为17独立测试。

## 最终证据目录

.runtime/real-trial-evidence/accepted/20260907T100710Z-7fb75fbd

- run.json：测试实现ce4cafa，dirty仅未跟踪handoff模板。
- runner.log、trial-e2e.trx：required E2E实际1 passed。
- acceptance.json：17个场景族，resource_cleanup=verified。
- initial/resumed-browser.json：同一library_id，浏览器151.0.7922.34。
- initial/resumed-desktop.png及mobile-dark.png：1440px与390px暗色真实页面，已查看。

## 负向和恢复

真实6个module LOGIN各自唯一NOINHERIT/SET权限；交换gateway/asset连接后RuntimeReadiness不ready。真实管理员bootstrap、普通账号生命周期和session均经原应用/PG函数，不seed伪管理员来绕过认证。

普通账号直接管理403、隐藏库404且正文无库名、库列表为空；错误Origin/CSRF403。Host重启后原Cookie/DB session200。模拟风险源Unavailable仍可登录/浏览；新口令恢复则闭锁且旧会话保持，风险恢复后key轮换/恢复/重放正确、旧会话401并清Cookie、新口令登录成功。

源目录暂时移走，真实子进程探测offline而原索引可读；queued任务恢复执行遇失效源成为failed，目录恢复后新任务成功。测试暂停本Host扫描消费者以确定地验证queued取消、重启cancelled终态与新任务成功；不伪称此处验证leased文件读取中的取消。5份合成原文件强哈希、mtime及文件数不变。

## 发现和修复

首轮在浏览器登录后libraries.list503；直接端口给出42501/schema gateway_auth，定位PostgresReadExecutor遗漏SET LOCAL ROLE。根9076e53修复后复现断言及浏览器流程通过，未放宽NOINHERIT或函数权限。原失败证据在.runtime/real-trial-evidence/read-boundary。

Windows输出编码在harness显式统一UTF-8，避免诊断乱码；测试fixture遵守原耦合门禁，按配置/生命周期/HTTP/权限/存储职责拆分，未抑制分析器。

## 清理与限制

每次新的证据子目录避免旧成功文件污染。driver检查TRX实际通过，成功退出后确认自有数据库/集群/临时目录清理；超时只终止自己的dotnet测试进程树。最终al20-*残留0。TLS证书用UserKeySet且无PersistKeySet/Exportable；只观察自己证书的容器文件，正常Dispose后不存在，没有删除其他Crypto文件或修改系统信任。

未验证真实NAS、断电耐久、完整平台发布、资产写入、Windows异常终止容器回收。PG测试fixture禁用fsync等只适合功能测试。正在读取时取消和进程故障由V01-017承担；Windows异常终止私钥容器由根验收。不将这些缺少的证据记为通过。
