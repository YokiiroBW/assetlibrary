# Windows Explorer 精确登记键打开：两次 short 取证交接

这两次执行未取得 `RegOpenKeyExW(HKCR, CLSID\{BA16CE0E-728C-4FC9-98E5-D0B35B384597}\ShellFolder)` 的匹配调用或返回状态。第一次在附加前因计划格式错误被拒绝；纠正后的第二次成功附加并通知，但因原定其他首次异常阈值提前停止。两次登记均已清理，自有窗口均关闭；不构成产品功能或发布门禁通过证据。

## 阶段与结果

| 项目 | 第一次 short | 第二次 short-fixed |
|---|---|---|
| Plan | `0c0f3bcdf6e04aa082cf2bf618d33ee5`；18 行 / 328 B，格式无效 | `641fd1d613be423cb5454827bd939fdc`；11 行 / 321 B，UTF-8 无 BOM，写后回读一致 |
| 新窗口 / PID | 3147082 / 9520 | 656266 / 7392 |
| 精确 creation FILETIME | `134335982471480875` | `134335987993635186` |
| ownership epoch FILETIME | `134335982411020262` | `134335987989402933` |
| SDK 正控 | 原生实际视图 17 项 | 原生实际视图 17 项 |
| Observer | `planned_setup_failed`，exit 1，未 attach / 未 ready | attach / ready 成功，后 `planned_capture_failed`，exit 1 |
| ready 后通知 | 未执行 | 原 STA UPDATEDIR + ASSOCCHANGED 一次，均返回 |
| ThisPC / Browse | 两者均未执行 | 首次 ThisPC 导航已操作并读到此电脑；Browse 门禁拒绝，未 dispatch |
| 精确键 entry / return | 0 / 0；没有观察窗口 | 0 / 0；非匹配丢弃 3896 |
| 清理 | guard 8 根 absent，独立 reader 20 missing，双通知返回 | 断点 0 / detach / alive / 无 debugger；guard 8 根 absent，reader 20 missing，双通知返回 |

第一次的直接原因是执行层 PowerShell 数组拼接优先级错误，`id=`、`pid=` 等字段和值被分成额外行。原错误计划、失败输出与 `cleanupVerified=false`、`targetAliveAfterDetach=false` 等原始默认值均保留；这个未附加分支的默认值不能解释为目标已死亡。独立 [refusal-health.json](first/refusal-health.json) 证明当时目标同 creation、alive、无 debugger。

纠正只引入本次 Python plan 生成器，以明确 11 项字符串列表和换行拼接生成字节；校验顺序、单值、ID/PID/FILETIME/HWND、120 秒 plan 有效期、60 秒观察时长、ownership 不超过 600 秒、无 BOM 与 2048 B 限制，独占写入后回读比对。固定样例 320 B / 11 行通过，10 个负例被拒绝，纯检查未写计划、启动进程或操作登记。生成器源码经协调方审查后才执行第二周期。旧错误 plan 与窗口身份未复用，没有第三周期。

## 第二次提前停止的准确含义

ready tick 为 1547437。预算停止 tick 为 1573250，相隔 25,813 ms；最终结果 tick 为 1574234，相隔 26,797 ms，未完成计划的 60 秒。

| 时点 | 总数 | C++ 首次异常 | 其他首次异常 | 二次异常 | 详细日志 |
|---|---:|---:|---:|---:|---:|
| 触发停止 | 81 | 64 | 17 | 0 | 16 |
| 清理结束 | 177 | 132 | 45 | 0 | 16 |

停止原因为 `other_first_chance_limit`，触发码 `0x40080201`。总目标异常停止阈值仍为 256，其他首次异常阈值仍为 16；阈值是停止条件，阈值触发后至清理结束继续计数，所以最终数高于触发数。没有扩大上限、吞异常或把超限记为通过。SDK 中此码为 `EXCEPTION_RO_ORIGINATEERROR`；实际 payload HRESULT、消息及来源栈未记录，不能据此确定具体错误或调用方，见 [SDK 对照记录](../winrt-error-notification.json)。

原生 ThisPC 读回为 `{20D04FE0-3AEA-1069-A2D8-08002B30309D}`、22 B PIDL、3 项；自有窗口树显示既有本地卷、NAS 资源与映射资源，未见样例标签。当前 3 项不能套用历史 2 项基线。完整 UI 树、存储名称和 SDK 窗口内容不入库，仅保留明确脱敏的最小投影。

## 时间与覆盖范围

下表统一为 UTC，显示到毫秒；原始 JSON 保留原偏移与更高精度，进程身份使用精确 FILETIME。

| 事件 | 第一次 | 第二次 |
|---|---|---|
| ownership epoch | 2026-09-11 11:04:01.102 | 2026-09-11 11:13:18.940 |
| Explorer 创建 | 11:04:07.148 | 11:13:19.364 |
| 登记 | 11:04:55.194 | 11:14:12.525 |
| capture ready | 无 | 11:14:38.681（collector 记录） |
| ready 后通知返回 | 无 | 11:14:39.500 |
| capture 结果 | 11:05:31.454 | 11:15:05.478 |
| native ThisPC 读回开始 | 无 | 11:15:05.493 |
| guard 清理 | 11:06:27.275 | 11:15:52.634 |

第二次 native ThisPC 读回开始比 capture 结果晚约 15.05 ms。CUA 的精确导航提交 / 取景 UTC 没有持久化，因此只能确认执行顺序及事后实际视图，不能宣称观察完整覆盖了 ThisPC 导航。Browse 门禁检查时 capture 已结束；没有 `browse-dispatched`，也没有单独的失败 `gate-browse.json` 回执。拒绝事实来自当时执行输出、执行汇总与未 dispatch 的互证，不能补造回执。

两次登记均发生在新进程创建之后，不是 registration-before-process 证据。20 字段读回来自独立进程，其 HKCR 结果不代表 Explorer 进程缓存视图。通知成功不证明缓存刷新。SHCORE IAT 与固定入口一致只证明入口校验，不能给每个调用归因。零匹配不证明键不存在，也没有目标 API 的 LSTATUS、成功 PHKEY 读回或关闭证据。

## 清理及身份保留

每个周期登记有效时的两次独立读回均为 11 官方字段 + 4 Owner + 5 外部 HKCR 字段匹配；停止 guard 后 8 根 absent、20 字段 missing、UPDATEDIR 与 ASSOCCHANGED 均返回。第二次保留原始断点清零、detach、同 creation alive 与无 debugger 的证据。

两次最后的 fresh CUA 核对均确认自有窗口消失；第二次同时保留 native 窗口缺席证据。原 Explorer 进程在最后快照中仍驻留，未强杀。桌面 PID 6212 保留，没有操作原用户窗口，没有再次复用旧 PID、HWND 或 epoch。首次窗口关闭证据以第二次 fresh CUA 核对为准；初次关闭动作后的瞬时清单尚包含窗口。

本归档阶段没有 GUI、登记、新观察周期、编译或提交；不修改外层 result/tests/state，由协调方验收并更新。

## 文件与可追溯性

- `first/`、`fixed/`：必要原始计划、native 读回、observer、失败、通知、清理和身份记录。
- `preparation/`：本次生成器的明确脱敏源码副本、固定样例检查、必要脚本差异与哈希；不重复复制既有调试器源码。
- [evidence.json](evidence.json)：每个原件 SHA256 → 副本 SHA256、类型、变换说明、排除项、限制及冻结工具引用。
- `original_copy` 逐字节保留；`redacted_copy_not_original` 明确不是原件。含账号/profile/SID 的 JSON 以带标记的 `payload` 封装；UI 和 session 仅保留所需字段。生成器脱敏文本含路径占位符，不作为可直接执行的源文件。
- 两份原始 `capture.stderr.txt` 不是有效 UTF-8；归档仅保存明确标记的脱敏展示副本，使用替换解码并保留原件 SHA256。原错误字节流不被伪称为规范 UTF-8 或逐字节原件，结构化失败 JSON 与 observer JSONL 同时保留。
- `WINDOWS_OWNER_WORKTREE` 占位符指既有 Windows owner 工作目录，个人 profile 前缀省略。原件仍在原 runtime 中，未修改；副本哈希不冒充原件哈希。
- 冻结观察器 `75F1…`、参考工具与原检查来源见 [已有工具归档](../clsid-key-open-debug-v2/manifest.json)。本次未重跑已通过的参考测试。

失败和证据缺口保持原样：两次 capture 均 `passed=false`；第二次 key-call 问题仍未解决，不构成 Explorer 内嵌能力或里程碑完成证据。
