# V2参考通过后的真实Explorer缓存消费对照

本次取得了新的有效区别：同一原样注册周期内，独立normal/cancel参考在固定消费点读到flags最低位有效、attributes=A0000020；真实Explorer四次同目标消费点快照则flags/attributes/callFor/restricted全部为0。外部HKCU/HKCR字段前后仍20/20正确，实际入口仍无关联应用。这说明差异已经能在选定消费点观察到，尚不能据此认定哪次注册读取失败或具体根因。

## 新周期及参考准入

上一9192版本normal的单snapshot断言失败完整保留于e169e51，本轮使用独立新输出目录和V2，不覆盖旧日志。V2仅将参考完成条件改为cacheSnapshots==matched及独立文件名/目录，范围、预算、清理保持；root/独立源码复核和38项纯检查是准备证据，本窗口不重复运行。

冻结manifest SHA `E8C2896E092BD81DC397C4CB0B28B6B51E483F3C79981601986B51C7D707CC73`，14件逐项核SHA；observer `952886D8F483E81345626F5866887C015A81915432BB7FABF5F2293C576792BC`，reference target `12A6774021B15059C4A1B92F7C22A1AAE079F1BAC9B8B83793D2C925C923CAE4`。原F298/323CD843四根guard注册14:55:30.9876308Z，原期限15:05:30.9876308Z，双通知成功，未延长。

固定reader before为11官方+4Owner+5外部HKCR共20/20匹配，PIDL预检S_OK/22B。normal于14:55:51.840–14:55:52.210Z、cancel于14:55:52.216–14:55:54.101Z各exit0、passed=true；分别创建参考进程38252和36012，均只调试自己新建的参考目标。额外语义核对逐个matched call要求恰好一个snapshot和return、outerCallId/requestMask一致，结果如下；没有仅凭observer passed放行。

| 模式 | outerCallId | 输入mask | flags | attributes | callFor | restricted | 外层HRESULT/输出 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| normal | 1 | 68180000 | 00000001 | A0000020 | 00000000 | 00000000 | 00000000 / 20000000 |
| normal | 2 | 20400000 | 00000001 | A0000020 | 00000000 | 00000000 | 00000000 / 20000000 |
| cancel | 1 | 68180000 | 00000001 | A0000020 | 00000000 | 00000000 | 00000000 / 20000000 |
| cancel | 2 | 20400000 | 00000001 | A0000020 | 00000000 | 00000000 | 00000000 / 20000000 |

两mode各2返回+2入口断点清零、Detach、参考心跳/无debugger和目标协作退出确认；cancel_requested为S_OK，exitInterruptObserved原值false保留。两次参考不再重跑。

## 唯一真实Explorer对照

参考语义与清理全部通过后，剩余519.884秒，fresh WTS Active/Default输入桌面成立。保留用户“下载”窗口2229572、桌面32044及其他应用，新建专属窗口227804804/PID15224、creation134335258295631551，创建时间晚于本轮ownership epoch，非桌面进程。观察器再次执行自己的精确目标准入；未使用冷启动工具停止任何Explorer。SDK17项和ThisPC22B/2项正控通过，F5仍无官方示例。

新11行plan、ready tick291380765之后，只联动一次官方Browse。原42B PIDL/flags1，10秒控制器到期，无browse.return HRESULT；实际模态68881476经原生owner确认属于15224，文本仍为无关联应用。

| real outerCallId | 输入mask | flags | attributes | callFor | restricted | 外层HRESULT/输出 |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | 20000000 | 00000000 | 00000000 | 00000000 | 00000000 | 00000000 / 00000000 |
| 2 | 40418000 | 00000000 | 00000000 | 00000000 | 00000000 | 00000000 / 00000000 |
| 3 | 40000000 | 00000000 | 00000000 | 00000000 | 00000000 | 00000000 / 00000000 |
| 4 | 2044007F | 00000000 | 00000000 | 00000000 | 00000000 | 00000000 | 00000000 / 00000026 |

四个snapshot各与一个entry/return配对且输出有效，首个消费tick291381203，另外三个291381218；不是零命中情形。call1/4请求FOLDER，另两次没有请求该位。该状态是固定1194C3消费点的私有结构四DWORD读回，不能把flags=0直接当成某个注册API的返回码、把attributes=0当成实际注册值0，或从中推断HKCU已缺失。

原异常保护在tick291440203触发，距ready为59.438秒；最终tick291440218，passed=false，非完整60秒成功。保留16次首机会异常与初始running上下文/恢复查询错误；后续自有暂停身份、4返回+2入口断点删除、剩余0、Detach、同creation目标存活无debugger均确认。未提高预算或重跑实机。

## 读回、清理与证据范围

脱离后关闭已记录模态，实际视图仍ThisPC类20D04…/22B/2项，官方match exit2、ThisPC match exit0，样例DLL未加载。有效注册窗口after-real字段再次20/20，包括实际HKCU及外部HKCR Attributes均A0000020；两次字段查询不冒充Explorer内部注册读取。

确认身份后仅关闭专属窗口，stop guard正常exit0，15:02:07.8723266Z双通知/8根absent；最终reader再次20项missing-key。新鲜CUA确认专属窗/模态消失，用户下载窗2229572仍在，DSH Desk、Chrome、Codex保留。没有重启或停止原用户Explorer。`final-cleanup.json`的HardKillUsed=false指未硬杀调试器；原10秒超时路径确实结束了自有Browse控制器，不能将该字段扩读为所有辅助进程从未被终止。

[证据索引](cache-consumption-v2/evidence.json)保存59份来源/派生文件及[8行完整对照矩阵](cache-consumption-v2/comparison-matrix.json)，7份身份/profile相关JSON为显式脱敏副本，分别记录runtime原件与副本SHA。14件执行候选（含二进制）另在本周期runtime的candidate-frozen原样保留；版本库保留源码、差异、准备验证及manifest，不提交二进制。候选README“仅准备”属于其冻结时点，不覆盖本轮执行日志。

本轮复用固定字段reader、原guard、同一公共外层/单消费点及ready→Browse包装，只改候选目录/文件名/hash与输出目录；没有产品代码、注册字段、系统策略、权限模型或接口修改。原首轮失败继续保留，G1仍未关闭。后续由主协调根据已观察的参考与真实消费状态差异选择下一最小取证点，不在本轮追加注册加载点或盲采。
