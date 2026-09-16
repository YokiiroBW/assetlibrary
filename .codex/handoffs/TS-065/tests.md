# TS-065 实际验证命令与结果

全部在任务检出 `C:/YOKI/Codex/tianshu-peiban-bot/worktrees/TS-065/assetlibrary` 内执行；未接触真实 NAS、真实库、账号、设备或生产数据库。

## 本机 .NET 运行环境

本机 `C:\Program Files\dotnet` 只有运行时（`Microsoft.NETCore.App 6.0.36`），没有 `global.json` 要求的 SDK `10.0.111`，且工作区沙箱默认拒绝读取 `%APPDATA%\NuGet\NuGet.Config`，同时本机无外网。因此本次验证从同机既有检出借用 SDK 与离线包缓存，并把 CLI 状态目录与本任务隔离：

| 变量 | 值 |
| --- | --- |
| SDK | `C:\YOKI\Codex\AssetLibrary-worktrees\M0-004-windows-evidence\.runtime\sandbox-storage\M0-004\dotnet-10.0.111`（10.0.111，与 `global.json` 一致） |
| `DOTNET_CLI_HOME` | `<worktree>\.runtime\dotnet-home` |
| `APPDATA` | `<worktree>\.runtime\appdata`（避免读取被拒的用户级 NuGet 配置） |
| `NUGET_PACKAGES` | `C:\YOKI\Codex\AssetLibrary\.runtime\nuget`（既有离线缓存） |
| `--configfile` | `<worktree>\.runtime\nuget-offline.config`（指向该离线缓存，仅运行期产物） |
| `DOTNET_CLI_UI_LANGUAGE` | `en` |

未修改任何系统权限、ACL、全局配置或其他 venv/检出。

## 命令与结果

```text
dotnet restore tests/dotnet/AssetLibrary.ReadCore.Tests/AssetLibrary.ReadCore.Tests.csproj \
    --configfile .runtime/nuget-offline.config
  -> Restored AssetLibrary.AssetLink / AssetLibrary.CoreServer / AssetLibrary.ReadCore.Tests

dotnet build tests/dotnet/AssetLibrary.ReadCore.Tests/AssetLibrary.ReadCore.Tests.csproj \
    -c Release --no-restore -m:1 -nodeReuse:false
  -> Build succeeded. 0 Warning(s) 0 Error(s)

dotnet test tests/dotnet/AssetLibrary.ReadCore.Tests/AssetLibrary.ReadCore.Tests.csproj \
    -c Release --no-build --no-restore
  -> Passed! - Failed: 0, Passed: 96, Skipped: 25, Total: 121

dotnet test ... --filter "FullyQualifiedName~Dedup"
  -> Passed! - Failed: 0, Passed: 39, Skipped: 0, Total: 39     # 本切片新增用例

python -I -B scripts/validate_architecture_baseline.py
  -> 通过；Architecture inputs scanned: 566；0 issue

python -I -B scripts/validate_dotnet_source.py
  -> .NET source policy passed (605 C# files scanned)
```

25 项跳过全部是既有条件用例（需要 PostgreSQL 或非 Windows 平台），与本切片无关；本切片的 39 项**无跳过**。

## 边界与替身说明

- 迭代期使用 `-m:1 -nodeReuse:false`：沙箱拒绝 MSBuild 的跨进程节点通信，单进程构建等价且不改变产物。
- `dotnet test` 需要 testhost 打开父进程句柄，工作区沙箱下被拒；本会话已在获批的更宽模式下运行，测试进程与用例本身无替身。
- 所有文件证据来自 `.runtime/sandbox-storage/V01-004/tests/<guid>` 下由夹具自建并自校验的合成文件；junction 案例由 `cmd /d /c mklink /J` 创建并在清理时按链接删除。
- 无模拟哈希：字节重复结论来自 `SystemDedupContentReader` 对真实文件流的 SHA-256。

## 迭代中修正的缺陷（均由上述用例发现）

1. `SystemDedupContentReader` 对每个文件返回空流哈希 `e3b0c442…`（读首段后未写入哈希）→ 已修复；「大文件跨缓冲区」用例锁定「首段与尾部采样都进入摘要」。
2. `DedupPlanPolicy.Recount` 把 `根|相对路径` 整体当资产路径构造 → 已改为只取相对部分。
3. 计划摘要曾包含每次运行的 `AnalysisId`/`LibraryId`，使同一批文件的两次预览摘要必然不同 → 已改为只覆盖本次观察证据。
4. 自有只读遍历曾把目录当文件上报、且未检查祖先重解析点 → 已按端口语义只上报文件，并在遍历前检查整条祖先链。
