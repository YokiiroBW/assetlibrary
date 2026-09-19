# 媒体成品包只读预检（Media Package Preflight）

状态：TS-099 已完成 R1–R6 修复轮：沙箱内编译通过（0 警告 0 错误）、冻结正反例静态自查 19/19 一致、
交付补丁可逐文件复现候选；真实测试执行待协调环境复验（见文末"证据边界"）。交付状态 `needs_validation`。

## 1. 这份契约解决什么

上游把一次 B 站下载整理成"成品包"（staging 目录 + `manifest.json`）后，核心必须在**不动任何文件**的前提下回答一个问题：

> 这个成品包是否完整、可信、可发布，以及发布后目标目录应当叫什么？

TS-099 只交付这个**只读预检**。它不搬运、不创建目标、不写库、不接 HTTP、不触碰 NAS。真正的搬运与发布属于后续任务。

## 2. 边界（不可协商）

| 项目 | 规则 |
| --- | --- |
| 写操作 | 预检全程零写入；报告恒为 `grants_file_operation=false` |
| 权限 | 先解析调用方授权；未授权时**不做任何 I/O**（连卷空间都不探测） |
| 目标目录 | 只计算名字，不创建。目标已存在（含文件、目录、链接、重解析点）→ `target_exists`，绝不复用或覆盖；目标父根缺失或不可用 → `target_unavailable` |
| 路径 | 原始清单路径先做词法拒绝，再交给 `RelativeAssetPath`；拒绝重解析点；反斜杠在任何平台都拒绝 |
| 报告 | 状态只有 `inspected` / `rejected`；问题最多 100 条并置 `truncated`；`Issue.Location` 只承载已校验相对路径或固定字段位 |
| 结论 | 报告不得出现真实绝对路径、文件内容或凭据 |
| 离线/过期授权 | 报 `target_unavailable`；`scope_changed` 只留给**运行中**被撤销的授权 |

## 3. 冻结词表

`MediaPackagePreflightCodes.All`（22 个，顺序即契约）：

```
invalid_manifest, digest_mismatch, unsupported_version, invalid_identity,
invalid_layout, invalid_path, duplicate_path, invalid_file_set, budget_exceeded,
unauthorized, scope_changed, source_missing, source_changed, unsafe_path,
hash_mismatch, size_mismatch, target_exists, target_unavailable,
insufficient_space, busy, timeout, io_failure
```

`MediaPackageUnverifiedFacts.All`（每次报告都完整回填，顺序即契约）：

```
media_decoding, quality_policy, metadata_semantics, publication, indexing,
media_server_import, production_path_races
```

**预检通过不等于可发布。** `unverified` 是报告的一部分而不是免责声明：它明确列出预检**没有**证明的事项，调用方必须自行处理。

## 4. 两种摘要，不要混用

| 摘要 | 定义 | 用途 |
| --- | --- | --- |
| `expected_digest` | 收到的**原始字节** SHA-256，不做换行归一化、不重新序列化 | 请求方声明"我发的是这些字节" |
| 夹具/清单锁定摘要 | UTF-8/**LF 归一化**后字节的 SHA-256 | 仓库内候选夹具与 `manifest.json` 的自校验 |

候选目录 `contracts/media-package/candidate-v1/manifest.json` 的 LF 归一化摘要为
`b523ba439ded3950e1ac3dcc483dcba3e3f90b4848f2e662377e770f3637db5f`。该目录是**任务候选输入**，不是生产发布合同。

## 5. 预算

| 预算 | 值 | 超限码 |
| --- | --- | --- |
| 并发预检 | 1（占用即 `busy`） | `busy` |
| 文件数 | 512 | `budget_exceeded` |
| 读取字节 | 32 GiB（`34359738368`） | `budget_exceeded` |
| 时长 | 120 s | `timeout` |
| 流缓冲 | ≤ 1 MiB | — |
| 目标余量 | +64 MiB（`67108864`），**下限**，只能抬高不能压低 | `insufficient_space` |
| 枚举条目 | 4096 | `budget_exceeded` |
| 清单大小 | ≤ 1 MiB | `invalid_manifest` |

清单自身限制：文件数 3..512；选中分 P 1..128；单文件 1..137438953472；总量 ≤ 1099511627776；
`nfo`/`source` ≤ 4 MiB；图片 ≤ 32 MiB；`cid` 匹配 `[1-9][0-9]{0,19}`；`bvid` 为 `BV` + 10 位字母数字；
`package_id`/`library_id` 为小写规范 `D` 型 UUID 且非全零；`staging_ref` 匹配 `[A-Za-z0-9][A-Za-z0-9_-]{0,63}`。

## 6. 目标目录命名

| 形态 | 目录名 |
| --- | --- |
| 单 P | `bilibili-{BV}-cid-{CID}` |
| 多 P | `bilibili-{BV}` |

包根为 `stagingRoot/<staging_ref>`，必须是 `stagingRoot` 的**直接子目录**，不再嵌套子目录。

## 6.1 布局模板（精确，不再近似匹配）

`layout` + `media_extension` + `selected_parts` 三者共同决定**唯一**可接受的文件集；模板逐字符匹配，
不接受 `StartsWith("Season ")` 或 `Contains(cid)` 这类近似判断。

| 形态 | 必需 | 可选 |
| --- | --- | --- |
| 单 P（`layout=single`） | `video.{mp4\|mkv}`（`cid` 为该 P）、`movie.nfo`（`cid: null`）、`source.json` | `poster.jpg` / `poster.png` |
| 多 P（`layout=multipart`） | 根级 `tvshow.nfo`、`source.json`，以及每个选中 cid 恰好一份 `Season 01/S01E{集数}-cid-{cid}.{mp4\|mkv}` 与同名 `.nfo` | 每个 cid 至多一张 `Season 01/S01E{集数}-cid-{cid}-thumb.{jpg\|png}`；根级至多一张 `poster.jpg` / `poster.png` |

严格性要点：`cid` 必须是声明值本身（`cid-10` 不等于 `cid-101`）；集数必须与该 cid 的
`episode_number` 一致且至少两位；未选中的 cid 一律拒绝；根级文件的 `cid` 必须是**显式 `null`**
（键缺失同样拒绝）；三层对象（根 / `selected_parts[i]` / `files[i]`）都只接受精确键集，
未知键与缺键一律 `invalid_manifest`；`media_extension` 声明为 `mp4` 时实际文件不得是 `mkv`。
校验后的清单以只读快照交给调用方，无法再被改写。

## 7. 组件与依赖方向

```
Contracts/   MediaPackageContracts.cs, MediaPackageInspectionContracts.cs
Domain/      MediaPackagePolicy.cs, MediaPackagePathPolicy.cs,
             MediaPackageShapePolicy.cs, MediaPackageLayoutPolicy.cs
Application/ MediaPackageInspectionPorts.cs, MediaPackageInspectionLimits.cs,
             MediaPackageInspectionService.cs
Infrastructure/ MediaPackageManifestReader.cs, IsolatedMediaPackageInspector.cs,
             MediaPackagePathBoundary.cs, MediaPackageFileHasher.cs
```

R1 修复轮按职责拆分了 Domain：`MediaPackageShapePolicy` 只做 JSON 形状/类型/身份读取（三层精确键集、
`cid` 的显式 null 与缺键区分、整数词素、身份词表、尺寸上限），`MediaPackageLayoutPolicy` 只做布局模板与
基数判定，`MediaPackagePolicy` 退回为编排层（预算常量、声明字节总量、真实列表比对、服务端目标目录名）。

依赖方向严格为 `Infrastructure -> Application -> Domain`，`Contracts` 被各方只读引用。
`Domain` 不含 `System.IO`（架构门禁禁止令牌 `system.io`），路径一律以原始字符串传入、由
`MediaPackagePathPolicy` 做词法判定，真实文件系统解析只发生在 `Infrastructure`。
受信根在**比较时**统一为规范正斜杠形式并沿用 scope 的 `RootPathComparison`，真实访问仍用平台原生形式。

JSON 解析使用 .NET 自带 `System.Text.Json`（`Utf8JsonReader`/`JsonDocument`），
额外施加：重复键拒绝、原始整数词法检查、UTF-8 合法性与字节预算检查。
仓库内**没有**另一套通用 JSON 解析器。

## 8. 用法

```csharp
using var service = new MediaPackageInspectionService(
    manifestReader, scopeResolver, inspector, clock, limits);

MediaPackagePreflightReport report = await service.InspectAsync(
    new MediaPackageInspectionRequest(stagingRoot, stagingRef, manifestBytes, expectedDigest, caller),
    cancellationToken);

if (report.Status != MediaPackageInspectionStatus.Inspected)
{
    // report.Issues 给出冻结错误码与相对位置；report.GrantsFileOperation 恒为 false
}
```

调用方在任何情况下都不得把 `report.Status == Inspected` 当作"已发布"。

## 9. 证据边界（TS-099 R1–R6 修复轮交付时）

已在**本工作树沙箱内**实际验证：

- `dotnet restore AssetLibrary.slnx --locked-mode` 通过；50 个在解内锁定包与
  `packages.lock.json` 的 `contentHash` 及 `.nupkg` 字节摘要双向一致。
- `dotnet build AssetLibrary.slnx -c Release`：15 个项目，**0 警告 0 错误**
  （`TreatWarningsAsErrors` + `EnforceCodeStyleInBuild` + 分析器预算全开）；
  R1–R6 改动后产品与测试项目再次单独 Release 编译，均 **0 警告 0 错误**。
- **静态自查**：以独立 Python 重读同一套冻结规则（路径词法、三层精确键集、身份词表、模板与基数、
  声明预算、大小写折叠碰撞、原始字节摘要、BOM/UTF-8/重复键/整数词素），重放 17 个反例与 2 个正例
  并校验服务端目标目录名 → **19/19 与冻结码一致，0 mismatch**。
  它是独立第二读、能发现实现偏离，但**不是**端到端预检。
- **补丁可复现**：交付补丁在 base 上以 `git apply --directory` 应用后，**15/15 文件**的字节长度与
  SHA-256 与源快照完全一致。
- `python -B tests/architecture/check_release_gates.py --target v0.1-start` 允许受控开发。
- 仓库校验的 12 个步骤通过，含 `validate_dotnet_source.py`（666 个 C# 文件，重复令牌与敏感日志检查）
  与 `validate_architecture_baseline.py`（层级依赖方向、跨模块公开层、禁止令牌、循环依赖）。

**未**在本环境执行，属 `needs_validation`：

- `dotnet test`：VSTest 与其 testhost 之间需要命名管道，本沙箱拒绝（首轮 90 秒超时中止；修复轮复跑
  时 testhost 更早在 `Process.GetProcessHandle` 处 `Win32Exception(5)` 拒绝访问）。
  **测试从未运行过，不得记为通过。**
- `dotnet format --verify-no-changes`：`MSBuildWorkspace` 的 build host 同样需要命名管道，被拒。
- `tests/database`、`tests/architecture` 的 Python 用例：沙箱拒绝在临时目录内写入/清理。
  这些是环境错误，不是断言失败。
- 自建 in-process 反射 runner 曾在真实执行路径上跑出 43 用例 4 通过 / 39 失败，但失败主因是 runner
  自身的启动目录导致夹具查找错误，**不是产品缺陷**；该路线已按协调指令停止，其原始日志仅作受限留痕，
  结果**一律不计入测试结论**。

上述各项须由协调方在固定提交后用真实命令
（`restore --locked-mode` / `build` / `test` / `format --verify-no-changes`）复验。

## 10. 未覆盖事项

静态重解析点拒绝**不能**关闭恶意并发目录替换（TOCTOU）；真实 NAS 语义、断电持久性、
媒体解码、质量策略、元数据语义、发布、索引与媒体服务器导入均未验证。
持久化发布与索引归 TS-094/TS-095。
