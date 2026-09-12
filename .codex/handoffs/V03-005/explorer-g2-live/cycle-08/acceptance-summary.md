# desktop-08：补齐 invalid-version / partial-frame

仅补两项，未重跑 crash/silent，未改生产代码、登记规则、旧 helper 或既有 desktop-07 结果。前轮两个自有窗口先完成关闭；本轮新 broker 为 PID6564/HWND460224，新目标为 PID16108/HWND6358406（creation 134336819662724357）。原 Explorer6212 的创建时间与存活状态保持一致，未被重启或强杀。

| 场景 | 故障与错误证据 | 恢复证据 |
|---|---|---|
| invalid-version | fault PID5660 的日志确认 client_pid=16108、写出40B；原生工具栏刷新后显示“响应无效”，截图与实际视图一致 | 故障退出后启动真实 Host，点击后退回SDK17、再前进重入；实际4FF/22B/1项“原生客户端隔离样例”，完成时间早于guard截止 |
| partial-frame | fault PID18324 的日志确认 client_pid=16108、写出20B；刷新后显示“后台连接服务不可用”，截图与实际视图一致 | 同样后退SDK17/前进重入，截图及实际4FF/22B/1真实库均完成于截止前 |

两次都使用原生工具栏 Refresh 按钮，没有 F5 键或额外 RootProbe 预热；Loading 不被计作恢复。invalid fault 是自身119秒寿命正常退出（StopSent=false），partial 使用自有停止事件退出（StopSent=true）；两者均 exit0、ForcedCleanup=false。三次真实 Host 均正常退出，日志含 server_revoked 和 local_clear。

登记始于 10:19:25.106543Z，截止 10:29:25.106543Z。partial 恢复原生记录于 10:28:36.626722Z 完成，距截止48.480秒，**未满足计划的至少60秒清理余量**。guard 仍按原600秒于10:29:25.364359Z自动清理；stop标记在10:29:36.070676Z稍后创建，不把它写成手动触发，也没有延长或重复登记。

同进程清理无错误，另包外9字段全部missing；SDK实际17项、侧栏命名空间入口消失。新旧自有窗口最终均无窗口，guard、全部自有Host/fault退出；目标/broker原Explorer进程可能暂驻留，不宣称COM全卸载。Core由root负责。Application Error/WER限定时间/PID查询没有匹配事件；缺少PID字段的WER不能据此完全排除。

一次日志读取曾因ReadAllText的共享模式与活动写入者冲突；改为只读FileShare.ReadWrite后确认listening才点击刷新，没有重启故障进程。原始错误、恢复截图、native记录、进程退出和事件关联均保存在本目录。旧周期逾期的invalid-recovered.json原样保留，不改写通过。

精确结果见 acceptance-summary.json，文件哈希见 evidence-index.json。这里只交付两项实机证据及清理结果；G2总门禁由root裁决，不声明G3/G4或全客户端完成。
