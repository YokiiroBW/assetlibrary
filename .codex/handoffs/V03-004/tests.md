# V03-004 测试记录

最终交接为 ready_for_review：46 项独立检查通过，失败0、跳过0；短生命周期截止、原真实E2E、两端联调后的显式停止均完成清理。历史失败保留在下文；同一成功检查不重复计数。

## 环境与真实命令

Windows x64；Python 使用 C:/Users/Administrator/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe；精确 .NET SDK10.0.111 和 PG16.15 来自 C:/Users/Administrator/AppData/Local/Temp/V01-014-tooling-and-tests/tooling。依赖复制主仓库既有 NuGet cache 到自有 .runtime/nuget，再运行锁定 restore；没有改包版本、根锁或禁用漏洞审计。

已执行：

- dotnet restore tests/dotnet/AssetLibrary.WebGateway.Tests/AssetLibrary.WebGateway.Tests.csproj --locked-mode
- dotnet build tests/dotnet/AssetLibrary.WebGateway.Tests/AssetLibrary.WebGateway.Tests.csproj --configuration Release --no-restore：零警告/错误。
- dotnet format tests/dotnet/AssetLibrary.WebGateway.Tests/AssetLibrary.WebGateway.Tests.csproj --verify-no-changes --no-restore：通过。
- python -I -B -m unittest discover -s tests/integration/native-clients -p test_serve.py -v：5/5；有界生命期、不执行语义、会合路径逃逸、localhost身份、进程存活检查。
- dotnet test tests/dotnet/AssetLibrary.WebGateway.Tests/AssetLibrary.WebGateway.Tests.csproj --configuration Release --no-build --no-restore --filter FullyQualifiedName~NativeClientTlsFixtureTests：3/3、零跳过；旧证书一小时默认、native125分钟/localhost、过期或无界有效期拒绝。
- python -I -B scripts/verify_repository.py：通过；21 项 manifest +14 项架构回归，SDK/依赖/源码/交接门禁通过，Alpha 有效性审计仍 blocked。

原生支架 CI 可复现命令（自行提供同版本工具路径，Linux使用能执行initdb的非root用户）：

```text
python -I -B tests/integration/native-clients/serve.py --execute --dotnet <dotnet> --postgres-bin <PostgreSQL16.15/bin> --web-root <built-Web/dist> --lifetime-seconds 2 --evidence .runtime/native-clients-evidence
```

实际 Windows 路径参数：dotnet 为 tooling/dotnet/dotnet.exe，PG为 tooling/postgresql/pgsql/bin；Web静态目录复用 C:/YOKI/Codex/AssetLibrary-worktrees/V01-025/apps/web/dist。该静态依赖不是本任务新构建或原生 UI 验收证据。

## 真实运行结果

20260908T085311Z-adf0d3a3：短生命期正常截止；真正生产迁移/模块最小权限连接、operator、认证、Worker完成 140 条索引。TRX total=passed=1、failed=notExecuted=0。host-cleanup 与 acceptance 均确认138文件 SHA256/mtime不变、Host Dispose、CNG容器删除、监听关闭、Host/PG进程退出、数据库及六角色删除、临时目录删除。生命期截止1项通过，不把内部断言虚增为多个E2E。

20260908T085618Z-963768ae：原 tests/integration/read-only-trial/run_e2e.py --execute 回归，使用上述dotnet/PG与原已验证Web/Playwright，Node为 C:/YOKI/Codex/worktrees/V01-006/.runtime/toolchains/node-v24.20.0-win-x64/node.exe。真正Chromium登录/登记/扫描/浏览/搜索、Host重启、普通账号/隐藏库/Origin/CSRF拒绝、故障恢复与源不变全程完成。TRX 1/1、22.689秒、零跳过；acceptance resource_cleanup=verified。用于验证改变的共享fixture默认参数及Restart/Dispose路径。

20260908T085431Z-e77b23d2：配置两小时上限的交互实例，READY后提供给V03-002/V03-003实际HTTP/设备联调。两端owner确认完成后创建私密stop文件，2026-09-08 17:20:44 +08:00停止。TRX outcome=Passed、1/1、26分7.7256405秒；host-cleanup.status=disposed，acceptance.stop_reason=explicit_stop、resource_cleanup=verified。138合成文件hash/mtime不变，数据库及六角色删除、Host/PG进程退出、61180监听关闭、私密临时目录删除。另行回读文件系统确认runtime不存在、Host PID23872不存在。Android owner已确认删除本次reverse及设备内私密连接JSON。此显式停止生命周期计1项，消费者自己的协议/UI测试在各自任务统计。

所有真实运行在稳定源码提交前执行，run.json保留工作区dirty=true与基线提交；0296df89e572814085cde2a8105bb4ad5992bad5包含同一已验证代码，后续仅交接与状态更新。没有把服务READY当作验收完成。

## 历史失败与修正

首次恢复NuGet时出现源TLS重试；复制已锁定现有缓存后restore成功，不关闭TLS或审计。新增显式disposed异常会使既有fixture超出CA1506耦合阈值，采用小型ReleaseApplicationAsync收拢真正资源释放；最终build零警告。

20260908T084914Z-27ae56ca 首轮真实短周期已成功扫描，但旧fixture立即断言CNG容器不存在失败，未写通过证据。原因是Dispose后仍保留Host/DI图和证书复制体。修正为清空已释放Host引用，仅当自有容器仍存在时收集并等待finalizer，继续原不存在断言。失败运行的PG、角色、临时目录与Host已释放；其时间窗内未遗留CNG文件。第二次实际截止通过。未删除未知密钥容器，也未触碰真实NAS。

## 尚未覆盖与限制

本支架不代表Android/Windows的UI、设备、TLS负向、无障碍或50万资产性能通过；由消费者独立记录。测试PG沿用既有fixture的fsync等优化，不是断电耐久证据。没重跑不受改动影响的整个仓库平台矩阵；原生成SDK/迁移仍通过仓库门禁。没有仍运行的本任务实例或清理剩余项。
