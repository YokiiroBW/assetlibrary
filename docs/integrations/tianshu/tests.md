# TS-060 实测记录

2026-09-14，Windows；AssetLibrary `99e79f2f5a6d3b84ff2faaa9640752569796eb69` 加本任务文档/测试。未改业务源码。平台 `a5ee59f` 仅按固定提交只读审查公开文档，没有请求平台服务。

## 结果

| 检查 | 实际结果 | 证据边界 |
| --- | --- | --- |
| `scripts/verify_repository.py` | 通过；准备阶段与新测试完成后各一次 | 架构/源码/契约生成/清单检查；内含21迁移清单、14架构测试。不是全产品原生构建或NAS验收。Alpha仍 `blocked`。 |
| WebGateway.Tests、TransferOperation.Tests locked restore + Release build | 两项目及其依赖通过，0警告/0错误 | 精确SDK10.0.111；所有缓存、构建结果属于本任务检出。产品锁文件未变。 |
| 候选 schema / 关系负例 | 7项通过，0跳过 | 人工合成建议；不执行分析器或平台消费者。 |
| `probe_core.py` | 1次真实临时Core，11组HTTP检查通过 | 真HTTPS/认证/库授权/PostgreSQL16.15/生产迁移/真实扫描；138合成文件。检查组不能按组数记成11个独立E2E。 |
| 现有计划/传输沙箱套件 | 64项通过，0跳过 | 真实应用服务与测试文件I/O；内存计划store、测试授权/空间/故障端口，不是生产执行器或持久发布。 |
| Python语法与最终差异检查 | 通过 | 仅授权文档/测试文件；未改共享合同、源代码、锁/迁移、根任务板或协调检出。 |

脱敏可提交回执见 [evidence.json](evidence.json)，包括原始本地回执/TRX的SHA256。HTTP回执明确 `platform=not_connected`、`production_publishing=unavailable`、`web_ui=not_tested_static_shell_only`。HIBP只使用已有测试handler，认证和授权逻辑未替换。

HTTP实际验证401、授权库发现、137个根文件按100+37分页且ID不重复、page_size101拒绝、游标改排序拒绝、路径穿越拒绝、稳定详情无哈希冒认、中文范围搜索、已提交首次扫描、五个候选操作返回400 unsupported_operation、Origin/CSRF403、真实普通账号的空列表/404/403、两账号退出后会话401。成功envelope严格关联request_id；解析阶段错误允许既有 `unknown`，从不当作成功。

服务端结束回执：`sample_file_count=138`、SHA256/mtime unchanged、六个临时LOGIN删除、临时库与私密runtime移除、HTTPS监听关闭、Host/PostgreSQL退出。本轮自有外层系统临时目录也已确认不存在；64项测试结束后 `sandbox-storage/V01-007` 无剩余fixture。未接触真实NAS或资产数据库。

## 实际命令

下列变量仅缩短展示，指向本轮实际使用的可执行文件；所有命令工作目录均为 `C:/YOKI/Codex/tianshu-peiban-bot/worktrees/TS-060/assetlibrary`。

```powershell
$tsPython = 'C:/Users/Administrator/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe'
$tsDotnet = 'C:/Users/Administrator/AppData/Local/Temp/V01-014-tooling-and-tests/tooling/dotnet/dotnet.exe'
$tsPg = 'C:/Users/Administrator/AppData/Local/Temp/V01-014-tooling-and-tests/tooling/postgresql/pgsql/bin'
& $tsPython -I -B scripts/verify_repository.py
& $tsPython -m venv .runtime/ts060-venv
& .runtime/ts060-venv/Scripts/python.exe -m pip install jsonschema==4.26.0
& $tsDotnet restore tests/dotnet/AssetLibrary.WebGateway.Tests/AssetLibrary.WebGateway.Tests.csproj --locked-mode
& $tsDotnet restore tests/dotnet/AssetLibrary.TransferOperation.Tests/AssetLibrary.TransferOperation.Tests.csproj --locked-mode
& $tsDotnet build tests/dotnet/AssetLibrary.WebGateway.Tests/AssetLibrary.WebGateway.Tests.csproj --configuration Release --no-restore
& $tsDotnet build tests/dotnet/AssetLibrary.TransferOperation.Tests/AssetLibrary.TransferOperation.Tests.csproj --configuration Release --no-restore
git diff --cached --check
& .runtime/ts060-venv/Scripts/python.exe -I -B -m unittest discover -s tests/integration/tianshu -p 'test_*.py' -v
& .runtime/ts060-venv/Scripts/python.exe -I -B tests/integration/tianshu/probe_core.py --execute --dotnet $tsDotnet --postgres-bin $tsPg
& $tsDotnet test tests/dotnet/AssetLibrary.TransferOperation.Tests/AssetLibrary.TransferOperation.Tests.csproj --configuration Release --no-build --no-restore --logger 'trx;LogFileName=ts060-plans.trx' --results-directory .runtime/ts060-plans
& .runtime/ts060-venv/Scripts/python.exe -I -B scripts/verify_repository.py
git diff --check
```

`probe_core.py`内部调用的生产夹具命令与自有路径见该脚本和 `.runtime/ts060-http-899dfc93e73a/runner.log`。此日志为本机定位文件，不提交。可提交结果复制自同目录 `http-checks.json`；原计划TRX为 `.runtime/ts060-plans/ts060-plans.trx`。

测试环境实际依赖：Python3.12.14、jsonschema4.26.0、attrs26.1.0、jsonschema-specifications2025.9.1、referencing0.37.0、rpds-py2026.6.3、typing_extensions4.16.0。仅为本任务工具，未加入产品依赖或改锁。

## 失败与修正

- 首次候选运行6通过/1失败：最小jsonschema安装没有可选RFC3339 checker，`format`被跳过。补充显式UTC日期检查和schema形状约束，未隐藏失败；最终7项通过。
- 首次真实Core启动在迁移前备份处失败，原因是长worktree临时路径超过Windows路径长度限制，pg_dump无法创建归档；尚未进行HTTP验收。既有夹具完成失败清理，失败runtime已确认不存在。探针改用本次自有系统临时父目录，证据和工具环境仍在本任务`.runtime`；第二次完整通过。
- 系统`python`不在PATH，系统dotnet没有SDK；使用已核实便携工具，不修改global.json或降SDK。便携二进制是工具来源，不复用旧任务数据、凭据或数据库。

未执行：真实平台到Core适配、内容查重/分类/优选服务、生产发布、真实下载/阅读器、图片decoder、NAS/真实设备、全产品/平台发布门禁、50万资产与断电耐久。成功局部测试不改变这些状态。
