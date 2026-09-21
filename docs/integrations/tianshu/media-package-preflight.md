# 媒体成品包只读预检（Media Package Preflight）

状态：TS-099 已完成协调第五次验收后的返修轮（候选 `68a278c358dde0d7a0ce85ed89e66ccdf90a9a6c`，**第五**候选；
`acd8eb6a` 是**第四**候选）。**全部生产代码已冻结**，本轮**未改任何生产文件**；
唯一改动是一个测试文件里的一处计数语义，其余为六份文档校准。测试项目 Release 编译通过（0 警告 0 错误）。
协调第五次验收的真实结果是「**134 项展开结果：131 通过、1 失败、2 链接条件 skip**，
build/19 个 C# 格式/架构通过」，针对**上一版候选**：唯一失败是本卡测试侧的计数语义问题
（把「真实哈希次数」误当「换根次数」），唯一生产缺陷（`MediaPackageIssueSink` 内部去重集合无界增长）
已在上一候选闭合——cap=1、10000 个不同 location 后公开 `Issues=1`、内部 `recorded=1`。
真实测试执行与格式结论仍待协调环境复验（见文末"证据边界"）。交付状态 `needs_validation`。

**两种口径不要混用**：**测试方法数 128**（本卡 **70**）是 DSH 可静态数出的 `[TestMethod]` 个数；
**展开结果数 134** 是协调真实运行的结果条数。DSH 从未运行过测试，134/131/1/2 只属于协调环境。

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

诊断上限：报告最多 100 条，且**内部存储同样有界**——`MediaPackageIssueSink` 只记住**实际写入报告**的
`(code, location)` 对，因此去重集合与码表都 ≤ 100。满额后再来的**新的不同**问题只置 `truncated`，
**不落入任何内部集合**；重复的同一对既不计入上限也不置 `truncated`（它没有丢弃任何诊断）。
这一条此前被违反过：`Record` 曾先 `Add` 后判上限，cap=1 时 10000 个不同 location 会在内部留下
10000 条，现已改为**先查询、满则只标截断、有容量才记录**。

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
**在读取任何成员名或值之前**完成的代理转义良构性检查。最后一项的位置与方式都是关键：
`{"bvid":"BV1\uD800xx"}` 是**合法 JSON**、能解析成功，但读它的字符串会抛
`InvalidOperationException`（`JsonReaderHelper.TryUnescape` → `ReadIncompleteUTF16` / `ReadInvalidUTF16`），
所以判定不能建立在"先读出来再检查"之上。**判定方式因此是让解析器自己解码**：
用 `Utf8JsonReader` 遍历文档，对每个 `PropertyName` 与 `String` token 实际调用 `GetString()`，
解码失败即记 `invalid_manifest@surrogate_escape`，异常类型与原文都不进报告。
这样同一个字符无论写成 UTF-8 还是写成合法转义，在语法层行为完全一致；
字段是否允许该字符由身份/布局校验决定。
（此前一版曾用手写字节扫描，它既漏掉 `\uD800XDC00` 又误拒合法成对 `\uD83D\uDE00`，**已删除**。）
仓库内**没有**另一套通用 JSON 解析器。

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

## 9. 证据边界（协调第五次验收后的返修轮交付时）

已在**本工作树沙箱内**实际验证：

- `dotnet restore AssetLibrary.slnx --locked-mode` 通过；50 个在解内锁定包与
  `packages.lock.json` 的 `contentHash` 及 `.nupkg` 字节摘要双向一致。
- `dotnet build AssetLibrary.slnx -c Release`：15 个项目，**0 警告 0 错误**
  （`TreatWarningsAsErrors` + `EnforceCodeStyleInBuild` + 分析器预算全开）。
  本轮**未改任何生产文件**，故产品构建与真实探针**未重跑**：它们在前一候选已真实通过，
  协调第五次验收的 Release build 也通过。本轮唯一的编译动作是改动后的测试项目 Release 编译，
  **0 警告 0 错误**（`.runtime/dsh-delivery/logs/build-r5-tests-1.txt`；
  沿用的上一候选日志为 `build-r4-core-1.txt` / `build-r4-tests-1.txt`）。
- **真实程序集探针**（`.runtime/probe/verify/`，本卡私有、不进候选）：引用
  `services/core-server/bin/Release/net10.0/AssetLibrary.CoreServer.dll`，因此驱动的是真实产品程序集。
  55 行记录（`.runtime/dsh-delivery/logs/probe-run-44.txt`）复算：**诊断 sink 的内部有界性**
  （cap=1 时同一对重复 → 不置 `IsTruncated`；第二个不同对 → 置 `IsTruncated`；
  **10000 个不同 location 后公开 1 条，内部 `issues=1`、`recorded=1`、`codes=1`**）、
  **目标受信根两场景**（初始即普通文件 → `unsafe_path`、`hasher_calls=0`、不报 `target_exists`；
  首文件 hash 后变普通文件 → `unsafe_path@target`、4 次真实 hash；根被删除仍 `target_unavailable`）。
  既有项（正例/负例/边界/摘要/只读/端到端）继续成立，全部 55 行**无抛异常、无回显原文**。
  协调第五次验收在自己的环境里再次确认了内部有界与目标根两场景。
  **探针不是测试宿主**：它不跑 MSTest 夹具生命周期，也不能替代标准测试套件。
- **夹具不变性**：6 个候选夹具的 LF 归一化字节与 `HEAD` **逐字节相同**（磁盘 4 个 CRLF、2 个 LF），
  5 个被 `manifest.json` 钉住的 LF 归一化摘要 **5/5 相符**。**本轮一字未改夹具**，
  包括那个曾让测试失败的 `examples.json`——修的是测试怎么读它，不是它的内容。
- `python -B tests/architecture/check_release_gates.py --target v0.1-start` 允许受控开发。
- 仓库校验的 12 个步骤通过，含 `validate_dotnet_source.py` 与
  `validate_architecture_baseline.py`（层级依赖方向、跨模块公开层、禁止令牌、循环依赖）。
- 静态空白自查（支持性证据）：19 个 C# 文件中 **7 个仅有超过 120 列的长行**，其余规则无发现
  （`.runtime/dsh-delivery/logs/whitespace-selfcheck-r4.txt`）。**这不是全绿结论**：
  脚本退出码 1 即由超列导致，这些长行是不可安全折行的 `Justification`/文档注释/断言消息文本。

**未**在本环境执行，属 `needs_validation`：

- `dotnet test`：testhost 无法在本沙箱启动。**已知受限路径，本轮按卡 5 第 21 行不再重试**；
  **本卡 70 个测试方法一个都没跑，不得记为通过。**
  协调第五次验收给出的**真实**结果是「**134 项展开结果：131 过 1 失败 2 链接条件 skip**」，判定
  `changes_requested`：唯一失败是本卡测试侧的计数语义问题——该回归用同一个计数器兼表
  「真实哈希次数」与「换根次数」，包装器每次真实哈希后都触发回调，读 4 个文件得到 4 而断言写成 1，
  于是在换根已正确发生、最终也已正确拒绝的情况下提前失败；本轮把 `hashCalls` / `flipCount` /
  `flipAfterHashCall` 三个计数器分开并分别断言，**不硬编码哈希次数**。
  唯一生产缺陷（`MediaPackageIssueSink.Record` 先 `Add` 后判 cap）已在上一候选修好并由协调实测确认闭合，
  **全部生产代码现已冻结**；但**本轮改动尚未经过任何测试宿主**。
- `dotnet format --verify-no-changes`：`MSBuildWorkspace` 的 build host 需要命名管道，加载工作区即被
  `UnauthorizedAccessException` at `NamedPipeClientStream.TryConnect` 拒绝。**已知受限路径，本轮不再重试**、
  未提权，格式证据只有静态自查（且静态自查**并非全绿**）。
- `tests/database`、`tests/architecture` 的 Python 用例：沙箱拒绝在临时目录内写入/清理。
  这些是环境错误，不是断言失败。
- `.runtime/probe/escape` 临时探针：两次无输出超时后已按协调指令**停止**并原样保留；
  静态读取还发现它把 `examples.json` 根数组误当对象成员，故其失败**不是产品结论**，
  也不据此改夹具或 reader。

上述各项须由协调方在固定候选后用真实命令
（`restore --locked-mode` / `build` / `test` / `format --verify-no-changes`）复验。

## 10. 未覆盖事项

静态重解析点拒绝**不能**关闭恶意并发目录替换（TOCTOU）；真实 NAS 语义、断电持久性、
媒体解码、质量策略、元数据语义、发布、索引与媒体服务器导入均未验证。
持久化发布与索引归 TS-094/TS-095。
