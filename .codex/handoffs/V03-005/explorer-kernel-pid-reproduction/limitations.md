# 内核事件筛选限制与接续

计划bc7c7ebf在Start/Open/三个Enable返回0、注册正控已收到之后，实际接收Kernel-Registry event4/version0/headerPid10716。该PID不在选定集合，工具未读取或保存payload，立即停止；root独立确认会话4201、两个进程退出，GUI入口未提交。原始记录及hash见evidence.json。

这使当前内核事件配置无法满足“非目标进程事件不进入会话”的约束，不能再把Enable返回0等同于筛选生效。[微软PID过滤说明](https://learn.microsoft.com/en-us/windows/win32/api/evntprov/ns-evntprov-event_filter_descriptor)针对用户模式provider实例；[V2.FilterDesc](https://learn.microsoft.com/en-us/windows/win32/api/evntrace/ns-evntrace-event_trace_properties_v2)仅用于system-wide private logger，不是通用内核PID过滤入口。[System Providers](https://learn.microsoft.com/en-us/windows/win32/etw/system-providers)把原事件组映射到关键词，未提供新的Registry PID白名单保证。已核对SDK和[TraceSetInformation枚举](https://learn.microsoft.com/en-us/windows/win32/api/evntrace/ne-evntrace-trace_query_info_class)，没有找到满足该范围的公开入口。

不再重复相同内核配置，也不切换到全局事件流后仅在消费端丢弃。原授权仍有效，下一步准备更窄的用户态方案：Microsoft-Windows-User-Loader已安装事件3/8/10的加载失败、原COM class-not-registered，以及明确属于本工具的私有控制事件。它保持原PID范围、超时和清理；不伪造Microsoft事件，不改变原组件或系统策略。控制事件仅验证采集链路，不证明所有加载失败都会被系统provider报告。

本机metadata已保存于.runtime/explorer-etw-owned/user-loader-provider-metadata.json；User-Loader GUID在System32/ntdll.dll中有一个二进制常量匹配。该静态线索支持用户态来源，实际筛选和投递仍需运行验证，不能提前记为通过。Windows原生入口根因尚未解决。
