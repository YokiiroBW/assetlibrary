# G3 五个预定恢复样本

五个样本均使用同一 Explorer PID16068、creation134336932049894842、同一4AE347…模块。经root批准分为两个独立600秒登记周期：desktop-01完成01–02，desktop-02仅继续03–05，没有重复成功样本或延长guard。中间保留目标/broker及模块映射，累计Enum/Query/cancel计数连续。

12份原始G3读取（基线、分段连续性、5份错误、5份恢复）均校验执行pid等于目标、末次view/PIDL稳定。主测量为目标进程内部QPC，hz=10,000,000；不以截图、工具提交或并发end-start差值代替。

| 样本 | 重开根首次有效Ready观测 | 静止objects/callbacks/slots | cancel/reap | op/cancel/pins live |
|---|---:|---|---|---|
| 01 | 2650.2502 ms | 6 / 1 / 1 | 1 / 1 | 0 / 0 / 0 |
| 02 | 2632.7720 ms | 6 / 1 / 1 | 2 / 2 | 0 / 0 / 0 |
| 03 | 2617.2135 ms | 6 / 1 / 1 | 3 / 3 | 0 / 0 / 0 |
| 04 | 3134.9231 ms | 6 / 1 / 1 | 4 / 4 | 0 / 0 / 0 |
| 05 | 1030.2071 ms | 6 / 1 / 1 | 5 / 5 | 0 / 0 / 0 |

累计max：Enum 161.013 ms、Query 161.0083 ms、现有UI Refresh 26.1308 ms、取消完成0.0942 ms。Query总测量不能写成小于150ms；150ms仍是代码内部前台等待预算。冻结判据为Enum/Query/现有UI Refresh <250ms、取消≤2000ms、重开Ready≤10s。计数为非事务读；数值与门禁最终裁决由root负责，不自行推断所有系统引用已释放。

## 辅助响应数据的限制

旧1ms观察器有3个零样本点，原件保留并标inconclusive；不能用exit0当作有效响应/资源证据。新观察器已验证至少1个真实样本与resources行，其后点样本和30s区间有效，但不能重建早期瞬间。

- 03旧30s区间结束比故障Query开始早1.4759763s。
- 05 v2区间在故障Query结束后0.4921243s才开始，又在重开obs_start前3.2646541s结束。

两个30s区间各276样本、0失败，但仅为前后消息循环响应佐证，不宣称覆盖故障/恢复中。主结论来自内部QPC。

## 清理与范围

全部样本defer_n=0，没有异步deferred排空样本；pins=0不是OS DLL已卸载。五个静止点对象/callback/slots均与基线6/1/1一致，合法外部COM引用仍可能存在。

第一段guard于13:36:43.027Z自然到期清理，stop标记稍后；第二段于13:49:24.012Z显式stop、13:49:24.267Z清理，早于13:49:52期限。两段均同进程owner注销、独立原生9missing。SDK17与侧栏入口消失已确认，所有Host/fault/response/guard已退出后才关闭自有窗口；fresh CUA/native均无自有窗口，原Explorer6212同creation存活，未重启或强杀。Core由root单独停止和验证合成原件。

G4按用户豁免未执行，不把豁免写成实测通过；没有20轮、8小时或额外历史owner卸载诊断。精确原文、图片路径、连续性、所有响应资源行见g3-measurement-summary.json，双段文件哈希见evidence-index.json。
