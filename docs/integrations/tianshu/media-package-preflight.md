# 媒体成品包只读预检（Media Package Preflight）

状态：TS-099 已完成协调第三次验收后的返修轮：沙箱内产品与测试 Release 编译通过（0 警告 0 错误），
私有探针在**真实产品程序集**上复算了本轮每一项（固定 `Season 01` 的三种反例、目标受信根对象种类、
JSON 解码守卫、sink 去重与截断，另有既有正例/负例/边界/摘要/只读/端到端项）。
协调第三次验收的真实结果（128 项 120 过 6 失败 2 链接条件 skip）针对**上一版候选**，
6 个失败全部是本卡测试侧问题并已更正；真实测试执行与格式结论仍待协调环境复验（见文末"证据边界"）。
交付状态 `needs_validation`。

## 1. 这份契约解决什么

上游把一次 B 站下载整理成"成品包"（staging 目录 + `manifest.json`）后，核心必须在**不动任何文件**的前提下回答一个问题：

> 这个成品包是否完整、可信、可发布，以及发布后目标目录应当叫什么？

TS-099 只交付这个**只读预检**。它不搬运、不创建目标、不写库、不接 HTTP、不触碰 NAS。真正的搬运与发布属于后续任务。

## 2. 边界（不可协商）

| 项目 | 规则 |
| --- | --- |
| 写操作 | 预检全程零写入；报告恒为 `grants_file_operation=false` |
| 权限 | 先解析调用方授权；未授权时**不做任何 I/O**（连卷空间都不探测） |
| 目标目录 | 只计算名字，不创建。目标已存在（含文件、目录、链接、重解析点）→ `target_exists`，绝不复用或覆盖；目标父根缺失 → `target_unavailable`；目标受信根**存在但不是目录**（普通文件或不可观察）→ `unsafe_path`，绝不读成「目标还不存在」 |
| 路径 | 原始清单路径先做词法拒绝，再交给 `RelativeAssetPath`；拒绝重解析点；反斜杠在任何平台都拒绝 |
| 分隔符 | 清单词表是 POSIX：`/` 在**所有平台**被接受，并在真实访问前转成平台原生形式；`\` 在任何平台都拒绝；结尾分隔符不属于冻结文件路径 |
| 报告 | 状态只有 `inspected` / `rejected`；问题最多 100 条；只有**新的不同**问题因容量被丢弃才置 `truncated`（重复的同一问题不算丢弃）；`Issue.Location` 只承载已校验相对路径或固定字段位 |
| 结论 | 报告不得出现真实绝对路径、文件内容或凭据 |
| 离线/过期授权 | 存储离线或不可用 → `target_unavailable`；许可到期、被撤权或版本改变 → `scope_changed`；`source_missing` 仅指源 |
| 清单字符串 | 任何 `\uXXXX` 转义若只拼出代理对的一半，一律 `invalid_manifest@surrogate_escape`；判定方式是用 `System.Text.Json` 的 token 与解码能力**实际解码**每个属性名与字符串值，解码失败即拒绝；合法成对与等价的 UTF-8 写法行为完全一致 |
| 读后复核 | 每个文件保留长度与最后写入时间戳（**仅 Inspector 内部记录，不进公开端口**），全部读完后逐一复核；长度相同但时间戳变化的等长改写同样报 `source_changed`；目标受信根在首次读取前与最终确认时各验证一次「是目录」 |

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

**集节目录是合同常量 `Season 01`，不可由输入推导。** 这一点曾在本卡早期被写反（当时写成
「由声明的分集条目自己推导」），随后被协调验收判为**未经授权的合同变更**并已更正：
`contracts/media-package/candidate-v1/semantics.md:22` 钉死了 `Season 01/` 这条路径，
从输入推导目录等于让被检查的包自己定义模板，于是「全部集节目录统一改成 `Season 99/`」
或「全部去掉 Season 目录」的包都会被接受——而这正是预检要拦的情况。

当前行为：多集的每一个集节目录条目都必须落在 `Season 01/` 下；全部集节目录统一移到其它目录、
或全部去掉 Season 目录，都逐条记 `invalid_file_set`；自定子目录（如 `Season 01/Extras/`）
另记 `invalid_path`。常量定义在 `MediaPackageLayoutPolicy.EpisodeDirectory`，若未来合同允许其它季目录名，
必须显式改合同与该常量，**不得**回到由输入推导。

严格性要点：`cid` 必须是声明值本身（`cid-10` 不等于 `cid-101`）；集数必须与该 cid 的
`episode_number` 一致且至少两位；未选中的 cid 一律拒绝；根级文件的 `cid` 必须是**显式 `null`**
（键缺失同样拒绝）；三层对象（根 / `selected_parts[i]` / `files[i]`）都只接受精确键集，
未知键与缺键一律 `invalid_manifest`；`media_extension` 声明为 `mp4` 时实际文件不得是 `mkv`；
比对针对**整条路径**，不接受「去掉目录后文件名相同」的文件；同一 part 出现第二个视频 / nfo / thumb
即 `invalid_file_set`（不同 part 的同名角色不是重复），根级角色仍全局唯一。
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
R2 返修轮把这两个策略类收紧为 `internal`（只被同模块的 `MediaPackagePolicy` 使用），
`MediaPackagePolicy` 与 `MediaPackagePathPolicy` 保持 `public`，因为已提交的边界与契约测试直接调用它们。

依赖方向严格为 `Infrastructure -> Application -> Domain`，`Contracts` 被各方只读引用。
`Domain` 不含 `System.IO`（架构门禁禁止令牌 `system.io`），路径一律以原始字符串传入、由
`MediaPackagePathPolicy` 做词法判定，真实文件系统解析只发生在 `Infrastructure`。
受信根在**比较时**统一为规范正斜杠形式并沿用 scope 的 `RootPathComparison`，真实访问仍用平台原生形式。

JSON 解析使用 .NET 自带 `System.Text.Json`（`Utf8JsonReader`/`JsonDocument`），
额外施加：重复键拒绝、原始整数词法检查、UTF-8 合法性与字节预算检查，以及
**在读取任何成员名或值之前**按原始字节完成的代理转义良构性检查。最后一项的位置是关键：
`{"bvid":"BV1\uD800xx"}` 是**合法 JSON**、能解析成功，但读它的字符串会抛
`InvalidOperationException`（`JsonReaderHelper.TryUnescape` → `ReadIncompleteUTF16` / `ReadInvalidUTF16`），
所以判定不能建立在"先读出来再检查"之上。命中即记 `invalid_manifest@surrogate_escape`，
异常类型与原文都不进报告。仓库内**没有**另一套通用 JSON 解析器。

## 8. 用法

```csharp
using var service = new MediaPackageInspectionService(
    manifestReader, scopeQuery, inspector, clock, limits);

MediaPackagePreflightReport report = await service.InspectAsync(
    new MediaPackagePreflightRequest(
        new MediaPackageCallerContext("caller-1"),
        manifestBytes,
        expectedDigest),
    cancellationToken);

if (report.Status != MediaPackageInspectionStatus.Inspected)
{
    // report.Issues 给出冻结错误码与相对位置；report.GrantsFileOperation 恒为 false
}
```

调用方在任何情况下都不得把 `report.Status == Inspected` 当作"已发布"。

## 9. 证据边界（协调第三次验收后的返修轮交付时）

已在**本工作树沙箱内**实际验证：

- `dotnet restore AssetLibrary.slnx --locked-mode` 通过；50 个在解内锁定包与
  `packages.lock.json` 的 `contentHash` 及 `.nupkg` 字节摘要双向一致。
- `dotnet build AssetLibrary.slnx -c Release`：15 个项目，**0 警告 0 错误**
  （`TreatWarningsAsErrors` + `EnforceCodeStyleInBuild` + 分析器预算全开）；
  本轮改动后产品与测试项目再次单独 Release 编译，均 **0 警告 0 错误**
  （`.runtime/dsh-delivery/logs/build-r3-core-final.txt` / `build-r3-tests-final.txt`）。
- **真实程序集探针**（`.runtime/probe/verify/`，本卡私有、不进候选）：引用
  `services/core-server/bin/Release/net10.0/AssetLibrary.CoreServer.dll`，因此驱动的是真实产品程序集。
  本轮（55 行记录）新增复算：**固定 `Season 01`**（全部集节目录统一移到 `Season 99/`、全部去掉
  Season 目录、自定子目录三种反例，原拼写仍可读）、**目标受信根对象种类**（初始即普通文件 →
  `unsafe_path` 且不报 `target_exists`；首文件 hash 后变普通文件 → `unsafe_path@target`；
  根被删除仍 `target_unavailable`）、**JSON 解码守卫**（`\uD800XDC00` 在 bvid / path / 键名三处
  零异常零回显，合法成对 `\uD83D\uDE00` 的转义写法与 UTF-8 写法诊断完全一致，字面 `\\uD800`
  按普通字符串处理）、**sink 去重与截断**（cap=1 时重复对不置 `IsTruncated`、新对不同对才置 true、
  10000 个不同键仍只持有 1 项）。既有项（正例/负例/边界/摘要/只读/端到端）继续成立。
  输出：`.runtime/dsh-delivery/logs/probe-run-42.txt`。
- **夹具不变性**：6 个候选夹具的 LF 归一化字节与 `HEAD` **逐字节相同**（磁盘 4 个 CRLF、2 个 LF），
  5 个被 `manifest.json` 钉住的 LF 归一化摘要 **5/5 相符**。
- `python -B tests/architecture/check_release_gates.py --target v0.1-start` 允许受控开发。
- 仓库校验的 12 个步骤通过，含 `validate_dotnet_source.py`（666 个 C# 文件，重复令牌与敏感日志检查）
  与 `validate_architecture_baseline.py`（层级依赖方向、跨模块公开层、禁止令牌、循环依赖）。

**未**在本环境执行，属 `needs_validation`：

- `dotnet test`：testhost 无法在本沙箱启动。**本卡 68 个测试一个都没跑，不得记为通过。**
  协调第三次验收给出的**真实**结果是「128 项 120 过 6 失败 2 链接条件 skip」，判定
  `changes_requested`；6 个失败全部是本卡测试侧的构造/断言问题（原地改大小写没有第二条条目、
  `ThrowsExactly` 要求精确基类、`ResolveCalls` 与授权前判定矛盾、两个读预算用例基准取错、
  枚举上限强置截断），已逐条更正，但**本轮改动尚未经过任何测试宿主**。
- `dotnet format --verify-no-changes`：`MSBuildWorkspace` 的 build host 需要命名管道，加载工作区即被
  `UnauthorizedAccessException` at `NamedPipeClientStream.TryConnect` 拒绝；缩小 `--include` 重跑同样失败。
  本轮未再重跑、未提权，格式证据只有静态自查。
- `tests/database`、`tests/architecture` 的 Python 用例：沙箱拒绝在临时目录内写入/清理。
  这些是环境错误，不是断言失败。

上述各项须由协调方在固定候选后用真实命令
（`restore --locked-mode` / `build` / `test` / `format --verify-no-changes`）复验。

## 10. 未覆盖事项

静态重解析点拒绝**不能**关闭恶意并发目录替换（TOCTOU）；真实 NAS 语义、断电持久性、
媒体解码、质量策略、元数据语义、发布、索引与媒体服务器导入均未验证。
持久化发布与索引归 TS-094/TS-095。
