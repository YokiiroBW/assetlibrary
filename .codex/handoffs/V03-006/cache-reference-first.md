# 缓存消费观察器首轮参考：断言失败，未进入Explorer

按主协调冻结方案执行一次原F298/323CD843四根600秒周期：实际字段核对和官方PIDL预检成功，normal参考随后失败。本窗口立即停止进入cancel/真实Explorer，完成有效字段再次读回与注册清理，没有用已观察到的正确数值绕过失败门禁。

## 执行与字段证据

注册UTC14:47:47.3689702Z、期限14:57:47.3689702Z，原双通知成功。候选manifest SHA `887898A854B755C0EA08931646F95D4E0DF97D90505133A873D70F6C0D1EE894`，15候选文件及6基线source在启动前逐项核SHA，observer `9192C06013F4D86272BC9F4BBCB03B9FE803C7DD6CE01785CA410AF0955EB79D`，reference target `081DE6E8DE0B061D0D96F2DD2811E8679302A67B5E5B844BFC8E528F170A1B60`。

独立reader的before与失败后有效窗口读回均20/20匹配：11官方字段逐项存在、类型及实际值正确，四Owner正确，五个外部HKCR合并字段也匹配。REG_DWORD Attributes实际规范值A0000020，空默认REG_SZ也确实存在；不再仅引用guard的常量摘要。HKCR仍只代表该reader视图，不是Explorer内部观测。撤销后第三次reader为20项missing-key/OpenStatus2。错误类型、已存在key中的missing-value和其他未构造边界仍不计实测。

`CacheAttributesReferenceTarget.exe --check-official-pidl`实际S_OK、parsed=true/22B、registrationAbsent=false，debuggerStarted=false。

## normal的实际嵌套结果与失败点

唯一normal只启动自有reference进程PID31728、creation134335252911295189；没有附加已有Explorer。ready tick290740109，随后产生两组嵌套公共属性调用，返回次序是2再1：

| outerCallId | 输入mask | flags | attributes | callFor | restricted | 外层HRESULT | 外层输出 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | 68180000 | 00000001 | A0000020 | 00000000 | 00000000 | 00000000 | 20000000 |
| 2 | 20400000 | 00000001 | A0000020 | 00000000 | 00000000 | 00000000 | 20000000 |

两组cache事件各自通过候选的GUID/栈/scope过滤，均与其outerCallId及输入mask一致；`flags & 0x1`为真（最低位bit0，非0x2）。这只是参考进程数据，不是实机Explorer的cache字段结果。

tick290748140触发`attributes_single_and_multi_item_control`与`synthetic_capture_failed`，最终normal exit1、passed=false、matchedEntries=2、matchedReturns=2、nonMatchingDiscarded=1。只读源审定位`Observer.cpp:378`的`AllSyntheticReturns`要求`cacheSnapshots==1`，与本轮实际两个snapshot不符；这解释当前参考断言为何不能通过，但本窗口没有自行放宽该条件或盲目重跑。原8秒参考期限和失败日志保留。

## 清理与未执行边界

初始running上下文查询错误原样保留；随后暂停自有身份、2返回及2入口断点删除、数量0、Detach成功，post-detach无debugger、心跳前进及`synthetic_target_cooperative_exit`均S_OK。未硬杀debugger，reference已协作退出。normal observer原生stderr为空、exit1；包装器在保存结果后抛出失败异常，该工具返回不混作observer stderr。

失败后再次20字段匹配，再stop guard；14:48:55.1818546Z cleanup成功、双通知成功，外部读回8根absent，guard正常exit0。cancel未执行、real plan未创建、GUI输入0、已有Explorer attach0、Explorer重启0。准备时Active/Default桌面成立；冷启动专用全局Eligible因原桌面的Xaml_WindowedPopupClass为false，但本轮没有执行停止工具或操作该弹窗，不把它作为参考子进程准入条件或偷偷放宽规则。

当前周期已经结束；等待主协调根据真实嵌套调用修复参考验收及完成新冻结版本。原observer自己passed不足以替代语义核对，反之数值符合预期也不把失败的参考记为通过。G1仍未关闭。

[证据索引](cache-reference-first/evidence.json)含33份来源/派生文件，其中5份含账号或profile信息的JSON为明确脱敏副本，分别记录原始runtime SHA与副本SHA。实际执行候选的manifest、15文件（含二进制）和6基线source原样另存本轮runtime的`candidate-frozen`；版本库保存候选源码/差异/准备原件及manifest，不提交二进制。候选README/validation中的“参考待执行”保留为其准备时点，最新结果以本报告和原始normal日志为准。

本轮复用已审field reader、原guard和冻结candidate，没有修改观察器、注册字段、时限、系统策略、权限或产品代码。只验证字段/PIDL、首次normal及清理；旧pure scope/Plan和旧实机控制未重跑。
