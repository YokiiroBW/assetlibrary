# ADR-0006：Windows独立客户端先稳定视图

状态：Accepted

决定：先在独立客户端验证UI、网络、文件操作和预览，再复用到Explorer。

2026-09-08 补充（ADR-0018）：上述顺序是视图开发验证策略，不是要求最终用户另开应用。
用户明确要求 Windows 首版以原生 Explorer 的资产库入口使用；当前先验证系统原生
DefView 与进程外 AssetHost。生产 Shell 仍须通过既有发现、故障和稳定性门禁。
