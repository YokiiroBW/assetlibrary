# V01-016 测试记录

## 环境与源码

2026-09-07，Windows x64，worktree C:\YOKI\Codex\AssetLibrary-worktrees\V01-016。源码09e090e，最终测试基架f5e0eba，基线77540cb。SDK精确10.0.111，目录C:\Users\Administrator\AppData\Local\Temp\V01-014-tooling-and-tests\tooling\dotnet；NuGet缓存复用主仓.runtime/nuget。Python使用Codex bundled runtime。依赖和lock未更新。

## 最终命令与结果

| 命令 | 结果 |
|---|---|
| dotnet restore AssetLibrary.slnx --locked-mode --disable-parallel | 通过 |
| dotnet format AssetLibrary.slnx --verify-no-changes --no-restore | 通过 |
| dotnet build AssetLibrary.slnx --configuration Release --no-restore --disable-build-servers | 0 warning / 0 error |
| dotnet test tests/dotnet/AssetLibrary.WebGateway.Tests/AssetLibrary.WebGateway.Tests.csproj --configuration Release --no-build --no-restore --logger "console;verbosity=minimal" --logger "trx;LogFileName=webgateway-final.trx" --results-directory .runtime/V01-016-tests | runner81 passed，0 failed/skip；实际执行79，2个PG条件方法未配置连接而return |
| dotnet test tests/dotnet/AssetLibrary.Packaging.Tests/AssetLibrary.Packaging.Tests.csproj --configuration Release --no-build --no-restore --logger "console;verbosity=minimal" --logger "trx;LogFileName=packaging-final.trx" --results-directory .runtime/V01-016-tests | 46 passed，0 failed/skip |
| python -I -B scripts/verify_repository.py | 通过；含manifest21、architecture14；Alpha有效性审计仍blocked |
| python -B -m unittest discover -s tests/spikes/assetlink -p test_*.py -v | 21 passed |
| python -B scripts/generate_assetlink_sdks.py --check | 7 generated files current |
| python -B scripts/validate_assetlink_sdk_source.py | 通过 |
| python -B tests/architecture/check_release_gates.py --target v0.1-start | allowed，exit0 |

新增24项：Core8、Host16。去重排除2个未执行PG body和重复运行：125个.NET实际执行 +21 manifest +14 architecture +21 AssetLink =181 passed，0 failed，2 not-executed。result.json把未执行方法列为skipped；原runner显示skipped=0，特此说明。

未执行方法：PostgresStoreCompletesLocalSignInCsrfAndRevocationRoundTrip、PostgresStoreCompletesAdministratorBootstrapAndRecoveryRoundTrip；未设置ASSETLIBRARY_TEST_AUTH_CONNECTION/ASSETLIBRARY_TEST_AUTH_SECRET。其runner pass不作为数据库行为证据，真实PG14与全Host闭环由V01-017/V01-015统一执行。

## 负向与恢复覆盖

- 真实DP加密key/proof跨provider重启仍有效；轮换拒绝旧proof；文件不含原始32-byte key。
- action、operation、account、expiry、deployment、MAC篡改拒绝；损坏/丢失/错误DP provider闭锁；并发初始化仅一个成功；取消传播；其他用户可读目录拒绝且原key不变。
- 自动恢复使用prepare版本；授权拒绝先于风险/prepare；风险Unavailable零prepare/store；准备冲突零凭据写。
- 真实Kestrel HTTPS login/session/logout检查Cookie旗标与6个Session字段；错误/未知口令通用401；Origin缺失/null/多值/错误，Fetch Metadata跨站、Host错配、HTTP、CSRF缺失/错配均拒绝。
- 数据库失联503不清Cookie，恢复后会话可用；注销后旧Cookie401移除；每次取当前身份/管理员事实，撤权后直接管理调用403，普通读可继续；控制错误经生成AssetLink codec验证。
- 窗口20次后429；并发2无队列；第三个真实并发登录在credential lookup前429；Retry-After存在。
- 非法JSON、重复/遗漏字段、超限输入、取消stdin拒绝；operator保留稳定authorization/op/expiry，字符串化脱敏。

## 失败诊断与修正

初始编译暴露管理员service和测试fixture耦合门禁；把恢复准备、现有诊断、测试传输和构造职责分开后通过，无抑制或放宽。

最后新增权限拒绝测试被源码门禁识别为60-token重复准备片段。f5e0eba改为复用既有sandbox初始化方法，无生产/测试语义变化；重新通过247份C#源码策略、format verify、全solution build及GatewayAuthorizationPermissionTests定向1/1。未重复没有输入变化的其他成功用例，去重计数不变。

最初Host认证12项中9项在TLS握手前失败，3项非握手测试通过。TLS诊断确认Windows Schannel不支持ephemeral私钥。采用X509CertificateLoader.LoadPkcs12(...,UserKeySet)，无PersistKeySet/Exportable，app Dispose后确定释放证书，12/12通过；随后扩展为16项Host新测试并通过完整suite。客户端仅信任本测试证书thumbprint，未关闭全部TLS校验或修改系统信任。生命周期依据：[Microsoft说明](https://support.microsoft.com/en-us/topic/private-key-lifetime-on-windows-and-the-january-2021-net-framework-security-and-quality-rollup-3f097a10-f798-4ffd-8511-4757ebaf2c70)。

12项TLS批次前后只读当前用户Crypto文件清单：3→3，新增0、减少0，没有删除既有私钥。异常进程终止容器生命周期由根Host故障E2E验证，正常退出证据不推广为崩溃保证。

## 协调PG证据与残留

V01-017回传真实PG16.15：BEGIN READ ONLY下prepare可调用；disabled管理员ready|1；首次recover applied|f|2；同auth/op/expiry再prepare ready|1、recover applied|t|2；过期、未知、auth/op冲突和LibraryStorage runtime调用拒权通过。SQL14初始提交4a4df6a，完整测试记录/最终commit见V01-017交接。本任务不改SQL/manifest，也不重复计数协作PG证据。

%TEMP%/AssetLibrary-V01-016下任务叶目录全清理，父目录子项0。.runtime/V01-016-tests保留TRX，bin/obj、dotnet-home等ignored缓存保留。未读写原资产/NAS，未改注册表、服务、防火墙、系统证书信任。根集成继续负责真实Postgres适配器、完整Web以及Host异常终止验证。
