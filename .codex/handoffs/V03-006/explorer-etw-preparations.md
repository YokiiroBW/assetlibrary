# 两轮ETW前置准备与撤销

状态partial。用户已批准一次有界管理员ETW观察，桌面测试授权持续。V03-005负责采集器、有效期计划、UAC、会话与guardian；本任务仅负责独立测试窗口、原动态proof注册、等待ready后的GUI入口和自有清理。两轮均未收到允许导航的稳定ready，**实际GUI入口提交次数为0**，不计G1复现、通过或失败。

原动态DLL SHA256保持0174DB9B1B4CCD4925D3A28470930FA6FAEBD4070348CC374A7CB87313E2FD15。每轮使用新的自有runtime副本、原owner保护HKCU注册脚本、600秒guard及相同sandbox空FolderName.{CLSID}目录。未改DLL、接口、注册语义、生产源或系统策略。

第一轮新窗口发现有延迟：Ctrl+N产生1376478，随后受支持launch产生4196070；按前后集合确认均为本任务新增，并关闭额外1376478。唯一保留测试窗口4196070的精确SDK目录标题与进程主窗口读回对应PID8364/Session2；原窗口1247028属于17944。注册04:21:39.9830571Z，两键和64位HKCR Inproc路径已读回，入口未输入。

主协调报告首轮Start/Open/三个Enable及注册正控成功，但PID归属异常事件触发scope guard立即结束；其独立status确认会话absent且collector/guardian退出。本任务未自行启动ETW，也未把该报告当作自身独立验证。随后stop使注册guard正常exit0，finally04:31:26.3561724Z两键false；空目录经路径/Directory/无reparse/零子项核验后非递归删除。关闭4196070时首次工具报告检测到用户输入，按要求重新get_window_state确认仍为同一自有SDK目录窗口后关闭，最终只剩原1247028及Codex。

第二轮仅一次受支持launch创建唯一2754052，精确SDK标题/进程主窗口读回对应新PID19288，创建时间04:37:50.9158085Z。注册04:38:10.085729Z；ready.json中解析后显示等价+08偏移，原register.json字节保留。第二轮截图与文字不一致，未作为现场或成功证据；独立UIA只读确认SDK目录、17项，与进程标题一致。入口未输入。

主协调随后报告Windows UAC启动返回“操作已被用户取消”，没有启动成功、collector JSONL、guardian或ready信号。按协调指令立即停止自动重试；stop使guard正常exit0，finally04:46:37.5792186Z两键false。相同空目录核验后删除，2754052经fresh UI确认后关闭，最终窗口列表保留原1247028及Codex。现场无残留注册或自有窗口，EntrySubmitted=false。

准备、UTC、PID/窗口对应、注册与清理原始记录及hash见[证据索引](explorer-etw-preparations/evidence.json)。复用现有guard/注册和computer-use接口，无新框架、依赖、共享契约或资产I/O；固定规模准备不构成性能/长期稳定性结论。无源码改动，不重跑原有loader、root-bind、图片矩阵或全仓库业务测试。

下一步等待用户准备好处理Windows系统提示后由主协调安排；已有任务授权不需要重问，但本轮取消后不自动重试UAC。继续前须重新建立窗口/进程、注册和有效期计划，不能沿用两轮已清理的ID或ready状态。
