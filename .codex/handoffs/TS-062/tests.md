# TS062 实际验证

工作目录：`C:/YOKI/Codex/tianshu-peiban-bot/worktrees/TS-062/assetlibrary`；日期2026-09-14。所有命令明确此目录。

| 实际命令/检查 | 结果 | 边界 |
| --- | --- | --- |
| `python -I -B scripts/verify_repository.py` | passed，539架构输入；21迁移清单测试、14架构测试及SDK/依赖检查通过 | 准备阶段一次；Alpha仍blocked，不是全产品测试 |
| `dotnet restore tests/dotnet/AssetLibrary.WebGateway.Tests/AssetLibrary.WebGateway.Tests.csproj --locked-mode` | passed | SDK10.0.111，独立.runtime/ts062-dotnet、ts062-nuget |
| 同项目 `dotnet build --configuration Release --no-restore` | passed，0警告/0错误 | 现有测试项目及依赖，无业务变更 |
| `python -I -B tests/integration/tianshu/verify_service_read.py --dotnet <精确SDK路径>` | passed，8测试，0失败/跳过 | 真实Application/AssetLink端口，query和principal替身；测试类/计数/TRX SHA256见port-evidence.json |
| 驱动的TS060输入差异检查 | unchanged | services/packages/database/contracts/SDK配置/测试与原生夹具；保留原HTTP证据，不重复运行 |
| 完整新增diff人工审查、`git diff --cached --check` | passed | 仅任务允许路径；审查调用者、合同/迁移提案与实际未实现边界 |

Python工具：`C:/Users/Administrator/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe`。SDK：`C:/Users/Administrator/AppData/Local/Temp/V01-014-tooling-and-tests/tooling/dotnet/dotnet.exe`。

交接附加检查：Python运行时没有jsonschema，首次直接import失败；未安装新依赖。改用仓库现有 `tests/spikes/assetlink/schema_support.py` 的 SchemaStore（仅schema子集，覆盖此交接使用的type/required/enum/properties/items/additionalProperties）校验成功；result.changed_files与实际暂存的10个文件精确一致。此检查不宣称通用Draft2020-12验证器完整实现。

窄测试覆盖：主体来自认证principal、列表/浏览/搜索序列化、未授权/无效请求/不支持操作错误、越目录结果拒绝、无界页拒绝、超时与调用者取消分别传播。未新增虚构的生产服务实现测试，也未运行无关Web/Android/Explorer或TransferOperation套件。

TS060真实HTTPS与资源清理回执在 `docs/integrations/tianshu/evidence.json`，旧HTTP和计划TRX哈希按原文保留；本轮不持有原任务数据库、口令、可变缓存或原始TRX，不把摘要重新说成本轮执行。新TRX只在本任务.runtime，提交其哈希及计数。新服务鉴权、实时PG撤权/轮换、平台连接与重启仍需实现后验证。
