# 真实Explorer属性读取：只读准备与最小方案

本轮仅审阅既有观察器、官方注册源码、原guard和公开API文档。没有GUI、注册、attach、重启、系统策略或产品代码修改，没有重跑已完成控制。主协调并行解析固定windows.storage.dll的准确函数/调用边界；本文不把尚未定位的读取API或内部ABI写成既成事实。

**建议只有一项：在精确PIDL匹配的GetAttributesOf活动调用内，仅观察一个经离线验证的目标属性来源边界，优先选择可覆盖缓存命中的消费/合并点，与同callId外层mask返回配对；若静态证据支持选择注册读取点，再记录其身份、状态、类型/大小/值。** 不先启用所有Reg*断点，不建立泛用trace或进程范围句柄追踪框架。

新增准确符号后，该“注册读取边界”须满足一项前提：调用确实发生在所选窗口内。主协调的匹配PDB签名原件已只读核对，得到下表零偏移候选；尚未决定实机点。`_LoadValuesFromRegistry`可能在ThisPC正控或F5阶段就完成缓存填充，随后attach的outer范围内零命中很可能只是缓存命中。因此若静态控制流确认读取只发生于填充路径，方案改为同outer内**一个可覆盖缓存命中的值消费/合并点**，不同时叠加两类点，也不因零命中再盲采。

| RVA | 准确符号签名概要 | 返回解释边界 |
| --- | --- | --- |
| 1188E0 | CRegFolder::GetAttributesOf | HRESULT，原B41外层点 |
| 119098 | CRegFolder::_AttributesOf(IDLREGITEM const*, DWORD, DWORD*) | HRESULT；符号名不能授权解码私有PIDL布局 |
| 11B8F0 | CCLSIDInfoCache::_LoadValuesFromRegistry(CLSID_CACHE_ENTRY*) const | void，没有可按HRESULT/LSTATUS解释的返回状态 |
| 119F50 | CCLSIDInfoCache::GetPerUserAttributes(GUID const&) | DWORD返回值，0或高位置位不能直接当失败HRESULT |
| 11B054 | CCLSIDInfoCache::_QueryCallForAttributes(CLSID_CACHE_ENTRY const&, DWORD, DWORD) const | DWORD返回值，含义需结合消费分支，不能套注册API状态 |

主协调报告PE/CodeView/PDB匹配；原件module_info记录SymPdb、GUID `FA3BF0701EBD655CD7B3691D3BF1C00A`、age1及unmatched=false。签名只提供边界和类型，不证明`CLSID_CACHE_ENTRY`字段布局，也不构成根因证据。若最后选择消费/合并点，下表中的注册读取status/type/size判别须按实际数据流重新定义，不能从DWORD合并结果倒推真实注册返回码。

| 可区分的假设 | 所需观测 | 可得结论边界 |
| --- | --- | --- |
| 目标读取失败、选错位置/视图或取得不同值 | 证明目标key身份后实际非零读取状态，或有效读取值/类型不同 | 缩小到具体读取边界；非零错误本身不等于权限根因 |
| 已正确取得A0000020，随后转换/过滤产生0或26 | 同一outer call内有效DWORD读取A0000020，outer仍返回已见mask | 调查读取后的消费路径；不能由调用同线程单独证明该值确被最终结果使用 |
| 未命中这一个读取点 | 外层匹配存在但内部无可归属事件 | 仅说明该点/同步窗口未命中；可能是缓存、窗口外读取、异步或不同分支，不能写“未读注册” |

## 可复用范围及必须补齐的关联约束

固定B41观察器源SHA256为`26FDC2A5CC4E7445503F01B2136A8B5103D9B756CF12E26920A5B9AD04547C28`，EXE仍B41E76F1。`PendingCall`已有return site、thread、callId、expectedRsp、maskAddress；入口仅在cidl=1和完整opaque PIDL匹配后建立记录，返回通过site+thread+expectedRsp反向匹配并删除。已有32 pending、128匹配调用、64返回站点、4096总入口、60秒及异常保护边界可复用；它当前**没有**注册读取事件或线程ID输出。

这里的thread来自`GetCurrentThreadId`，是DbgEng引擎ID；`SetMatchThreadId`也要求引擎ID。若需要与OS/ETW线程关联，另取`GetCurrentThreadSystemId`，分字段记录，不能把两者混用。[引擎线程ID](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/dbgeng/nf-dbgeng-idebugsystemobjects-getcurrentthreadid)、[断点线程范围](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/dbgeng/nf-dbgeng-idebugbreakpoint-setmatchthreadid)、[系统线程ID](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/dbgeng/nf-dbgeng-idebugsystemobjects-getcurrentthreadsystemid)。

仅在outer pending存在且同引擎线程时允许内部候选；对嵌套调用选择经过栈关系核对的最内层outer，另配inner sequence和自己的entryRsp/returnRsp。同线程发生并不自动构成因果关系，必须同时证明准确调用点和目标key来源。不能把opaque HKEY数值或仅名字`Attributes`当成BA16目标身份；若调用点只有预先打开的hkey而来源无法证明，应报告身份不明并停止扩大解释，而不是自行增加全进程open/close追踪。

当前事件订阅没有线程退出处理，first-chance异常在累计上限前也不清理pending。对于新增嵌套归属，需在受影响scope发生异常/线程退出或配对不完整时使窗口无效并安全结束，避免把栈复用后的后续事件归给旧callId；不吞掉目标异常，不为了覆盖更多事件提高旧上限。辅助点只有在相应outer范围内启用/归属，未知线程或无pending时不得读取/输出无关注册payload。

## 原四根官方字段核对及证据缺口

固定官方`Dll.cpp`209–224的10个注册值及MyComputer挂载默认值共11项，与323CD843 guard的四根写入清单相符，没有发现漏写下表字段。字段来源为仓库内已保存的官方原件SHA `D9D89FA1C3288E4957426AF49A61D6C31B90CD00AED8F6924C6EED1EF2630C19`；未把2004/Win7样例当作Windows11完整支持保证。

| 根/相对路径 | 值名 | 类型 | 预期值 |
| --- | --- | --- | --- |
| Classes/CLSID/BA16 | 默认 | REG_SZ | FolderView SDK Sample |
| BA16/InprocServer32 | 默认 | REG_SZ | 同一固定F298 DLL路径 |
| BA16/InprocServer32 | ThreadingModel | REG_SZ | Apartment |
| BA16/DefaultIcon | 默认 | REG_SZ | shell32.dll,-42 |
| BA16/ShellFolder | Attributes | REG_DWORD | A0000020 |
| Classes/CLSID/BB8F | 默认 | REG_SZ | FolderView SDK Sample |
| BB8F/InprocServer32 | 默认 | REG_SZ | 同一固定F298 DLL路径 |
| BB8F/InprocServer32 | ThreadingModel | REG_SZ | Apartment |
| BB8F/ShellEx/MayChangeDefaultMenu | 默认 | REG_SZ | 空字符串（存在，非缺失） |
| Classes/FolderViewSampleType/shellex/ContextMenuHandlers/BB8F | 默认 | REG_SZ | BB8F完整CLSID字符串 |
| Explorer/MyComputer/NameSpace/BA16 | 默认 | REG_SZ | FolderView SDK Sample |

四个诊断Owner是额外一致性条件，不混入官方11字段计数。官方机会性`PSRegisterPropertySchema`未执行是已知限制；源码说明其失败影响部分详情属性功能，当前没有证据把它变成根入口失败的已证原因。追加ASSOCCHANGED及guard寿命/Owner也都是已记录的诊断差别，均不在本轮改动。

**旧证据不足之处：** `Read-Presence/verify`只返回根键存在及Owner；`register.json`的`AttributesHex='A0000020'`是输出常量。`Assert-Tree`遍历实际值/子键核对值和类型，却没有反向检查expected值/子键是否缺失。创建代码逐项SetValue及最后清理读回仍是证据，但不能把这些摘要当作宿主确实读取了REG_DWORD A0000020，也不能把过程中的presence检查当作11字段持续完整的证明。

主协调已同意在下一获准注册周期，用独立只读检查器对固定四根11字段做**每项存在性、类型、值**读回，并分别核四个Owner；默认空字符串必须区分缺失，禁止GetValue默认值填补缺失。保持Registry64、原路径、原guard写入/cleanup/600秒不变，至少覆盖开始和目标调用后的有效注册窗口；只输出固定字段、比较结果和必要值，DLL路径按现有脱敏规则保存。这是补强实际读回，不是增加注册字段。它仍不能证明目标进程使用了同一合并视图；HKCR涉及HKCU/HKLM合并规则，必须以真实调用点的来源/视图证据判断。[Microsoft HKCR合并视图](https://learn.microsoft.com/en-us/windows/win32/sysinfo/merged-view-of-hkey-classes-root)。

## API、ABI与清理契约

下表是等待准确调用点落定时的公开ABI核对表，**不是要同时增加这些断点**。仅当离线控制流和导入/符号证明实际边界对应其中一个公开API时使用；内部helper不能只凭名字套原型。x64首四个整数/指针参数在RCX/RDX/R8/R9，剩余参数位于栈；下列偏移只针对callee入口、尚未执行prologue且返回地址位于RSP的时刻。[Microsoft x64调用约定](https://learn.microsoft.com/en-us/cpp/build/x64-calling-convention?view=msvc-170)。

| 实际边界若为 | callee入口参数 | 必须区别 |
| --- | --- | --- |
| RegGetValueW | RCX=hkey，RDX=subkey，R8=value，R9低32=flags；[RSP+28h]=type*，[+30h]=data*，[+38h]=size* | subkey可空；需证明hkey来源，记录视图/type限制flags |
| RegQueryValueExW | RCX=hkey，RDX=value，R8=reserved，R9=type*；[RSP+28h]=data*，[+30h]=size* | hkey已打开；只有value名字不足以认定目标key |

这两类返回是低32位`LSTATUS`，只有`ERROR_SUCCESS==0`算成功，不能用`SUCCEEDED`把正数Win32错误当成功。入口保存原data/type/size指针及容量；仅成功、类型已确认REG_DWORD、长度4、原容量足够且可读时记录4字节值。type指针缺省、大小查询、MORE_DATA、缺失/拒绝等路径只记录可证明的状态/元数据，不读失败payload，也不把zero-on-failure得到的0当注册值。只比较有界固定key/value名称，不输出无关路径、SID或任意数据，不修改参数、mask、寄存器、hkey或返回值。[RegGetValueW契约](https://learn.microsoft.com/en-us/windows/win32/api/winreg/nf-winreg-reggetvaluew)、[RegQueryValueExW契约](https://learn.microsoft.com/en-us/windows/win32/api/winreg/nf-winreg-regqueryvalueexw)。

保留固定目标身份/会话、module文件hash与PE/CodeView/候选代码指纹、只附加一个自有进程、ready后一次Browse、合作取消以及有界数量/时限。新增inner entry/return资源也必须纳入同一个cleanup：确认自有暂停上下文后移除所有断点并验证0，Detach后验证同creation目标存活/无debugger，最后按原guard撤销。没有确认ABI/目标key来源/完整清理就不进入实机。

在工具实现稳定后，仅补与新增关联风险有关的独立测试：其他线程/非目标key拒绝，嵌套/同返回站点配对，Win32非零状态和空/小buffer不读payload，异常或线程退出使scope失效，正常/取消清理无残留。原有未变控制不重跑。当前仅完成方案和源审，待主协调准确调用点/PDB结果合并后决定实现与验证范围；本文不提前选择或执行尚未冻结的实机观察点。

[源审索引](registry-read-preparation/evidence.json)记录三个原始来源指纹；[固定11字段契约](registry-read-preparation/official-field-contract.json)和[带原行号的观察器摘录](registry-read-preparation/observer-scope-excerpts.txt)均明确为源码派生材料，不是新的实机注册或调用日志。没有新增语言、依赖、公开契约或产品逻辑；仅本任务交接材料变化。
