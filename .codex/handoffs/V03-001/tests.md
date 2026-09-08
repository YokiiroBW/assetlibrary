# V03-001 集成验证

## 完整行为与来源

- Android：21 JVM协议/状态、WorkspaceUiTest和RealCoreUiTest，共23个不同原生用例通过。手机、平板深色、200%字体以同用例不同配置验证，不增加独立计数。Debug/Release/Lint、strict锁/verification、apksigner与真实Core/PG均通过，详见V03-003。
- Windows：22个标准.NET单元/协议及1个有效真实Core用例通过。真实联调后的两项新增校验（CSRF控制字符、陈旧翻页）由专门正/负路径覆盖；额外尝试在正常fixture清理后未执行，不伪造新现场证据。C++真实编译/analyze和可逆HKCU独立探针通过；1个真实Explorer入口场景失败、根因未定。详见V03-002，G1..G4不变。
- 真实fixture：5个Python边界、3个TLS、期限退出/显式stop两个真实Core生命周期与旧Web真实E2E，共11项通过，所有本轮临时资源回收verified，见V03-004。
- root审计脚本：5个失败关闭测试通过，覆盖不完整/错误OSV结果、坐标关联、无效inventory、POM许可继承和陈旧/不匹配外部response拒绝。
- 公共迁移21/架构14共35项，按同一逻辑测试计一次，不把子任务重复执行相加。总97通过，另有Windows实际入口1失败。构建/审计/CLI不冒充单元用例。

## 集成检查

最终代码/调用方/权限边界/依赖diff已审查。services、database、contracts、packages/sdk相对于56361e9无改动。合并后的Android源码、主题JSON及Kotlin生成源与已构建e24331a逐项Git diff相同；不重建或重跑未变化业务。Windows协议组件和fixture已在各自最终稳定范围验证；根级统一校验在ce454c6上通过并记录到delivery.json，实际扫描373个C#文件、两原生源根和475个Android校验组件；Alpha仍合法blocked。

真实入口：`python -I -B scripts/verify_repository.py`。新增CI运行Android原生构建/Lint/协议/选定API36仪器及runtime审计、Windows协议构建/测试和test-only C++编译；本轮只核对CI配置与本机等价命令，未声称远程GitHub Actions已经执行。Windows CI没有注册Shell，也不替代Windows11实际Explorer证据。

当前环境Python不在默认PATH，实际使用bundled Python绝对路径，子进程沿sys.executable。日志 `.runtime/final-repository-verification.log`。原Android/.NET构建日志和原生JUnit/TRX见子任务目录，重复执行不作为新证据。

## 包、依赖与安全

APK 30064814 bytes，SHA256 afd7cbd3f387aebc1f2d00ec34c3f824909673615546404b81afd2a24a79b8cb；coordinator和用户主目录两份均重新读取完整哈希。Root没有重新签名、修改APK或读取初始管理员口令。

`validate_android_dependencies.py`实际审计101runtime POM许可、OSV与475组件SHA256元数据；报告在V03-003。外部API首次100条批量失败，Node TLS验证fetch按10条全部成功，再以`--osv-response`校验完整性/时效/inventory SHA，不为缺失结果填空。`nas-read-only-smoke.json`记录原公开证书正常信任下200，非登录/资产读请求。

UI残留与限制：注册键已清理；电脑输入被拒绝，8个任务窗口等待重新观测/安全关闭。该项尚未完成，不计资源全部清理；其他fixture进程/库/角色/监听/临时目录/模拟器均回收。没有在真实资产上做破坏性测试。所有未完成Explorer、写入、完整Alpha/V0.3与真机/规模门禁保留。
