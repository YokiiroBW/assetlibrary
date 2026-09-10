# 真实MyComputer候选Bind方法观测

本次取得了两对实际匹配的绑定入口/返回，不再只是未观察到通用COM入口。两次均针对与官方目标完全匹配的有限不透明PIDL，IID类别为other；返回分别为80070490与80004002，输出接口均null。还没有精确IID或绑定上下文内容，不能直接认定这是必需IShellFolder绑定失败或已定位根因。

使用根已审查的固定BindObserver SHA256 822FEE1961946116B7E6B8BABC5907F81E7C569B30D674AC2506CBF823037619，原F298/四根/双通知STA/600秒guard不变。工具只观察经系统文件SHA、PE及代码前缀校验的windows.storage候选Bind方法；本机RVA仅为诊断，不是产品实现策略。原v2三COM工具及其结果未改。

新自有1837638/PID22968，creationFiletime134334874764660908，所有权epoch在CUA创建前记录。SDK/ThisPC actualview正控和唯一身份通过，生成新11行official计划a8c0511fafe84975963c6a0e614f8665。实际reference_official_pidl、系统文件hash、目标候选身份与单断点安装均S_OK；该本地reference不是实际host对象身份证明。

ready tick253022218，包装记录04:19:34.434910Z后立即联动唯一Browse。Browse提交04:19:34.802Z/tick253022609，官方绝对子根42B、flags1，控制器前置ThisPC正确。目标内两对关联记录均在tick253022609：

| callId | IID类别 | pbc存在 | 实际HRESULT | 输出参数存在 / 返回接口null |
| --- | --- | --- | --- | --- |
| 1 | other | false | 80070490 | true / true |
| 2 | other | true | 80004002 | true / true |

未解释other IID，也未读绑定上下文对象。不能将这两个请求等同IShellFolder/IShellFolder2，不能据pbc存在就认定STR_SKIP_BINDING_CLSID。唯一Browse在实际“无关联应用”模态中达到10秒外限，没有browse.return HRESULT；本表是捕获的Bind返回，不是外部Browse返回。

随后原始日志出现16条first-chance E06D7363记录，目标自行处理，工具不吞异常；在tick253056281因callback health失败提前结束，未完成60秒。冻结源码在异常计数超过16时设置callbackFailed，日志未单列最终异常总数；保留事件和原逻辑，不把它扩大为目标崩溃或具体抛出源证明。两匹配/两返回关联完整，149个不匹配被丢弃。

清理初始running状态的pid查询8000FFFF、ownedPidMatch=false原样保留；后续ACTIVE暂停确认同一所持有进程/创建身份，return与entry两个断点移除S_OK、数量0、Detach S_OK。post-detach查询成功且无debugger，同一目标存活；最终cleanupVerified/targetAliveAfterDetach=true，passed=false不能写作清理失败。

确认完整脱离后才关闭自有模态527574。注册有效期内actualview仍20D04…/22B，不匹配官方42B；对ThisPC匹配读回成功/2项。观察器和目标模块快照未见样例DLL。stop/finally于04:24:59.6127282Z正常exit0，4CU/4LM检查全absent，UPDATEDIR和ASSOC清理通知均完成。自有1837638及模态已关闭，原1247028/Chrome8128618保留。

[原件索引](bind-debug-control/evidence.json)包含新plan、固定hash、包装stage识别修正、reference/系统候选校验结果、ready/唯一Browse、匹配callId、所有早停/清理事件、有效期view/模块与注册/UI清理。未增加上限或变更输入，也未运行下一变体；更细判别由根协调裁决，G1保持partial。
