# 正确STA通知后的完整官方对照

此次官方对照在通知前置修复后仍未成功：有效注册和成功原MyComputer通知之后，ThisPC无样例项；唯一原生打开又出现“无关联应用”，10秒外限结束控制器，实际view仍ThisPC。这是与先前MTA通知未执行那轮分开的新结果，不覆盖或改写旧失败。

原F298A352…官方DLL、18份固定源、原根/菜单CLSID、A0000020、Apartment、菜单关联、DefaultIcon及Inproc完整路径均不变；仍仅批准的4个HKCU根加同一owner元数据。唯一修复是通知线程在启动前显式STA，并记录实际阶段/HRESULT/耗时。没有加ASSOCCHANGED、属性schema、信任标志或系统策略。

新guard每次通知返回不可变快照，CoInitialize/GetSpecialFolderLocation/通知实际调用与返回、是否join和耗时均记录；初次通知不成功即拒绝进入测试并finally清理。已存在根与创建竞态拒绝、允许树/值/类型验证、600秒deadline及逆序清理保持原样。

新鲜自有窗口14092968/PID17740，创建17:13:58.0531713Z，原窗口1247028保留。17:15:02有效ThisPC正控：唯一frame及PID/创建身份、类20D04FE0…、PIDL22B、canonical相等、2项。注册17:15:39.3627432Z，Notification为STA/type0、CoInitialize=S_FALSE、Folder=S_OK、PidlPresent/NotifyInvoked/NotifyReturned/Joined全true，2ms/Success=true。随后11个原始字段含值与类型逐项核对一致；8项HKCU/HKLM前置检查均不存在。

17:16:55.426Z F5后ThisPC仍2项，没有FolderView SDK Sample。按批准分支，在正确通知完成后的同一有效注册窗口内，唯一BrowseObject提交17:17:56.192Z：前置实际ThisPC、唯一身份、外部解析官方MyComputer子根42B PIDL、flags仅1。调用在真实自有“该文件没有与之关联的应用”模态中阻塞，外部10秒期限终止控制器；未收到browse.return HRESULT，不把超时写成函数返回失败码。

有效注册期内的实际view读回：根仍`{20D04FE0-3AEA-1069-A2D8-08002B30309D}`、当前PIDL22B，与官方目标42B不相等；针对官方目标的观察器按匹配保护退出2。改用已允许的ThisPC匹配只读回当前父view，canonical相等/ItemCount2，exit0。观察器全阶段没有加载样例DLL，独立目标进程模块快照也未见样例。原官方DLL没有Factory日志，不伪造日志或宣称从未尝试加载。

需保留一项时序细节：第一次关闭模态的CUA变量已不在会话作用域，动作未执行；上述两个原生读回发生时模态仍在。随后重新读取其实际窗口24119662和文字，按确定关闭。该差错没有被写成“关闭后读回”，清理后的UIA另单列；没有重复Browse。

stop/finally于17:19:53.7036861Z正常exit0，四HKCU及四HKLM读回均absent、CleanupErrors空；清理通知也STA真实成功。自有14092968及模态24119662已关闭，最终仅原1247028和Codex。未删除/重命名示例项、未修改资产或官方源。

[原件索引](official-sta-runtime-control/evidence.json)保留成功通知、原字段读回、唯一超时、实际模态、两种期望目标的view匹配、独立模块和清理。沿用[上一轮独立官方观察器/控制器源码](official-runtime-control/evidence.json)及相同产物hash；新guard及run-sta源单独归档。没有重跑未变业务套件，成功读回/成功清理不计G1通过。

结论限于该主机和批准的HKCU部署条件。原proof的两路径独立绑定能成功、真实view却选系统类，官方样例在正确前置下也未能进入；后续应取得目标内精确激活/加载证据或验证宿主/部署条件，不盲修未被触达的扩展接口。
