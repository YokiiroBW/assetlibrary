# TS-099 交付摘要（第四次返修轮）

## 一句话

协调第四次验收 `acd8eb6a9b4a77044021e2a6ba272d7249a38fd3` 判 `changes_requested` 后，本轮只做**一项生产缺陷**
与**测试/文档收尾**：`MediaPackageIssueSink.Record` 原来**先 Add 后判 cap**，cap=1 时 10000 个不同键会在内部
去重集合里留下 10000 条（公开问题只有 1 条），本轮改为**先 Contains、满则只标截断、有容量才 Add**；唯一失败的
测试是重复路径夹具拼成了非法 JSON，改为向 `files` 数组追加完整冲突对象；代理转义回归改为在**最终原始字节**里
注入真实单反斜杠转义并断言精确语法诊断。产品与测试在 Release 下编译 **0 警告 0 错误**；沙箱内私有探针直接驱动
真实产品程序集，复算出内外集合都 ≤ cap。标准 `dotnet test` 与 `dotnet format` 在本沙箱仍无法运行，因此交付状态
**`needs_validation`**，两个协调提交标记为 `true`，本轮**未创建任何提交**（`working_tree_clean=false`）。

## 上轮已真实通过、本轮**未改写**的部分

协调第四次验收确认下列路径真实通过，本轮**一字未改**：固定 `Season 01` 合同、目标可信根初始/读取中变普通文件、
原生 JSON 代理解码、内部 Stamp、mtime 复核。卡 4 也把**唯一允许改动的生产文件**限定为
`Contracts/MediaPackageContracts.cs`，本轮严格遵守。

## 本轮实际改动（11 个文件：5 代码/测试 + 6 文档，白名单仍是 31 个文件、未新增文件）

| 层 | 文件 | 本轮作用 |
| --- | --- | --- |
| Contracts | `MediaPackageContracts.cs` | **R1（唯一生产改动）**：`Record` 改为先 `recorded.Contains` 查重 → 重复直接 false 且不截断 → 满则只置 `IsTruncated`、**不写入任何内部集合** → 有容量才 `recorded.Add` + `issues.Add`。类注释同步改为真实不变量 |
| Tests | `MediaPackageFailureTests.cs` | **R1 回归**新增 `IssueSinkKeepsEveryInternalStoreBoundedAtTheCap`（只读反射数内部集合）；**R4 回归**新增 `TargetRootReplacedByAFileAfterTheFirstHashIsRefused`（真实哈希器读完第一文件后替换根） |
| Tests | `MediaPackageSandbox.cs` | **R4**：新增 `ComposeWithHasher`，让测试通过**既有端口**包装真实哈希器，不改产品端口与 Inspector |
| Tests | `MediaPackageBoundaryTests.cs` | **R2**：重复路径测试改用 `JsonNode` 数组操作，保留原 `poster.png` 并**追加**完整 `Poster.PNG`；调用 reader 前先重解析断言两条都在、数组长度只多一条 |
| Tests | `MediaPackageContractTests.cs` | **R3**：新增 `MutateWithRawEscape`（先序列化、再在最终字节里替换唯一 ASCII 占位符），转义回归断言真实字节形状 + `invalid_manifest@surrogate_escape`；合法对/UTF-8/字面反斜杠为三个真实不同字节样本 |

`[TestMethod]` 由 126 增至 **128**（本卡 68 → **70**），`[Ignore]` 仍为 0，
**没有任何测试或断言被删除、skip 或放宽**；`MediaPackageFailureTests.cs` 中「初始根是文件」的拒绝断言
**原样保留**。

## 关键设计决定

1. **cap 是「诊断被丢弃」的标记**：重复对先在去重集合里命中，所以既不计入 cap 也不置 `IsTruncated`；但
   「命中」必须用**纯查询**完成。上一版用 `recorded.Add` 做查询，副作用把被拒的键留在了集合里，
   于是公开报告有界而内存无界——这正是本轮修的唯一生产缺陷。
2. **有界必须验内部**：只断言 `Issues.Count` 不能证明内存有界，所以测试与探针都用**只读反射**数
   `issues` / `recorded` 两个私有集合，不新增公开观测接口（卡 4 明确禁止）。
3. **测试夹具不能靠字符串拼接**：把对象拼进属性列表得到的是非法 JSON，reader 会以 `invalid_manifest`
   拒绝，于是「重复路径被拒」这个断言可能在证明错误的东西。改为在**数组**上追加，并在调用前重解析自证。
4. **转义必须在最终字节里成立**：把文字 `\uD800` 交给 `JsonNode` 再序列化会被二次转义成 `5c 5c 75…`，
   那是普通字符串，不是代理转义。因此先序列化、再替换占位符，并**先断言字节形状**再断言诊断。
5. **精确诊断而非任意码**：解码失败必须是 `invalid_manifest` + `surrogate_escape`，字段级拒绝
   （`invalid_identity` / `invalid_path`）不能冒充语法守卫；合法对与 UTF-8 写法必须给出**同一**字段语义诊断。
6. **两种摘要分离**：请求 `expected_digest` 是收到字节的原始 SHA-256（转义改动后按最终字节重算）；
   夹具/清单锁定摘要用 LF 归一化。
7. **报告不可授权**：`grants_file_operation` 恒为 `false`；`unverified` 按冻结顺序完整回填 7 项。

## 证据（沙箱内实际执行，可复核）

| 证据 | 结果 | 位置 |
| --- | --- | --- |
| 产品编译 Release | 0 警告 0 错误 | `.runtime/dsh-delivery/logs/build-r4-core-1.txt` |
| 测试编译 Release | 0 警告 0 错误 | `.runtime/dsh-delivery/logs/build-r4-tests-1.txt` |
| 真实程序集探针（55 行记录） | 内外集合均 ≤ cap；本轮每项回归成立 | `.runtime/dsh-delivery/logs/probe-run-44.txt` |
| 夹具不变性 | 6 个夹具 LF 归一化后与 `HEAD` 逐字节相同；5 个钉值 5/5 相符 | `.runtime/dsh-delivery/source-snapshot.json` |

## 探针本轮结论（真实程序集，非测试通过）

- **R1**：cap=1 时同一 `(code, location)` 记录两次 → 首次 true、重复 false、`IsTruncated=false`，
  内部 `issues=1`、`recorded=1`；再来一个**新的不同**对 → false 且 `IsTruncated=true`，内部仍各 1；
  10000 个不同 location 后公开 1 条、**内部 `issues=1`、`recorded=1`、`codes=1`**。
- **R4**：目标根初始为普通文件 → `Rejected` / `unsafe_path` / `root_is_file=true` / 0 文件 0 字节 /
  `hasher_calls=0` / **不报 `target_exists`** / 无绝对路径；首文件 hash 后替换为普通文件 →
  `unsafe_path@target`、4 次 hash 后拒绝、**不报 `target_exists`**；根被删除仍 `target_unavailable`。
- **既有行**：两个正例与 17 个冻结反例、边界拼写、原始摘要规则、只读清单、10 个真实端到端预检全部继续成立；
  55 行记录中**无任何抛异常、无任何回显原文**。

## 未执行（`needs_validation`）

- `dotnet test`：testhost 无法在本沙箱启动（本轮窄过滤器再试一次，仍 90 秒连接超时，未重试），
  **70 个本卡测试从未运行，不得记为通过**。协调第四次验收给出的是**上一候选**的真实结果
  （132 项 129 过 1 失败 2 链接条件 skip），本轮改动尚未被任何测试宿主执行。
- `dotnet format --verify-no-changes`：加载工作区即被命名管道权限拒绝，未再重跑、未提权、未重启。
  本轮格式证据仅为静态自查；**静态自查并非全绿**：19 个 C# 文件中有 7 个报出超过 120 列的行（均为既有长行），
  其余 LF/末尾换行/行尾空白/制表符/缩进检查通过。
- 架构/数据库/仓库 Python 门禁：本沙箱拒绝临时目录写入与清理。
- `.runtime/probe/escape` 临时探针：两次无输出超时后按协调指令**停止**，其失败**不是**产品结论；
  静态读取还发现它把 `examples.json` 根数组误当对象成员，故不据此对产品/夹具下任何判断。
- **本地提交未创建**：worktree 的 git 元数据在会话可写工作区之外，按协调指令未重试 Git 写入。

## 风险

1. **行为验证缺口（最高）**：本轮正确性是「Release 编译 + 真实程序集探针 + 代码审查」三重证据，
   没有执行过标准测试套件；协调必须在固定候选上复验。
2. `dotnet format` 的真实结论仍缺，本轮未新增格式违规证据，但也没有工具级确认。
3. 内部有界性由**反射**验证（探针与测试各一处），因为卡 4 禁止新增公开观测接口；若私有集合改名，
   两处必须同步更新。
4. 静态重解析点拒绝 + 读前读后戳记 + 最终目标与父根复核收窄但不关闭恶意并发目录替换（TOCTOU）；
   真实 NAS 语义与断电持久性未验证（`production_path_races`）。
5. `MediaPackageShapePolicy.cs` 与 `IsolatedMediaPackageInspector.cs` 超过 400 行软性指引；
   **未见协调针对这两个文件的专门豁免**，此处如实记录为未获豁免的行数偏离。

## 唯一快照入口

`.runtime/dsh-delivery/source-snapshot.json` 是候选字节的**唯一权威记录**：真实 `HEAD`/`base` 加 31 条
原字节 SHA-256 与长度。`delivery.json` 的 `source_snapshot` 字段是指向该文件的**字符串路径**，
**不再内嵌任何 hash 数组**——上一轮正是内嵌副本残留了 15 条旧 hash，而独立快照才是正确的。

## 建议合并顺序

本卡为只读预检，不依赖其他在途任务，可独立合并。合并前必须由协调方：
以 `.runtime/dsh-delivery/source-snapshot.json` 核快照并固定新候选 → 实跑
`dotnet restore --locked-mode` / `build` / `test` / `format --verify-no-changes`
→ 通过后合入 `services/core-server` 与 `tests/dotnet` 的 OperationTrash 范围。TS-098 与其他任务不受影响。

## 无后台遗留

本卡所有工具调用与后台写入均已结束；无运行中的后台任务、无遗留 dotnet/MSBuild 进程。
