# V01-017 测试记录

日期2026-09-07；Windows x64；.NET SDK10.0.111；PostgreSQL16.15；仓库固定MSTest/Npgsql版本，未改依赖锁。Python使用Codex bundled runtime。

## 命令和结果

在本worktree中，PATH前置V01-014临时tooling/dotnet，NUGET_PACKAGES复用主仓库`.runtime/nuget`。数据库测试显式设置 `ASSETLIBRARY_TEST_POSTGRES_REQUIRED=1`，PG bin为V01-014临时tooling/postgresql/pgsql/bin；运行根使用系统临时`AL017`缩短Windows路径，所有资产和故障测试只用沙箱。

| 实际入口 | 结果 |
|---|---|
| `dotnet restore AssetLibrary.slnx --locked-mode` | 通过，11项目 |
| `dotnet format AssetLibrary.slnx --verify-no-changes --no-restore` | 通过；仅主动格式化本任务owned文件 |
| `dotnet build AssetLibrary.slnx --configuration Release --no-restore` | 通过，0警告/0错误 |
| `python -I -B scripts/verify_repository.py` | 通过；architecture153 inputs、source289 C#、manifest18/11；14架构测试、21manifest测试和生成SDK等检查通过；Alpha仍blocked |
| `python -B -m unittest discover -s tests/database -p test_*.py -v` | 最终69/69，91.133秒，零跳过；日志`.runtime/V01-017/database-suite-final.log` |
| 数据库wrapper调用 `dotnet test tests/dotnet/AssetLibrary.ReadCore.Tests/AssetLibrary.ReadCore.Tests.csproj --configuration Release --no-build --no-restore --filter FullyQualifiedName~AssetLibrary.ReadCore.Tests --logger console;verbosity=normal` | 77/77，6.156秒，零跳过；日志`.runtime/V01-017/native-read-only-tests.log` |
| `dotnet test tests/dotnet/AssetLibrary.TaskHealth.Tests/AssetLibrary.TaskHealth.Tests.csproj --configuration Release --no-build --no-restore` | 32/32，零跳过 |
| `dotnet test tests/dotnet/AssetLibrary.AssetLink.Tests/AssetLibrary.AssetLink.Tests.csproj --configuration Release --no-build --no-restore` | 13/13，零跳过 |
| `git diff --check`、旧1–13迁移diff检查 | 通过，旧迁移无变化 |

ReadCore的原生测试由 `test_read_only_trial_dotnet_runtime_and_isolated_workers` 创建真实PostgreSQL并注入受限runtime连接、独立测试admin连接、已构建Host路径和dotnet路径；断言不得出现Skipped。Windows ACL只属于Windows试用平台证据；其他平台过滤这一平台特有测试，不将其计作通过。

## 新增行为和故障证据

26项新增ReadCore原生/协议场景：登记/重叠/幂等冲突与回放；同库四个并发请求只产生一个活动任务；离线单库不影响同源其他库，恢复立即fresh probe；排队取消、运行中取消、独立幂等取消；超时不会变空索引成功；接受后未入队崩溃补投；旧lease未提交stage清理后重试；已提交但journal响应丢失；最终尝试过期失败后的snapshot成功修复；失租/取消guard不调用资产提交；三个模块SET LOCAL ROLE和跨schema写拒绝；SQLSTATE53100容量故障保留源并允许恢复重试；真实Windows拒绝ListDirectory再恢复；宽目录DFS、Unicode路径与源强摘要/mtime不变。

真实Host worker验证之外，受控Python子进程分别模拟缺终帧、错误计数、非零退出、超大帧、stderr洪水、不读取stdin。Windows未读取stdin在约0.43秒触发startup deadline，所有故障均确认进程已reap；未留下阻塞NAS线程。没有把进程模拟称为真实NAS断网或内核故障。

迁移14另验证：BEGIN READ ONLY准备查询；停用管理员可ready；恢复后准备仍返回旧expected version，CAS重放不再次增版本；过期、未知账号、auth/op绑定冲突、其他模块runtime拒绝。既有数据库全套仍覆盖备份/恢复、迁移摘要、重启持久性、100k目录索引计划、队列租约和并发、原读权限撤销等。

## 发现后修正

- 初次构建暴露新类耦合与重复事务模板，按职责拆分并复用已出现的三个技术消费者；未降低分析器阈值或加抑制。
- Windows ACL测试最初未显式写回原descriptor，故cleanup失败；已修正且77项全套中该测试通过。旧fixture权限已通过更小的不删除操作恢复；自动审批仍拒绝过的递归删除不再重试，残留路径见summary.md。
- 首次数据库全套68/69通过，唯一失败是旧静态断言认为TaskHealth仅有迁移6；改为精确验证6和18、各自owner/path后，定向检查及最终69/69全套均通过。

## 边界

没有运行真实用户NAS或修改用户原文件。未提供真实50万资产、NAS长时间soak、内核级原子no-follow、全平台服务安装或浏览器最终部署证据；这些不得由当前单元/集成数据替代。最终Web/HTTPS包由协调器及对应任务验证。

## 2026-09-07 Windows父硬死增量（独立计数）

源提交 `43a4f2c`。新增命令：

`python -B -m unittest discover -s tests/dotnet/AssetLibrary.ReadCore.Tests -p test_worker_lifetime.py -v`

设置 `ASSETLIBRARY_TEST_DOTNET` 为已固定的10.0.111工具路径，并先构建当前Release Core。该测试只在Windows执行；在系统临时目录把已提交WorkerParentFixture.cs构建为父进程控制台，引用现有Core和共享框架，无PackageReference、新生产项目或根锁文件。

- **修复前：1项失败，7.885秒。** 父使用生产transport，child确认收到请求后不再读写管道并阻塞120秒。控制器仅调用Windows TerminateProcess结束父PID，等待child原生句柄5秒仍为WAIT_TIMEOUT。控制器最后清理自己的进程，未遗留本次测试进程。
- **修复后：新增1/1通过，1.071秒。** 同样硬杀父进程，没有Ctrl-C、正常Exit或杀树操作；Job关闭后child句柄在5秒内发出退出信号。保留原生句柄避免PID复用误判。
- **受影响既有回归10/10通过，0.8795秒。** `dotnet test tests/dotnet/AssetLibrary.ReadCore.Tests/AssetLibrary.ReadCore.Tests.csproj --configuration Release --no-build --no-restore --filter FullyQualifiedName~IsolatedReadOnlyWorker --logger "console;verbosity=normal"`；覆盖6种worker故障、正常协议/只读、取消、宽目录遍历。
- `dotnet format AssetLibrary.slnx --verify-no-changes --no-restore` 与完整Release build通过，0警告/0错误。源码策略291 C#、架构153 inputs通过；未提高阈值或抑制警告。

该增量没有改动SQL或其他业务源，未重复数据库69项套件。原77/69基线保持原含义；新增1项及重跑的10项分开记录。Linux不具有本次Windows Job父死亡证据，未宣称已验证。
