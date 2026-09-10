# Explorer 属性消费路径：固定版本离线证据

当前仅完成离线定位和候选方案复核；尚未在真实 Explorer 读取以下内部缓存字段，不能据此宣布具体根因。

本机 `windows.storage.dll` SHA256 为 `22aff66094747a65ea1b5390e60dd423cf3bfd897781a6347ccf7f6234d0388b`。从其 PE CodeView 取得 `Windows.Storage.pdb`、GUID `FA3BF0701EBD655CD7B3691D3BF1C00A`、age 1，并从 [Microsoft 公共符号服务器](https://learn.microsoft.com/en-us/windows-hardware/drivers/debugger/microsoft-public-symbols)取得匹配 PDB。最终符号处理记录确认 SymPdb、相同 GUID/age、时间戳和映像大小，PdbUnmatched/DbgUnmatched 均为 false；五个目标符号偏移均为零。

符号工具使用独立虚拟会话标识和只读文件句柄，未打开、附加或暂停 Explorer。Visual Studio 自带签名有效的 x64 DbgHelp 只复制至任务 runtime。前几次本地符号初始化返回 error 126，未取得符号，不构成 Explorer 故障证据；最终使用本地匹配 PDB 成功，仍保留非致命 symsrv 加载诊断。[Microsoft 对 DbgHelp 版本和部署位置的说明](https://learn.microsoft.com/en-us/windows/win32/debug/dbghelp-versions)解释了系统版本的功能限制，本文不据此推断那些早期失败的唯一原因。

## 已定位的调用链

| 位置 | 固定版本中的证据 |
| --- | --- |
| `0x1188E0` | `CRegFolder::GetAttributesOf`；`.pdata` 范围至 `0x118DD1`，此前已在真实宿主命中 |
| `0x118A37` | 调用 `0x119098` 的 `CRegFolder::_AttributesOf`，返回位置为 `0x118A3C` |
| `0x11B8F0` | `CCLSIDInfoCache::_LoadValuesFromRegistry`，返回 void；不是 HRESULT 边界 |
| `0x11B993` | 经导入 `SHQueryValueExW` 查询固定字符串 `Attributes`；仅检查 LSTATUS 为零和长度 4，type 参数为空 |
| `0x1194C3` | 消费本地缓存项的 flags；后续从同项 attributes 与输入 mask 合并。缓存命中和部分直接加载路径均可到达此处 |

Loader 在项偏移 `+0x10/+0x14/+0x18/+0x1C` 写入 flags、Attributes、CallForAttributes、RestrictedAttributes；消费函数复制 40 字节项至本地栈。GUID 起点还有将项地址传入 StringFromGUID2 的指令证据。字段解释来自此固定版本的指令流，公开 PDB 签名本身不提供私有结构布局。

没有成功取得属性的分支会使用默认属性；这提供了与已观测 `0/0/0/0x26` 比较的具体假设，尚未证明真实 Explorer 走了该分支。后续还有受限属性清除和其他调用，不能从静态代码单独判断最终动态路径。

## 最小候选观察点

在既有 B41 外层 PIDL 完整匹配的调用内，只增一个固定消费点 `0x1194C3`。记录同 callId 的输入 mask 及目标 GUID 匹配后的四个 DWORD；这些是局部缓存消费快照，不是宿主注册读取的状态、类型或值收据。

必须同时满足：同一个未失效的引擎线程调用；入口保存的输入 mask 与 R14D 相等；GUID 为固定官方 BA16；`RBP=RSP+0xC9`；`[RSP+0x128]=module+0x118A3C`；`RSP+0x410=outer expectedRsp`。偏移来自公共函数的 8 次 push/`0x298` 栈调整和私有函数的 8 次 push/`0xE8` 栈调整。采样字段分别位于 RBP 的 `-0x79/-0x69/-0x65/-0x61/-0x5D`。

新增点也须计入有界总命中预算，模块/代码字节必须在加断点前匹配；异常或线程退出使关联窗口失效，禁止旧窗口重新生效。继续使用原有时限、取消、异常转交和完整断点清理。某些内置项及其他分支可绕过此点；零命中或字段为零均不能证明未读取注册。

根协调及独立 reviewer 已复核上述偏移和分支条件；候选代码、参考模式及真实使用仍须分别验证。下一周期另由 Windows owner 对官方 11 字段、4 Owner 及选定 HKCR 字段逐项只读核对，补齐先前 presence/输出常量的证据缺口；独立 reader 的 HKCR 视图不冒充 Explorer 进程内视图。

[索引](evidence.json)保留原始局部反汇编和工具输出的 SHA256、匹配 PDB 出处、字段约束及限制。PDB、系统库、工具 EXE 和完整导入/`.pdata` 索引仅留任务 runtime；不进入产品或发行依赖。没有改产品代码、共享契约、系统登记或发布门禁。
