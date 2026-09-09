# 四轮ETW前置准备与撤销

状态partial。用户已批准一次有界管理员ETW观察，桌面测试授权持续。V03-005负责采集器、有效期计划、UAC、会话与guardian；本任务仅负责独立测试窗口、原动态proof注册、等待ready后的GUI入口和自有清理。四轮均未收到允许导航的稳定ready，**实际GUI入口提交次数为0**，不计G1复现、通过或失败。

原动态DLL SHA256保持0174DB9B1B4CCD4925D3A28470930FA6FAEBD4070348CC374A7CB87313E2FD15。每轮使用新的自有runtime副本、原owner保护HKCU注册脚本、600秒guard及相同sandbox空FolderName.{CLSID}目录。未改DLL、接口、注册语义、生产源或系统策略。

第一轮新窗口发现有延迟：Ctrl+N产生1376478，随后受支持launch产生4196070；按前后集合确认均为本任务新增，并关闭额外1376478。唯一保留测试窗口4196070的精确SDK目录标题与进程主窗口读回对应PID8364/Session2；原窗口1247028属于17944。注册04:21:39.9830571Z，两键和64位HKCR Inproc路径已读回，入口未输入。

主协调报告首轮Start/Open/三个Enable及注册正控成功，但PID归属异常事件触发scope guard立即结束；其独立status确认会话absent且collector/guardian退出。本任务未自行启动ETW，也未把该报告当作自身独立验证。随后stop使注册guard正常exit0，finally04:31:26.3561724Z两键false；空目录经路径/Directory/无reparse/零子项核验后非递归删除。关闭4196070时首次工具报告检测到用户输入，按要求重新get_window_state确认仍为同一自有SDK目录窗口后关闭，最终只剩原1247028及Codex。

第二轮仅一次受支持launch创建唯一2754052，精确SDK标题/进程主窗口读回对应新PID19288，创建时间04:37:50.9158085Z。注册04:38:10.085729Z；ready.json中解析后显示等价+08偏移，原register.json字节保留。第二轮截图与文字不一致，未作为现场或成功证据；独立UIA只读确认SDK目录、17项，与进程标题一致。入口未输入。

主协调随后报告Windows UAC启动返回“操作已被用户取消”，没有启动成功、collector JSONL、guardian或ready信号。按协调指令立即停止自动重试；stop使guard正常exit0，finally04:46:37.5792186Z两键false。相同空目录核验后删除，2754052经fresh UI确认后关闭，最终窗口列表保留原1247028及Codex。现场无残留注册或自有窗口，EntrySubmitted=false。

准备、UTC、PID/窗口对应、注册与清理原始记录及hash见[证据索引](explorer-etw-preparations/evidence.json)。复用现有guard/注册和computer-use接口，无新框架、依赖、共享契约或资产I/O；固定规模准备不构成性能/长期稳定性结论。无源码改动，不重跑原有loader、root-bind、图片矩阵或全仓库业务测试。

第二轮结束时暂停自动重试，等待用户准备处理Windows系统提示。用户之后明确回复继续，第三轮在同一授权范围重新准备，未再次询问权限。

第三轮一次launch创建唯一2689898，fresh UIA显示SDK目录17项，原生进程主窗口对应PID22932/Session2，创建11:47:49.6084838Z。注册11:48:12.7233043Z，guard600秒；仅提议测试宿主22932、原窗口代理17944和Desktop候选6284，不把全部旧idle Explorer纳入计划。入口未输入。

主协调报告第三轮Kernel-Registry provider的event4/version0/headerPid10716不在计划22932/17944/6284/collector28708集合内，scope guard未保留payload并停止；原生Stop/Query4201及guardian均成功。该来源修正了此前关于image事件的猜测，本任务仅按主协调报告记录，不自行读取事件负载或宣布根因。遵照清理指令，stop使guard正常exit0，finally11:51:58.9926312Z两键false；空目录核验后删除，自有2689898经fresh UI确认关闭，最终仅原1247028及Codex。

第三轮之后主协调停止该Kernel配置，未放宽到全局消费后筛选。其后准备用户态候选，仅保留UserLoader、COM和任务私有控制事件，仍有原PID范围、60秒、guardian和越界即停止；工具代码审查/构建验证由主协调负责，本任务未修改采集器或默认生产组件。

第四轮一次launch创建唯一1248182，fresh UIA确认SDK目录17项，原生主窗口对应PID7272/Session2，创建12:19:08.4762088Z。原动态DLLhash匹配，注册12:19:26.2408631Z，guard600秒；提议相关PID仅7272/17944/6284。主协调报告此次用户态工具的UAC启动也返回“操作已被用户取消”，没有collector JSONL或ready；入口未输入。

按协调指令立即清理，stop使guard正常exit0，finally12:25:04.5484541Z两键false；空目录经完整路径、Directory、非reparse及零子项核验后非递归删除。1248182经fresh UI确认关闭，最终list仅原1247028及Codex。该轮准备文件位于自有.runtime/explorer-live/20260909-user-etw-entry，归档以attempt4命名；采集器启动结果仍明确来自主协调报告。

当前四轮均已清理且没有GUI入口提交。主协调正在向用户核对.NET Host系统提示是否可见，这是状态澄清而非重复授权；不自动重试提示。后续必须重新建立窗口/进程、注册和有效期计划，不能沿用已撤销的ID或ready状态；已有授权持续。
