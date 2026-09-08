# Windows启动与所有权清理检查点

状态partial。源码a21a145、ACL兼容修复53bc455、删除幂等回归d152a80781442e9e1e21416649b5f469a99d6485；完整原生文件/网络/COM/句柄/资源/父退出矩阵仍未完成，不启用Windows能力。

## 实现

StartAsync返回实际启动任务，不用本层WaitAsync遗弃Win32工作。启动有15秒合作式截止、逐阶段取消、64KiB分块/强hash验证复制；原生API本身不能取消时，Core按共享约定结束HTTP并保留有限并发名额。Job终止回调提前注册，记录原PID/创建时间并复核取消后才resume。

Dispose两秒未完成或失败时使用共享ImageChildCleanupPendingException，Completion继续持有原进程/Job/stdio及清理责任。确认进程退出后才释放句柄并删除profile；失败启动的原异常保留为InnerException，清理错误另记安全代码。共享broker和pending/reaper桥由V03-007单写，不能仅凭本层测试声明完整集成期限通过。

WindowsImageOwnerJournal、WindowsImageArtifact、WindowsImageProcessIdentity按root批准独立承担私密有界记录/原子更新/lease、验证复制、PID/创建时间或有界精确worker SID退出确认。没有partial拆分或降低CA1506/重复代码门槛。新私有windows-image-owners-v2与alimg2名称不接管旧记录；记录最多64项、每项512字节，损坏/不确定Prepared记录保留并拒绝继续；不猜测创建成功或删除未知对象。

root独立复验发现原一次Owner+DACL更新在继承仅Modify的目录失败。53bc455先发布相同严格protected DACL（当前User/SYSTEM FullControl），Owner相同不请求WRITE_OWNER，否则设置后重读验证。真实Modify-only父目录回归通过，没有放宽DACL。journal数字字段要求canonical round-trip。

## 已取得证据

- a21检查点：Core/Preview.Tests零告警构建、六项通过，涵盖五项生命周期与原真实AOT PNG。
- 53bc455：新增ACL回归，加前述六项合计七项通过/零skip；root在此前五例失败的相同目录独立复验七项全过。
- d152a80：针对系统profile已删除而旧Created journal尚存，真实删除同一自有profile两次均HRESULT0；目录首次删除后已不存在。保留旧journal再Recover成功，1/1通过。没有推测缺失对象返回码或额外删除未知对象。

不同逻辑用例总计八项（六生命周期、一ACL、一真实PNG），重复执行不累计。只读幂等原始数值和TRX摘要/hash在 `windows-image-lifecycle/`；原始TRX在本任务.runtime/windows-image/results。原四项Explorer loader计数保持独立。两个新增源码文件曾触发CA1506与重复声明门禁，按职责提取和复用修正；最终相关构建、格式、源码策略（424个C#文件）及diff通过，没有压制分析器。

同一个真实worker候选继续使用hash155FD3C8334F3AD60E4437FFB83BD9B4C956C0A0225DF6E221E1261BEBFC97AA。另为V03-007捕获landscape.png的512px真实输出1677B、SHA256 F67FE31590B54D36A47870FBDDDF748BBBDB4C4155196AAB25568254F8443057，chunks为IHDR/sBIT/sRGB/IDAT/IEND；其validator与Core回归由owner修正，未修改本任务以外工作区。

## 尚需完成

真实父进程崩溃与持有stdin情况下的Job回收、启动/清理超时桥边界、CPU/内存/子进程限制、文件/网络/COM旁路与非白名单句柄的完整测试。原生常量探针已在自有.runtime/windows-image/confinement编译，未执行这些新模式，不能计为通过。
