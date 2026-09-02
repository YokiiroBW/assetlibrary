# V01-001 交接摘要

## 完成状态

`ready_for_review`

- 分支：`codex/v01-001-build-foundation`
- 实现 commit：`c231a3e218714cc5731741ca9f3d4d6d6319288d`
- Worktree：`C:\YOKI\Codex\AssetLibrary-worktrees\V01-001`

## 完成内容

- 新增 `AssetLibrary.slnx`，纳入 Core Server、Worker Supervisor 和非空 MSTest 底座测试。
- 用 `global.json` 精确固定 SDK `10.0.111`，统一 `net10.0`、C# 14、nullable、确定性构建、warnings-as-errors 与 Roslyn 复杂度门禁。
- Core Server 只引用 ASP.NET Core 10 shared framework；Worker Supervisor 暂不引入 Hosting 包。两个程序集都没有入口点、监听器、Provider、数据库或文件行为。
- 启用 central package management、每项目 `packages.lock.json`、锁定还原、NuGet.org 单一源、SHA-512 内容哈希、许可证和直接/传递漏洞检查。
- 新增重复 C# token block 与敏感日志扫描，并为通过和故障路径增加仓库测试。
- `fast-merge` 改为 Windows/Ubuntu 矩阵，两个 runner 都执行固定 SDK、restore、format、Release build、MSTest、漏洞、许可证和源码策略门禁；任何步骤失败即阻断。
- 新增 `.gitattributes` 固定 .NET 源与项目文件为 LF，防止 Windows `core.autocrlf` 令同一提交在重新检出后无法通过格式门禁。

## 关键决策

- 复用 M0-004 已验证的 SDK `10.0.111` / runtime `10.0.11`，本任务没有安装或修改系统 SDK。
- 服务项目保持 class library 标记；后续业务 owner 再按模块边界引入入口点、Hosting、数据库和端口适配器。
- MSTest 使用三个精确的直接包，而不使用会扩大扩展面和许可证面的测试元包；当前 15 个唯一锁定包的机器可读许可证均为 MIT。
- 安全更新通过显式修改 `Directory.Packages.props`、重新生成 lock files 并运行 NuGet 漏洞审计进入；不接受浮动版本或静默自动升级。

## 修改文件

- 构建根：`.gitattributes`、`.editorconfig`、`AssetLibrary.slnx`、`global.json`、`Directory.Build.props`、`Directory.Packages.props`、`NuGet.config`。
- 工程策略：`eng/CodeMetricsConfig.txt`、`eng/dotnet-dependency-policy.json`、`eng/README.md`。
- 空程序集：`services/core-server/**`、`services/worker-supervisor/**` 的项目清单、程序集标记、lock file 与说明。
- 测试与门禁：`tests/dotnet/**`、`tests/repository/test_dotnet_foundation.py`、架构规则/测试、两个新增验证脚本和仓库验证入口。
- CI 与任务记录：`.github/workflows/handoff-quality.yml`、`.codex/tasks/V01-001.md`、本交接三件套。

完整清单见 `result.json.changed_files`。

## 模块边界、依赖方向与复用

- 两个服务程序集之间没有引用；测试项目只引用二者验证构建产物和无入口点属性。
- 没有 Domain/Application/Infrastructure 业务代码，没有跨模块内部访问，也没有创建公共产品契约。
- 复用 M0-009 的架构验证、handoff 验证、CI tier 合同和 V0.1 启动门禁；没有复制权限、路径、传输、同步、垃圾桶或 Provider 逻辑。

## 新语言、框架或重大依赖

- 激活 ADR-0012 已批准的 .NET 10 / C# 14 / ASP.NET Core 10 预算，没有新增未批准的技术家族。
- Core Server 的 `Microsoft.AspNetCore.App` 是 shared framework reference，不是额外 NuGet 运行时包；Worker 空标记只使用 `Microsoft.NETCore.App`。
- 新增的三个直接 NuGet 包全部为测试专用：`Microsoft.NET.Test.Sdk 18.0.0`、`MSTest.TestAdapter 4.0.1`、`MSTest.TestFramework 4.0.1`。完整 15 包闭包已锁定、无已报告漏洞、许可证均为 MIT。
- 本机任务级 NuGet 缓存约 108.82 MiB；含测试依赖的 Release 输出约 7.77 MiB，不进入产品发行。若替换测试框架，可删除测试项目和三个 central versions，不影响两个生产程序集。

## 共享契约或数据库变化

- 公共 AssetLink、Provider、API、事件和数据库 schema/migration：无变化。
- 仅更新内部机器合同：`.NET` 项目/lock file 成为架构必需项，`fast-merge` 原生门禁由“待激活”变为 Windows/Ubuntu 双平台执行。

## 测试结果

- 最终自动测试：34 passed，0 failed，0 skipped（repository 19、architecture 13、MSTest 2）。
- 固定 SDK 锁定还原、format、Release build、漏洞/许可证、源码策略、handoff、架构和 `v0.1-start` 门禁全部通过。
- 独立 lock-drift 副本把 MSTest framework 从 `4.0.1` 改为 `4.0.2` 后，真实 `--locked-mode` restore 以 `NU1004` 和 exit code 1 失败。
- 详细命令和故障夹具见 `tests.md`。

## 架构测试与质量门禁

- 架构规则继续检查语言预算、manifest/lock file、依赖方向、跨模块访问、循环依赖和 CI fail-closed 行为。
- 依赖门禁覆盖版本漂移、非法 SHA-512、未批准 package source、未批准许可证以及直接/传递漏洞。
- 源码门禁覆盖仓库通过样例、重复代码反例和敏感日志反例。
- `.gitattributes` 合同测试保证 Windows 重新检出后 C#、项目、props 和 slnx 仍使用 LF。

## 文件安全、权限与性能影响

- 没有读取或写入真实资产，没有注册表、Explorer、系统服务、管理员权限、网络监听或生产数据库行为。
- 所有生成物、NuGet cache、负向 lock-drift 副本均位于被忽略的 `.runtime/`；没有修改系统 PATH、全局 SDK 或全局包缓存。
- 当前只有两个极小的 inert assemblies 和测试门禁；不存在 50 万资产扫描、列表或大文件内存路径。

## 技术债、已知问题与风险

- Windows 和 Ubuntu GitHub Actions 矩阵已定义但尚未在远程 runner 上执行；本机仅完成 Windows 等价链路，远程日志需由合并检查产生。
- 重复代码和敏感日志检查是 fail-closed 的词法门禁，不代替后续语义分析；随着业务源码增长需要用受控基线校准误报。
- V01-001 不拥有集成、契约或迁移实现；这些 suite 必须在对应业务和 V01-003 owner 激活时加入同一 solution，不能把当前 build test 当成功能验证。
- SDK 精确固定意味着升级必须显式评审；继续依靠 NuGet audit 与受控 SDK 更新，而不是 roll-forward。

## 建议合并顺序

V0.1 顺序 `1`。先合并 V01-001，再允许依赖它的数据库迁移底座进入集成；V01-002 的 SDK 生成任务可按自己的既有依赖独立推进。

## 下一步

- 主协调线程复核 commit、交接、最终 diff 并 fast-forward 合并。
- 合并检查在 Windows 与 Ubuntu runner 上产生首份远程 .NET 门禁证据。
- V0.1 release、生产文件写入、Provider、Explorer 和平台发布仍受既有 M0 残余门禁阻断，本任务没有关闭这些门禁。

## Codex 线程链接（可选）

未填写；仓库 commit 与交接文件是权威证据。
