# desktop-04：两次目标进程内只读诊断，已清理

本轮仅诊断，不记功能通过。Shell 为 shell-build-04 的 3224A04C…，Host D26CA431…；03:32:53 +08 原生 guard 登记，9 字段匹配，之后新建 HWND 12847396 / PID 7428 / creation 134336287743552061。冷进入根后 45.162 秒仍显示 Loading；未 F5、interop 或预热。

两次固定 PROPERTYKEY 读回都满足 VT_BSTR、2048 UTF-16 上限、v1、执行 pid=7428（等于目标，而不是 observer 21316 / 12520），且末次 PIDL/view 稳定。原文分别在 [callback-diag-01.json](callback-diag-01.json) 与 [callback-diag-02.json](callback-diag-02.json)。完整原始 JSON 保留，无可选字段补值。

两份 payload 唯一差异是 tick：31575875 → 31644781，相隔 68.906 秒。其余字段一致：folder=110、source=109、last_clone=0、enum_n=0、enum_tid=0、enum_status=2；view 存在，cb_hr=0、site_n=1、window_n=1，ctor/site/window/owner thread 都为 412，reported/attached HWND 都为 592132；started/published/post/arm/tick_n/refresh 均为 0，slots=1。这是数值观察，既不推断缺少 Signal，也不把非事务原子读数写成根因证明。

03:37:24 +08 前完成同 guard 18940 stop/unregister，CurrentViewClean=true、无清理错误，包外 9 项 missing。实际 Desktop 为 00021400 类 / 2 B / 32 项，主列表和导航树入口都消失。Host 23376 正常撤销服务端会话、local_clear 并退出。目标窗口已由 fresh CUA/native 双重确认关闭；PID 暂驻留、无 debugger，未强杀。SDK broker 1049642 / 21232 继续按 root 要求保留，Core 未由此 worker 停止，不宣称完整 COM 卸载。

第三次 F5 对照的条件授权到达时，本轮已经注销并 9missing，因而没有执行，也没有重新登记。两张 screenshots/*.jpg 均为原始 CUA JPEG 字节，未编辑。精确哈希、两份完整 payload、稳定性和清理检查汇总见 [diagnostic-summary.json](diagnostic-summary.json)。
