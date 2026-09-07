# V01-019 验证记录 — 2026-09-07

## 最终结果与命令

8个本任务唯一自动测试通过、0失败、0跳过；另执行已有Windows父硬死回归1项通过。包smoke和Crypto观察单列，不按内部断言数量虚增测试数。测试均使用系统临时目录或本任务`.runtime/sandbox-storage/V01-019`，未接触真实资产。

- `python -I -B -m unittest discover -s tests/release -p test_read_only_trial_package.py -v`的8个方法按稳定修改范围执行：包来源/输出拒绝/manifest/PS解析/真实预检5项；真实PG1项；完整原生包流程1项；启动记录发布失败回滚1项。
- 原生环境：`ASSETLIBRARY_TEST_TRIAL_POSTGRES_BIN`=PostgreSQL16.15 bin，`ASSETLIBRARY_TEST_TRIAL_REQUIRED=1`；完整包/回滚再设置`ASSETLIBRARY_TEST_TRIAL_PACKAGE`和`ASSETLIBRARY_TEST_TRIAL_PACKAGE_REQUIRED=1`。工具为仓库精确.NET10.0.111、Node24.20.0、pnpm11.19.0、Python3.12+、PowerShell7.6.5（入口要求7.5+）。
- 完整原生流程对build-04/source339275b通过，123.429s：真实bootstrap/replay、HTTPS页面/管理员登录、双启动拒绝、伪造停止generation/nonce拒绝、伪造PID开始时间拒绝、正常stop和控制文件清理、重启登录、recover新口令后登录、包篡改拒绝、源hash/mtime不变、日志无口令及stop_nonce。真实风险源失败时最多3次同ID/expiry/password重试，仍失败则test fail，无fake risk/TLS bypass。
- 新启动回滚对代码版本包source035f2da通过，21.482s：在Host启动后实际阻止host-process.json发布，确认只对已验证进程请求停止、没有强杀、无残留运行Host/PG或pending文件。启动被取消时可保守返回exited/not_confirmed；正常运行后的请求返回graceful。未重跑未改变的外网风险整轮。
- `.NET restore/build`使用locked mode；新Host文件targeted format通过，最终Host及Core构建均0warning/0error。早期CA1506已按记录/文件/监视职责拆成同文件3个具体类，未抑制分析器。
- `ASSETLIBRARY_TEST_DOTNET=<pinned dotnet> python -I -B tests/dotnet/AssetLibrary.ReadCore.Tests/test_worker_lifetime.py -v`：1通过，2.785s。先按真实入口构建当前Release Core。该test只TerminateProcess自己父进程，不依赖finally，确认Windows Job关闭终止已接收请求且阻塞的子进程。
- `scripts/validate_architecture_baseline.py`、`scripts/validate_handoff.py`、`scripts/generate_assetlink_sdks.py --check`执行通过；最终handoff完成后再检查文档/边界有效性。原Alpha/平台发布门禁保持blocked。

## 最终产物实测

`python -I -B scripts/build_read_only_trial.py --dotnet <pinned dotnet> --node <Node24.20.0> --pnpm <pnpm11.19.0 pnpm.cjs> --nuget-packages <existing cache> --output-root .runtime/sandbox-storage/V01-019/delivery-rsa-doc`从干净commit864da3b2bfeca614b2654eb380ef6074d826b6c4构建。源码tree4cbfffc99e714466f20b6e3b44e2c836f188cb92。

最终smoke通过：372文件的长度/SHA-256、ZIP精确文件集合及流式SHA-256、18迁移私密初始化、跨进程解密/轮换operator key、start/ready、重复start拒绝、graceful stop/restart、合成源字节/mtime不变、自己的临时状态清理。记录在`delivery-rsa-doc/native-final-smoke.json`；ZIP与manifest校验值见summary.md。

## Crypto正常/强制退出观察

仅枚举当前用户Crypto目录的文件名元数据，记录计数和本启动新增项的名称哈希；未读取任何密钥内容、未删除任何profile容器。新观察期间Root和V01-020暂停TLS导入测试。

| 窗口 | 启动前 | 运行中 | 停止后 | 本窗口新增仍在 |
|---|---:|---:|---:|---:|
| 旧Kill stop | 13 | 14 | 14 | 1 |
| 新graceful stop | 15 | 16 | 15 | 0 |

文件：`.runtime/sandbox-storage/V01-019/crypto-old-stop-observation.json`与`crypto-graceful-observation.json`。数字只说明这些具体窗口，不能据此声明全部历史容器已删除、初始化无任何外部副作用或强制崩溃能完成正常清理。

## 已发现并修复的问题

1. PowerShell测试传参改为临时脚本；Windows pg_ctl继承捕获管道使等待不结束，改为独立日志/无捕获句柄。
2. 部署LOGIN标识改为既有migration_tool要求的assetlibrary_命名空间；模块仍各自NOINHERIT并显式SET角色。
3. Host operator原stdout混入结构化日志，Root改为stderr日志/单stdout结果；包装严格解析完整JSON并固定UTF-8，不通过取最后一行掩盖协议漂移。
4. 固定非秘密HIBP前缀00000的成功观察（TLS1.2、未Add-Padding）为200/98,561 bytes/2509行/无末尾CRLF；Add-Padding:true尝试当时是TLS失败，未混记。Root核对并更新5000行/256KiB及完整EOF末行兼容，2秒预算保留。未保存range正文到夹具/日志。
5. 真实包已bootstrap/replay后，旧PowerShell启动方式仍继承父捕获句柄；新Python launcher使用SW_HIDE/CREATE_NO_WINDOW/close_fds=True，捕获调用能正常返回。
6. 正常停止改为nonce/代次绑定的本机请求，旧/伪造请求不生效；启动回滚复用45秒等待与身份复核。未验证PID不被杀。记录失败的pending文件在自己的作用域清理。
7. 独立Get-Process对象的退出码不作为已接受停止请求的额外成功门槛；只有请求已接受且原进程已退出才报告graceful，强制终止仍明确forced。

没有关闭任何安全检查、修改信任库/SCM/防火墙、重置已有数据库、移动真实资产、清理历史worktree，或绕过先前审批拒绝的目录清理。

最终文档补齐新TLS及历史DP解密证书必须含RSA≥2048私钥，明确ECDSA-only不支持。合入Root的c5d2a6c CI字面修复后，从864da3b重建delivery-rsa-doc；372文件/精确ZIP校验、初始化/轮换/启动/正常停止/重启smoke再次通过。产品代码及联网流程未变化，未重复风险源整轮。最终handoff/架构有效性在这个元数据版本通过。
