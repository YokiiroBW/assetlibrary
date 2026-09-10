# 两次当前用户Explorer重启后的实际入口对照

已在用户明确授权范围内完成测试前和清理后各一次当前用户Explorer重启。两次固定目标集合全部确认退出，Windows两次自动恢复新的桌面/任务栏并持续存活；临时注册与调试器清理完成。但本次冷启动没有改变已观测的官方入口失败和属性返回差异，G1仍未关闭。

## 时间、身份与执行结果

| 阶段 | 原始UTC/身份 | 结果 |
| --- | --- | --- |
| 重连预检 | 11:04:44Z，Session2 Active/Default输入桌面 | 最初因EdgeUiInputTopWndClass未分类拒绝，未注册/停止；原件保留 |
| 修正后预检 | 11:11:26.722Z | 原22目标SID/session/image/creation/无debugger及UI检查通过 |
| 原样注册 | 11:11:38.821Z，期限11:21:38.821Z | F298 DLL、323CD843 guard、原4CU根/精确Owner、双通知成功 |
| 第一次停止 | 11:11:59.552–11:12:00.220Z | 一次固定22目标全部held-handle退出，errors空；原桌面最后停止 |
| 第一个新桌面 | PID38388，creation134335123202223994 | Windows自动恢复；GetShellWindow/任务栏同PID；helper退出后的两次独立读回确认存活、Active/Default、无debugger |
| 专属窗口 | PID13040，creation134335123552278776，HWND9373906 | 在新桌面之后创建，独占单窗口；SDK17项及ThisPC22B/2项正控通过 |
| 唯一官方打开 | 11:14:43.069Z，42B/flags1 | ready后唯一Browse提交；10秒外限到期，无browse.return HRESULT，实际“无关联应用”模态 |
| 清理及第二次停止 | 11:17:02.104–11:17:02.211Z | 先脱离调试、关闭模态/专属窗口、撤注册和双通知，再一次停止桌面38388与驻留测试13040；2/2退出、errors空 |
| 第二个新桌面 | PID32044，creation134335126222145497 | Windows自动恢复；helper退出后两次独立读回同身份持续存活；Active/Default、无debugger，测试DLL未加载、8根absent |

没有按泛进程名kill，没有动态追杀自动恢复的新PID，工具未负责启动桌面进程。Chrome、Codex和NAS不在停止范围。原文件夹窗口按授权关闭，未承诺或实施布局恢复。

“helper退出后存活”的顺序依据是两次同步stop命令的原始工具返回均为`exit_code=0`（返回块109446、08d44e），收到完成返回后才发起后续独立desktop读回：第一次在11:12:11.982Z，第二次在11:17:21.081Z。没有单独记录stop helper的PID/creation，也没有单独原始helper退出日志；`final-cleanup.json`的`AliveAfterStopHelperExit=true`是由上述调用完成先后及两次快照归纳，不能当作额外原始进程观测。未为补证据再次运行stop或伪造helper身份。

## 实际入口与属性结果

ThisPC刷新后仍2项，未发现官方示例。一次树项点击报告`coordinate input geometry is unavailable`，在重新观察后改用Ctrl+L/普通Shell位置导航；未把失败点击当已导航，也未追加第二次官方打开。官方打开后模态内容为没有关联应用；关闭模态后的原生活动视图仍为ThisPC类`20D04FE0-3AEA-1069-A2D8-08002B30309D`、22B/2项，官方42B匹配失败（exit2），ThisPC匹配成功（exit0）。有效注册期间目标模块读回未见样例DLL。

| callId | 输入mask | 实际HRESULT | 输出mask | 输出有效 |
| --- | --- | --- | --- | --- |
| 1 | 20000000 | 00000000 | 00000000 | true |
| 2 | 40418000 | 00000000 | 00000000 | true |
| 3 | 40000000 | 00000000 | 00000000 | true |
| 4 | 2044007F | 00000000 | 00000026 | true |

只在call1/4明确请求FOLDER，不能从call2/3缺该位推出非folder。已审查B41E76F1观察器原样执行：ready tick277931140，匹配tick277931546/562；16次首机会E06D7363后原异常保护在277959625早停，约28.485秒，非完整60秒。原初始上下文查询/恢复错误保留，随后暂停身份、4返回+1入口断点删除、剩余0、Detach和post-detach无debugger/目标存活均确认。`passed=false`不表示清理失败。

此前同注册、同完整PIDL、同mask及双查询顺序的两个独立进程仍是20000000→20000000、2044007F→20000024；本次完整当前用户Explorer重启未改变host侧0/26差异。这没有证明所有系统或其他进程缓存都已清空，也没有确定具体持久配置根因。

**下一条最有区分力的只读取证方向：评估从真实Explorer调用链观察目标CLSID/ShellFolder属性的实际读取，避免再次重复旧COM、Bind或冷启动对照，也不修改系统策略。** 本轮未追加该实机变体。

## 工具复用、静态验证与证据

本次实际重启源SHA `06CD2EE08C0CE153B2CD96DBF3A92F90FB85F3A500CF8EF0A13BC3F94A1CB259`，wrapper仍`448AB744EC8F0A7354A9871D92C5F5FBD158192A0315BFF92FC06BDF68958AAF`。主协调批准的v2仅增加严格顶部cloaked细条分类纯谓词；身份、操作窗口、逐目标Active/Default、固定集合、partial finally退出核验与单次marker门禁保留。232d753的历史F36准备材料未覆盖。

主协调的27项纯分类验证有self-test.ps1/validation.json原件；另20项独立内存复核只有工具输出和hash读回，本归档不为其伪造单独日志，也不将27项原件归给20项。这些均不是GUI或产品案例。复用F298官方DLL、323CD843 owner guard、C6ED563F活动视图观察器、508C3E5B Browse控制器、B41E76F1属性观察器；本轮wrapper只适配输出目录。无产品代码、共享契约、架构依赖、注册字段、系统策略或权限模型修改，无原资产写入。UI检查不证明没有任何后台IO。

[证据索引](cold-session-execution/evidence.json)保存48份来源文件/派生矩阵，其中身份或账号路径相关JSON使用明确标注的脱敏副本；真实原件保留runtime，索引分别映射原件SHA与副本SHA，脱敏件不冒充原件。包括两次stop、新桌面两次独立存活读回、注册/清理双通知、实际view、完整属性日志和早停/脱离记录。最终原件/副本/index哈希与断言核对、diff审查、标准handoff校验完成后提交；不重跑未变产品套件，不宣告G1..G4、20次恢复或8小时稳定性完成。
