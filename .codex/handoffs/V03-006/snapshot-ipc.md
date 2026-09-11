# V03-006 — 只读快照 Shell IPC

2026-09-12，受 root 冻结契约/限定目录委派，仅实现 tests/windows-shell 与本任务交接。分支 codex/v03-006-windows-explorer-native-integration。最新代码提交见本交接包所在提交；合并只需本轮实现提交，f1d85a1/ca60d4a 是协调者已有契约的 cherry-pick。

## 交付

移除 test-only DLL 的固定示例内容与同步 proof-calls.log 文件写入。EnumObjects 读取真实本地 Host 快照，严格拒绝超限/损坏/身份失效；默认不修改资产。名称、类型、规范身份比较和解析均来自 PIDL，最多一页 100 条与一项下一页导航。状态行不可导航，F5 或重开刷新。规范名称携带有界私有 PIDL hex，fresh instance/多层解析无需页面缓存或 IPC；按名称/类型列排序，未知列拒绝。

SnapshotPipe 核服务端实际 TokenUser SID、session、进程存活与 PID/创建时间，持有进程句柄；客户端 SQOS 为 Identification。所有前台 I/O 共用 150ms 单调等待预算；超时请求取消，未完成资源及 DLL pin 由线程池完成回收，四名额（在途和滞留合计）外返回 Busy。读取声明帧后关闭，不等 EOF，不重试网络。

新 ExplorerSnapshotProbe 直接 LoadLibrary/类工厂/固定根，不注册或 GUI；Root→首库→首目录/下一页，默认总重试预算 8 秒、250ms 间隔，单 Shell 请求仍 150ms。命令、退出码与限制见 tests/windows-shell/README.md。构建目录 .runtime/explorer-snapshot/Release。旧注册路径准入与 owner cleanup 未改变；verify 只读页断言替换了固定样例库断言。

## 验证及实测边界

- MSVC /W4 /WX /permissive- /analyze /utf-8 全目标 Release 构建通过。
- CTest explorer_snapshot：三个来自协调者 wire-vectors-v1.json 的独立 literal frame；所有截短位置、未知 header/status/kind、零/重复 token、坏 UTF-16/控制字符、长度/分页/尾随数据、genuine empty。
- PIDL roundtrip/错误版本与长度；fresh-instance 单段和完整多层 canonical；非 hex、尾部空段、非目录中间项和 65 层拒绝；同名不同身份；类型列逆序正控和未知列拒绝。
- 真实 local pipe：无 Host、完整帧但不关闭服务端、错误 header、截断 body、85ms header + 85ms body 累计预算（实测约156ms，测试留调度容差230ms）、25ms取消、四在途/第五Busy、取消完成后名额归零。SQOS实际线程令牌为Identification；同用户同session服务端正控。
- 无注册 COM：库/物理目录/下一页、文件及链接不可进入、零写/拖放能力、六类状态、Host epoch改变后旧位置Expired；展示/属性/比较/解析之间服务端请求计数不增加；COM对象释放后DLL可卸载。
- 独立子进程实际调用公开 probe 命令，四阶段全部完成。无 Host 的 --once 返回 status2/exit2，是预期负控。
- 稳定性检查连续五轮 CTest 通过。先前失败来自 test mock 主动 Disconnect 丢弃未读缓冲、及下一请求早于恢复监听；修正为等待客户端关闭/监听就绪。保留原因，不把测试夹具竞态记作通过。
- 当前取消通常迅速完成；未强制模拟内核永不完成取消，也未执行其他真实用户/跨session拒绝实验。滞留内存/DLL回调及四名额上限有代码审查和真实取消测试，不能宣称覆盖所有内核时序。
- 本轮无注册/通知/GUI/Explorer重启、无真实 Core 或 .NET Host 联调；默认pipe已归还root。实际视图、源文件hash、断网/权限撤销和长期 G2..G4 由协调端与Host后续验收，不关闭发布门禁。

## 架构、兼容和合并

复用现有 DefView/COM、CMake/SDK、冻结本地快照契约与 owner 注册工具；没有新增语言、第三方依赖、业务用例、数据库或 HTTP。C++静态内部库只复用 wire/PIDL/pipe；测试独立编码器及mock不进入DLL。Host/Core是权限和物理归属事实来源，Shell展示旧PIDL不代表当前权限。无文件读写、网络、凭据、Provider、预览或写入接口，probe日志只含安全转义的样例展示名/种类/状态。

50万资产不改变Shell复杂度：每次O(101 log101)最坏去重/一页解析与有界名称存储，四个请求payload各≤65536；无全库扫描/索引。内部 PIDL/解析名格式为 v1，旧合成PIDL不复用，需关闭旧test-only视图后以新DLL重新打开；不宣称安装器或生产兼容迁移。

建议 root 先合入冻结契约和本轮Shell，再接Host稳定提交，运行真实HTTPS/Core互操作，最后独占原生路线的新DLL实际Explorer验收。技术债保留：生产安装/登录/生命周期/推送失效、Explorer G2/G3/G4及真实跨身份/长期稳定性证据。

最终仓库检查：`python -I -B scripts/verify_repository.py` 通过（交接/架构/契约/依赖、432 C#文件策略与35既有回归；Alpha保持blocked）；17项既有Shell契约通过；`test-registration-context.ps1` 的19策略+2接线+当前进程原生查询通过，修改后verify.ps1 AST通过；`git diff --cached --check`通过。未运行无关.NET整套或真实注册。
