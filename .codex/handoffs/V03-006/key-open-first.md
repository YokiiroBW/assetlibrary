# 公开开键观察首轮：成功参考通过，额外触发前提前结束

本周期完成了注册成功状态的normal/cancel参考，并在真实Explorer中验证KernelBase入口与SHCORE实际IAT映射。真实采集在ready后5.609秒触发原异常保护，早于本窗口获得工具返回并执行额外通知；因此没有首次ThisPC导航或Browse，0匹配不是开键失败或无历史读取的证据。已清理本周期，不盲重跑或提高预算。

## 注册成功参考

冻结manifest `268B29E575878D3C711AAFA0A4775AC59A710EF064F5A74595213B4D4A9672F4`的26件在启动前核SHA；observer `73A3807982D087F7E67E1789D85A02C2F7E588B908BC6FAAD8C0201DFF0EBF81`，reference target `5289C3FAA3F2E682CCF9E0252ADD12AD971599252B6D04E367AC6661ED73700B`。原F298/323四根guard于16:01:51.5185899Z登记，期限16:11:51.5185899Z、双通知成功；reader20字段全匹配。

| 参考模式 | 自有目标PID | callId | ulOptions / samDesired | LSTATUS | 成功/读取输出/非空 | reference自行close |
| --- | --- | --- | --- | --- | --- | --- |
| normal | 44052 | 1 | 0 / 1 | 0 | true / true / true | true |
| cancel | 45032 | 1 | 0 / 1 | 0 | true / true / true | true |

两种参考均exit0/passed，逐call配对与句柄语义另行验证；各1返回+1入口断点清零、Detach、参考心跳/无debugger及协作退出通过。cancel_requested S_OK，exitInterruptObserved原false保留。此前未登记status2参考属于候选准备原件，本窗口没有重复。已通过且未变化的成功参考不需因这次真实触发未完成而重跑。

## 最小采集包装与真实前置

最终按root确定的简化方式执行：collector只启动observer、持续排空日志、保存ready/错误/完成，不自动通知、导航或Browse。外部由唯一GUI操作者按assert-live→原notify-only→首次ThisPC/原生正控→一次Browse顺序执行；旧自动状态机草稿不是执行版本。

实际collector源SHA `682FF61C8339F90A9FD5D766F8F70BEB0E6F9318DDFCA8665B8E19CE9115F0FB`，assert-live `2A7641AF35B934AA561BB36F72DCF507E793A99A915094C55E9A43BB18092920`，只读clock `F4797CC6BBE57C6DA6C168E3F3B2C993F6BC288DDD5062FAD3C7DFE8537E1FA6`，view wrapper `B0577834A4E6BEB96FD00633408F87AACE755CEA0CC156542E0C384C26B8991B`。原notify包装8B7/D0FA、原guard及旧capture均未修改。

参考通过后fresh Active/Default及原周期剩余时间符合条件。保留用户多标签Explorer窗口2229572和其他应用，新专属SDK窗口3804030/PID43256、creation134335297796118136，SDK原生正控17项通过；没有先进入ThisPC/F5。

真实初始准入、KernelBase文件/入口、SHCORE文件/PE均成功，`real_shcore_iat_matches_fixed_public_entry`实际S_OK，随后安装单RegOpen入口断点并ready（tick295283531）。这证明此时检查到的SHCORE IAT项映射一致，不证明本周期已执行其目标开键调用，也不替代目标HKCR视图证据。

## 提前结束与未执行触发

tick295288953开始出现E06D7363首机会异常，留下16条详细异常记录后原callback health保护在295289140结束采集，距ready5.609秒。详细日志有截断上限，不能据16条日志精确统计总异常数或认定这些真实异常良性。最终matchedEntries=0、matchedReturns=0、nonMatchingDiscarded=0、finiteWaitTimeouts=50、passed=false。保留原上下文查询/恢复错误，随后自有暂停身份、唯一入口断点删除、剩余0、Detach、同creation目标存活无debugger均确认（final tick295289156）。

采集作为同步工具命令执行，原始工具返回块8d1d5a于6.7430622秒后返回exit1，没有running session；观察器已结束后本窗口才得到该返回。这个顺序来自工具元数据和采集日志，[顺序说明](key-open-first/tool-order-note.json)明确是派生记录，不是补造的原始父进程日志。没有单独记录collector父进程PID/creation。

因观察已经结束，本窗口没有发送ready后的额外双通知、没有运行assert-live动作门、没有首次ThisPC正控或Browse；对应marker/输出均不存在。原guard在登记和清理时的双通知照常发生，与未执行的“ready后额外通知”区分。零匹配和短暂ready不构成已完成触发实验，不能继续使用旧ready或旧PID补动作。

## 清理与后续边界

退出后仍在有效登记期间再次reader20/20匹配；SDK窗口保持17项，确认身份和Detach后只关闭该专属窗口。guard正常exit0，16:05:35.0180092Z双通知/8根absent，最终reader20项missing-key；用户2229572及其他应用保留。无原用户Explorer重启、无debugger硬杀、无盲重试。

下一步由root依据这次早停与调度顺序裁决最小修正；本轮不自行放宽异常保护、增加观察点或另开周期。正参考、真实映射验证和未执行触发三类结果分开计，G1仍未关闭。

[证据索引](key-open-first/evidence.json)包含60份来源/派生材料，5份账号/profile相关JSON为明确脱敏副本，并映射runtime原始SHA与副本SHA。实际26件候选（含二进制）在本周期runtime的candidate-frozen原样保留，版本库仅收源码/准备结果/定位原件与manifest。wrapper的准备README/static-review记录保留其执行前时点；最新执行状态以本报告和native日志为准。原始成功/失败参考和早停原件未覆盖，没有产品代码、系统策略、注册字段、期限或权限模型变化。
