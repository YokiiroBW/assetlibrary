# V01-001 测试记录

## 执行环境

- 当前 Windows Codex worktree：`C:\YOKI\Codex\AssetLibrary-worktrees\V01-001`
- SDK：任务沙箱内的 `.NET SDK 10.0.111`，runtime `10.0.11`；`DOTNET_CLI_HOME` 与 `NUGET_PACKAGES` 均指向本任务 `.runtime/`。
- Python：Codex bundled Python 3.12，设置 `PYTHONDONTWRITEBYTECODE=1` 并使用 `-B`。
- 未安装系统级 SDK，未运行管理员命令，未操作虚拟机注册表、Explorer、服务或真实资产。

## 执行命令

在仓库根目录设置上述任务级环境后执行：

```text
dotnet restore AssetLibrary.slnx --locked-mode
dotnet format AssetLibrary.slnx --verify-no-changes --no-restore
dotnet build AssetLibrary.slnx --configuration Release --no-restore
dotnet test AssetLibrary.slnx --configuration Release --no-build --no-restore --logger "console;verbosity=normal"
dotnet package list --project AssetLibrary.slnx --include-transitive --vulnerable --no-restore --format json --output-version 1
python -B scripts/validate_dotnet_dependencies.py --solution AssetLibrary.slnx --packages-dir .runtime/nuget --vulnerability-report .runtime/dotnet-vulnerabilities.json
python -B scripts/validate_dotnet_source.py
python -B scripts/verify_repository.py
python -B -m unittest discover -s tests/repository -p "test_*.py" -v
python -B tests/architecture/check_release_gates.py --target v0.1-start
git diff --check
```

## 架构与契约测试

- `scripts/verify_repository.py`：通过；handoff、架构基线和 .NET source policy 全部通过。
- `tests/architecture/test_architecture_rules.py`：13/13 passed，包括 CI `continue-on-error` 反例、语言/manifest/lock、边界、循环依赖和启动门禁。
- `tests/repository/test_dotnet_foundation.py`：11/11 passed，包括 SDK/CI/LF 合同以及依赖、许可证、漏洞、重复源、敏感日志的正反夹具。
- 全部 repository tests：19/19 passed。
- `check_release_gates.py --target v0.1-start`：允许 scoped V0.1 implementation；没有改变 release gate ledger。

## 通过

- Locked restore：3/3 projects；固定 lock files 无漂移。
- Format/analyzers：通过；rebase 后复现并修正 Windows CRLF 检出问题后再次通过。
- Release build：3/3 projects，0 warnings，0 errors。
- MSTest：2/2 data-driven cases passed；两个服务程序集存在、可加载、无入口点且 role metadata 正确。
- NuGet audit：报告覆盖 3/3 projects，没有直接或传递漏洞。
- Dependency policy：3 projects、15 unique locked packages；NuGet.org 单一源、SHA-512 content hash 和 MIT license 全部通过。
- Source policy：扫描 4 个受管 C# 文件；无重复 60-token block，无疑似敏感日志。
- 最终唯一测试计数：34 passed，0 failed，0 skipped。
- 最终 `git diff --check` 通过，仓库内无 `__pycache__` 或 `*.pyc` 残留。

## 失败 / 跳过

- 最终失败：0；最终跳过：0。
- 中间一次 repository run 因人工 `py_compile` 留下被忽略的 cache residue 而按设计失败；残留已移入任务 `.runtime` 并改名，随后 19/19 通过且最终扫描为 0。
- 中间一次 rebase 后 format 按设计发现 C# 被 Windows `core.autocrlf` 检出成 CRLF；新增 `.gitattributes` 与合同测试后再次通过。
- Windows/Ubuntu GitHub Actions 尚未远程执行；这是待合并检查产生的外部证据，不计作本地测试跳过或通过。

## 故障注入与恢复验证

- 真实 lock drift：在 `.runtime/sandbox-storage/V01-001/lock-drift-probe` 副本中把 `MSTest.TestFramework` central version 从 `4.0.1` 改成 `4.0.2`，保留旧 lock file；`dotnet restore --locked-mode` 返回 `NU1004`、exit code 1。
- 合成依赖夹具分别注入 resolved version drift、非法 content hash、未批准 source、GPL license 和传递漏洞；每种情况均被门禁拒绝。
- 合成源码夹具注入跨目录重复 token block 和 access token 日志；两种情况均被门禁拒绝。
- 架构夹具注入 `continue-on-error: true`；CI 合同验证失败。

## 性能数据

- Windows 最终构建链路为秒级；一次记录的 Release build 为 0.94 秒，MSTest 为 0.37 秒，满足 fast-merge 10 分钟预算。
- 任务本地 NuGet cache：约 108.82 MiB / 15 package roots；包含测试依赖的 Release 输出约 7.77 MiB。二者均不属于产品发行包。

## 尚未覆盖

- 尚无远程 Windows/Ubuntu Actions 运行日志；合并检查必须产生并保留实际结果。
- 本任务没有业务 API、数据库、迁移、文件写入、Provider、Explorer、集成或契约测试对象；对应 owner 激活实现时必须在同一 solution 增加 suite。
- 没有 Linux 本地执行证据；跨平台可执行性当前由纯托管项目、LF 规则和双 runner workflow 定义保障，最终以远程 Ubuntu job 为准。
