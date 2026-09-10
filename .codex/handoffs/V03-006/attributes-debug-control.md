# 真实官方项GetAttributesOf输入与返回

固定单属性观察器B41E76F1…在目标进程中取得4对精确匹配的输入mask、实际HRESULT及输出mask。两次明确请求FOLDER的调用都以S_OK返回且输出未包含该位；这只是该host路径的已观测行为，不直接认定注册或本体根因。未请求FOLDER的另外两次不能用输出缺位推出非folder。

本轮仍为原F298/四根/323CD843双通知STA/600秒，新自有3738220/PID15548、creation134334905481728533，epoch在CUA创建前记录；SDK/ThisPC实际view正控通过。计划d036850368694918b193b16673a61df3，观察器完整SHA256 B41E76F156C39026294351433F9221BD17B90F878F59439EE3422030F7E3B6D0在启动前重验。

工具只使用已审查的系统GetAttributesOf候选，cidl须为1且首项完整不透明PIDL匹配后才记录32位mask；没有读取其他项或改写任何mask。reference/系统文件/目标候选校验均S_OK。系统RVA只是此次诊断依据，不是产品实现。

ready tick256117531，包装记录05:11:09.6670651Z后自动联动唯一Browse；提交05:11:10.024Z/tick256117906，官方42B PIDL/flags1/ThisPC前置成立。四对匹配都在tick256117906：

| callId | 输入mask | 实际HRESULT | 输出mask | 输出有效 | 请求FOLDER |
| --- | --- | --- | --- | --- | --- |
| 1 | 20000000 | 00000000 | 00000000 | true | 是 |
| 2 | 40418000 | 00000000 | 00000000 | true | 否 |
| 3 | 40000000 | 00000000 | 00000000 | true | 否 |
| 4 | 2044007F | 00000000 | 00000026 | true | 是 |

唯一Browse仍出现“无关联应用”模态331134，控制器10秒到期，无browse.return HRESULT；本表不是外部Browse返回值。随后首机会E06D7363记录及callback health保护在tick256150562提前结束，约33.031秒、4入口4返回/12非匹配，非完整60秒。没有提高上限、增加绑定断点或覆盖旧记录。

初始running上下文查询失败原样保留，后续ACTIVE暂停身份正确；4个return断点和1个entry断点删除、剩余0、Detach均S_OK，post-detach查询无debugger且同目标存活，cleanupVerified=true。passed=false不表示清理失败。

确认脱离后关闭模态，注册有效期内actualview仍ThisPC20D04…/22B，与官方42B不匹配；ThisPC匹配成功/2项，观察器和目标模块快照未见样例。stop/finally于05:15:57.6193927Z正常exit0，4CU/4LM全absent，双通知清理成功。自有3738220及模态已关闭，原1247028和无关Chrome保留。

[原件索引](attributes-debug-control/evidence.json)保留plan、固定hash、包装、4对callId关联、有效性标记、所有早停/清理事件、有效期view/模块及现场清理。未自行解释或修复注册/host，下一步由根协调据这些具体mask结果裁决，G1仍未关闭。

## 恢复后的只读调用链核对

独立普通枚举的属性正控使用 `ParentVisible.cpp` 第42行的 `candidate`：它来自先前 `EnumObjects` 保存的 child；代码通过父级 `CompareIDs(SHCIDS_CANONICALONLY)` 匹配解析所得的 `targetChild` 后，对枚举 child 请求 `28180000`，返回 `20000000`。该控制没有记录两者的完整字节是否相等，也没有对解析 child 查询相同属性。

真实观察器的匹配对象则来自 `Observer.cpp` 第81–87行：按 Browse 的固定绝对路径 `SHParseDisplayName`，核对 MyComputer 父级后克隆最后 child；第362行对目标内 PIDL 做有界完整字节比较。四对已捕获结果针对这个解析所得的 child。Browse 控制器同样使用解析所得的绝对 PIDL。

因此现有对照同时存在 PIDL 来源、输入 mask 和进程上下文的差别，不能把结果直接归为纯宿主进程差异。已向主协调提出下一项低侵入控制：同一独立 MyComputer 对象、同一注册窗口内，对唯一匹配的枚举 child 和解析 child 记录长度/完整字节相等性，并分别查询相同四组输入 mask，逐次保留 HRESULT 和输出有效性。该建议尚未执行，未新建注册、GUI 或调试附加；不解析私有 PIDL 布局、不改属性。
