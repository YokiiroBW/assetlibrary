# Explorer 门禁证据裁决（2026-09-12）

本报告保留最初G1裁决；后续[真实故障恢复](../explorer-g2-live/README.md)已关闭G2。当前G1/G2通过，G3/G4保持开放，`explorer-v0.5` 仍阻断。该裁决按原 `tests/architecture/m0-gates.json` 退出标准执行，没有新增验收条件，也没有将已通过功能等同于发布。

下表记录G1关闭时的历史缺口；G2当前结果以链接的后续实机报告为准。

| 门禁 | 已证明 | 尚缺 |
| --- | --- | --- |
| G1 | Accepted ADR-0018 的自有 HKCU 路线；Windows 11 真实 Explorer 发现、加载、进入自有 DLL；无 HKLM 回退 | 无；登记 closure_evidence 并关闭 |
| G2 | 真实视图打开，Host 缺失和正常停启恢复，owner 注销及独立九字段无残留 | 真实 Explorer 中 Host crash、timeout、invalid frames 后错误显示及正常 Host 恢复，Explorer 不失败 |
| G3 | 组件级 deadline/取消及有界刷新实现；真实界面功能观察 | 实际 UI 返回、取消回收及重连的计时；反复恢复无游离 worker 和 DLL 生命周期泄漏 |
| G4 | 已有功能迭代观察，不计作规定故障循环 | 20 轮真实 Explorer 参与的崩溃/重启恢复；完整 8 小时内存有界、无 WER/Application Error |

G1 依据为 [ADR-0018](../../../../docs/adr/ADR-0018_Windows原生Explorer入口与最小视图.md)、[自有入口原始索引](../../V03-006/proof-native-entry/evidence.json)与[最终真实数据周期](../explorer-host-integration/final-cycle/evidence.json)。Windows 平台版本见 [host-readonly.json](../windows-entry-research/host-readonly.json)。V03-006 的 `guard-machine-preflight.json` 证明两个 HKLM 根不存在，`register.json` 记录包外普通用户登记，`proof-after-entry.json` 为实际 4FF 类/22 字节根 PIDL/1 项，`target-proof-module.json` 确认目标 Explorer 加载匹配的自有 DLL。官方样例只用作路线参考，不能代替自有代码证据。

G2 注销子项已通过。历史 `final-cycle/evidence.json` 的 `G2_full_COM_unload` 字段及 V03-006 loading-refresh 报告对 G2 的称呼分类有误：原门禁把 DLL 生命周期放在 G3 第二条。保留历史记录和失败事实，以本裁决作为当前映射；不要求所有 COM 对象立即释放才能承认注册表清理。CViewSettings 保留来源只是观察，尚未证明最终释放或内存增长有界，因此 G3 保持开放。

最终周期明确 `HostStoppedNormally=true`、`HardKillUsed=false`，不能扩读为崩溃测试。稀疏 GUI 截图的观察上界也不是 ready 延迟测量。正式安装、登录设置、预览与主动权限失效等产品项单独跟踪，不据此重新打开已经满足的 G1。

V03-012 test-only 故障工具已完成并集成为 88e6c21，严格构建与组件回归通过；下一项是 V03-005 实机验收。工具只读合成帧、不访问资产、不杀 Explorer、不加入生产 Host。当前远程会话为 WTSDisconnected，真实故障周期尚未执行，见[恢复计划](g2-acceptance-plan.md)。G4 沿用 [原 soak 协议](../../../../docs/spikes/M0-002/explorer-soak-protocol.md)，Host-only 测试不能替代真实视图。
