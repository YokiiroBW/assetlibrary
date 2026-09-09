# 用户态ETW与真实入口提交对齐

状态partial，Explorer仍显示普通空目录。第五轮准备中提交了两次真实入口：首次晚于捕获结束，第二次处于有效窗口。有效捕获仅覆盖最终Return，不包含预填/自动补全。COM/UserLoader未报告目标失败事件，不能据此断定未尝试加载或激活；G1..G4未关闭。

## 准备与所有权

主协调通过用户批准的30分钟管理员确认会话和固定用户态采集器接收队列请求，本轮无新UAC。本任务保留其dotnet控制台7209254和用户原Explorer1247028，仅一次受支持launch创建1836972。fresh UIA为SDK10目录17项；原生主窗口对应PID32680/Session2，创建13:18:10.9859309Z。

原动态DLL SHA256为0174DB9B1B4CCD4925D3A28470930FA6FAEBD4070348CC374A7CB87313E2FD15，使用新的自有.runtime/explorer-live/20260909-session-entry副本。没有改源码、GUID、接口、SDK或注册语义。相同sandbox FolderName.{CLSID}空目录已核验Directory/非reparse/零子项。原owner保护脚本13:18:32.3710943Z注册，guard600秒；相关Explorer仅32680、17944、6284。

## 两次提交的时间关联

主协调报告计划93dd8489558e4edc87bf93c7a6a83314窗口为13:20:20至13:21:20Z。收到ready后经过焦点观察、路径输入读回、Return，但实际提交13:21:33.466Z，晚于结束约13.5秒。该次control-only捕获不覆盖GUI，不能解释其失败。原始ui-observation.json中的coverage pending保留当时状态，最终判断在capture-correlation.json。

13:21:33.752Z fresh UIA为目标目录0项/“此文件夹为空。”；13:21:33.9223638Z两键仍true，三个计划进程模块查询成功且无proof DLL，当前副本无trace。此为捕获窗口外的GUI观察。

双方随后调整为ready后同一functions脚本立即通知。按明确指令先回SDK目录，13:24:06.463Z UIA为SDK tab/17项，地址栏随后读回SDK绝对路径。13:24:42.132Z预填并核对目标路径，不按Return，窗口仍SDK目录、焦点地址栏；预填/补全明确排除在下一捕获范围外。

计划f2f729be-fdd3-4ae7-b926-e6735bb49141的自动ready观测为13:25:17.3852141Z，本任务仅一次Return：13:25:38.586Z。主协调确认截止约13:26:16Z，因此最终提交位于有效窗口内。13:25:38.834Z fresh UIA仍为目标目录0项/空文件夹；13:25:39.0011222Z两键true，三个计划进程模块读取成功且无proof DLL，当前副本无trace。未扩张为所有进程，也未使用截图作为独立证据。

主协调报告捕获完整60秒结束，无丢事件、PID或schema异常，独立status确认会话清理；只收到私有control，COM/UserLoader没有目标失败事件。ETW生命周期和事件结果来自主协调；本任务独立记录UIA、操作UTC、模块及注册读回。无失败事件不能证明未调用COM/loader，不能覆盖采集前的自动补全，也不能推出缓存或CRT根因。

## 清理与验证

guard先按600秒自然期限退出0，finally13:28:32.5031709Z两键false；stop文件创建13:28:33.5405039Z，晚于deadline，所以不是手动stop触发。空目录核验完整路径/Directory/非reparse/零子项后非递归删除，自有1836972经fresh UI确认关闭。最终list保留原1247028、管理员控制台7209254及Codex；后两者未被本任务操作。管理员会话结束由主协调/用户负责。

时间、预填、两次UIA/模块、捕获边界关联与清理原始记录见[证据索引](explorer-user-trace-aligned/evidence.json)。无新框架、依赖、共享契约或生产代码，复用原注册guard、系统Shell和computer-use。固定三进程读取不提供大资产库/长期性能保证。未重复loader/root-bind/图片矩阵或全仓库业务套件；两次GUI提交仅一次最终阶段被捕获，不计为两份独立通过测试。

后续不继续更换接口、注册位置或DLL试探。结论限于真实GUI未激活且本次受限用户态失败事件未解释原因，由主协调裁决下一步。
