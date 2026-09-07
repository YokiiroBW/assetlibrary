# ALIGN-002 测试记录

## 执行环境

- Windows x64，独立 worktree `C:\YOKI\Codex\AssetLibrary-worktrees\ALIGN-002`。
- .NET SDK 10.0.111：`C:\Users\Administrator\AppData\Local\Temp\V01-014-tooling-and-tests\tooling\dotnet`。
- Python 3.12：`C:\Users\Administrator\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe`，全部 -B。
- NUGET_PACKAGES 复用主仓 `.runtime/nuget`；DOTNET_CLI_HOME 为本 worktree `.runtime/dotnet-home`。
- 联接源和目标均在自建 `.runtime/sandbox-storage/V01-004/tests/<guid>`；未访问真实 NAS/用户资产。Windows 使用隐藏、有 5 秒超时的 junction helper，Linux 分支使用 Directory.CreateSymbolicLink。
- 首次 SDK restore 输出开发证书安装提示；未操作现存证书，后续明确禁用自动证书与遥测。

## 执行命令

```text
dotnet restore tests/dotnet/AssetLibrary.ReadCore.Tests/AssetLibrary.ReadCore.Tests.csproj --locked-mode
dotnet format tests/dotnet/AssetLibrary.ReadCore.Tests/AssetLibrary.ReadCore.Tests.csproj --verify-no-changes --no-restore
dotnet build tests/dotnet/AssetLibrary.ReadCore.Tests/AssetLibrary.ReadCore.Tests.csproj -c Release --no-restore
dotnet test tests/dotnet/AssetLibrary.ReadCore.Tests/AssetLibrary.ReadCore.Tests.csproj -c Release --no-build --no-restore
python -B scripts/validate_dotnet_source.py
python -B scripts/validate_architecture_baseline.py
python -B -m unittest discover -s tests/architecture -p test_*.py
python -B -m unittest discover -s tests/spikes/assetlink -p test_*.py
python -B tests/architecture/check_release_gates.py --target production-file-writes
git diff --check
```

## 架构与契约测试

architecture 14/14，AssetLink contract 21/21。源码门禁扫描 209 个 C# 文件通过；架构基线扫描 114 个输入通过。production-file-writes 输出 RELEASE_GATE_BLOCKED / M0-006-G2，$LASTEXITCODE 为 3。PowerShell wrapper 曾把原生非零映射为工具 exit_code=1，不把包装值当成脚本合同。

## 通过

ReadCore 51/51（既有 43 + 新增 8），0 skipped，运行 84 ms。最终唯一测试共 86/86。restore、format、Release build 通过，0 warning/error。仅运行受影响测试，未运行无关完整 solution/数据库/平台套件。

## 失败 / 跳过

1. 初版测试触发 CA1506 类耦合门禁，拆为发现边界和初扫集成两个测试类后恢复；未调整门禁。
2. 既有 RepositorySandbox 递归清理 junction 会抛 UnauthorizedAccessException 并遮蔽断言；改为登记并先非递归清理自建联接。
3. 生产修复前执行筛选 BoundaryTests 的目标测试，6 failed / 1 passed：根/祖先返回 Available、发现器不拒绝、yield 后目录替换使初扫仍为 Completed；正常子联接不递归通过。
4. 修复后补充 EOF 边界，共新增 8 例；最终整个 ReadCore 51/51。
5. 两个新测试文件的 CRLF 触发 format ENDOFLINE；统一为仓库要求的 LF 后通过，未修改无关文件。

## 故障注入与恢复验证

- 根/祖先联接：任何条目进入应用层前拒绝。
- yield 后把普通子目录替换为库外 junction：只观察 1 个原目录，session aborted、0 committed、journal 为 DiscoveryFailed；库外 sentinel 不变。
- 正在枚举的根换为 junction：下一条/EOF 两种情况均拒绝，不读目标元数据、不报告完成。
- 不变的子联接仅有 1 个 ReparseDirectory，不发现目标 private.bin。
- 既有初扫强摘要、取消/超时/失败中止及 10,000 项分批测试保持通过。

## 清理限制

初版清理失败留下以下 9 个不含文件的目录：7 个 fixture 根和 2 个空子目录。逐项读取为 Directory、LinkTarget=null，无文件、无 reparse。后续最终测试没有新增残留：

```text
C:\YOKI\Codex\AssetLibrary-worktrees\ALIGN-002\.runtime\sandbox-storage\V01-004\tests\00de8c8b55a741c38183a0ee3c12030d\library
C:\YOKI\Codex\AssetLibrary-worktrees\ALIGN-002\.runtime\sandbox-storage\V01-004\tests\563ec70d42f4408eb48f59a6e857c181\library
C:\YOKI\Codex\AssetLibrary-worktrees\ALIGN-002\.runtime\sandbox-storage\V01-004\tests\00de8c8b55a741c38183a0ee3c12030d
C:\YOKI\Codex\AssetLibrary-worktrees\ALIGN-002\.runtime\sandbox-storage\V01-004\tests\1f46599a85724ebfa41d1f79ecee541e
C:\YOKI\Codex\AssetLibrary-worktrees\ALIGN-002\.runtime\sandbox-storage\V01-004\tests\2e414071780f47c28280d054b0d42f87
C:\YOKI\Codex\AssetLibrary-worktrees\ALIGN-002\.runtime\sandbox-storage\V01-004\tests\563ec70d42f4408eb48f59a6e857c181
C:\YOKI\Codex\AssetLibrary-worktrees\ALIGN-002\.runtime\sandbox-storage\V01-004\tests\5ca6568aa855447b9c03c01ab3adebad
C:\YOKI\Codex\AssetLibrary-worktrees\ALIGN-002\.runtime\sandbox-storage\V01-004\tests\867dc21642e0468eaf0042c6aec5476c
C:\YOKI\Codex\AssetLibrary-worktrees\ALIGN-002\.runtime\sandbox-storage\V01-004\tests\912a3cf7226a44f4808c7f45e4490250
```

首次批量递归清理在预先校验本任务 sandbox 后被自动批准审查拒绝。主协调授权评估更窄操作后，显式列出上述 9 个目录，逐项校验绝对边界/无 reparse/仅含允许的空目录树，并从叶到父只做非递归删除，仍被拒绝。两次原文原因均为 `blocked by policy`，没有更具体解释；命令未执行，未换接口绕过。已交主协调处理。

交接补全时再次只读核验这 9 个路径，均仍存在、无文件且无 reparse；两个含 `library` 的 fixture 根各有 1 个空子目录，其余 7 个目录为空。没有再次尝试清理。Git clean 只表示版本控制层面干净，不表示被忽略的运行时沙箱为空。

## 交接补全核对

- 已按 `4f0fcc2` 完整 diff、既有根校验调用链及初扫 abort/日志调用链复核；源码没有再次修改，也未重复执行已经通过的源码测试。
- `python -B scripts/validate_handoff.py` 通过，37 个必需产物与所调用架构质量门禁正常。
- `python -B -` 核对本任务结果的必需字段、类型、允许值、实现提交、分支和 10 个修改文件；对比基线 `2878150` 的实际 diff，并确认本次暂存只含交接三件套。
- `git diff --cached --check` 检查最终交接差异；这些交接检查不增加上面的 86 个唯一测试计数。

## 性能数据

有界观察与流式发现不变；新增属性查询复杂度 O((条目数 + 目录数) × 祖先深度)。未运行真实 50 万 NAS 性能验收。

## 尚未覆盖

微观 TOCTOU/no-follow 句柄、普通目录身份变化、Linux 实际运行、NAS 内核挂起隔离和生产写入没有在此被证明。原门禁与 V01-004 后续集成限制保持。
