# V03-004 交接摘要

状态：ready_for_review。支架代码、独立回归、短周期截止及完成两端真实联调后的显式停止均已验证；本任务的所有运行实例已关闭并完成资源核验。

代码提交：0296df89e572814085cde2a8105bb4ad5992bad5，分支 codex/v03-004-native-client-real-integration。本任务不声明 Android、Windows 进程外 Host、Explorer 或整个里程碑完成。

## 已交付

新增 tests/integration/native-clients/serve.py 与既有测试程序集内的 NativeClient fixture，启动真正生产 TrialHostFactory/Kestrel HTTPS、认证/会话、21 个生产迁移、6 个独立 NOINHERIT LOGIN 与扫描子进程。复用原 run_e2e 的临时 PostgreSQL 准备和清理，不另建后端，不允许接入外部数据库。

138 个合成文件加 2 个真实目录覆盖 100 条分页边界、中文嵌套路径、空文件。测试先走真实 operator bootstrap、登录、普通账号预配、库登记与首次扫描，再通过私密 connection.json 给客户端账号、库 ID 与精确 localhost 叶证书 SHA256。管理员口令/普通账号口令不进入公开证据；无真实 NAS 登录、凭据或数据。

服务自动分配端口，生命期 1..7200 秒，有私密 stop 文件。只有完整退出后通过 TRX、SHA256/mtime、Host/PG 进程、监听器、数据库/角色和临时目录核验才写通过。Android 通过 adb reverse 保留相同 Host/Origin/SAN；映射由设置它的客户端任务清理。

## 既有 fixture 的必要调整

测试 TLS 工厂新增可选有效期，默认仍一小时；native 使用指定生命期加五分钟余量，最大两小时五分钟。没有放宽生产 TLS 验证。

首个短周期检查发现测试 fixture 在 Dispose 后仍持有 WebApplication/DI 对象图，使 Data Protection 的证书复制体尚未释放。ReleaseApplicationAsync 清空已弃用引用；仅在观察到自有容器仍存在时等待 GC/finalizer，然后继续执行原有不存在断言。没有删除未知 Crypto 文件，也没有用删除文件替代释放证明。这是测试生命周期调整，不能外推为生产异常崩溃的证书生命周期证明。

## 当前证据

- 独立 Python runner 边界 5 项、TLS 默认/可选有效期 3 项通过。
- 短周期真实 Core/PG 运行 1/1 通过，截止后清理 verified：.runtime/native-clients-evidence/20260908T085311Z-adf0d3a3。
- 原真实 HTTPS/PG/Worker/Chromium E2E 1/1、零跳过通过，涵盖默认参数、Host 重启/恢复、权限及源完整性，清理 verified：.runtime/real-trial-regression/20260908T085618Z-963768ae。
- 最终 Release build、dotnet format、verify_repository 通过；迁移 manifest 21 项及架构回归 14 项通过。重复执行不重复计数，共 46 项独立已完成检查。
- 交互运行：.runtime/native-clients-evidence/20260908T085431Z-e77b23d2。Windows 与 Android owner 均确认实际 Core 联调完成后，于 2026-09-08 17:20:44 +08:00 显式停止，TRX 1/1、运行 26分7.7秒。acceptance 确认138文件不变、六角色与数据库删除、Host/PG退出、HTTPS关闭、私密临时目录删除，resource_cleanup=verified。Android owner 已移除本次 adb reverse 和设备内私密会合文件。消费者自身的测试分别记在对应任务，不在本任务重复计数。

## 架构与影响

仅测试工具、测试 fixture、自有任务和交接变更；不改 App、生产 Core、数据库迁移、协议、生成 SDK、根 CI 或依赖。沿用 C#/.NET、MSTest、Python 标准库，没有第二套业务实现。权限和资产写操作仍由现有 Core 处理；只扫描自身系统临时目录。内存和样例规模有界；未声称 50 万资产性能通过。

Android 只读审查另外指出目录搜索响应范围校验、坏游标刷新恢复、前台会话复核缺口，并发给其 owner 处理，未修改其他任务目录。

另外完成一次 Windows NSE 官方样例只读对照：确认 Attributes DWORD 与 root 默认继承 Folder verbs 符合官方注册；指出子项关联对象、FORPARSING 名称与挂载父容器刷新需要独立验证。结论已交 Windows owner，未修改其源文件或系统注册，未将 Explorer G1 未加载泛化为环境问题。

## 建议合并与后续

先合入 V03-001 的规划/ADR，再合本测试支架及最终交接，应用消费者分别按其任务提交评审。本任务没有仍运行的临时服务或未完成清理项。原生 UI/设备/TLS 负向验收及 Explorer 门禁由 V03-002、V03-003 各自记录。
