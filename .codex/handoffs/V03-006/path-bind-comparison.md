# 相同注册下的CLSID根与实际路径绑定

结论：两条路径在独立进程中都加载原DLL、绑定原CLSID并枚举1个示例子项。实际sandbox绝对路径没有落到普通文件夹类。由此补上旧root-bind只测CLSID字符串的缺口，不能解释真实Explorer为何仍显示空目录；下一步优先获取其实际活动视图身份。没有运行GUI或追加属性/接口变体，G1仍partial。

主协调明确批准此唯一新增判别，保持被测DLL、CLSID和原owner保护注册脚本不变。原DLL SHA256 `0174DB9B1B4CCD4925D3A28470930FA6FAEBD4070348CC374A7CB87313E2FD15`。新的独立探针只存在于`.runtime/explorer-path-bind`，未改产品或tests源码。Release x64、SDK10.0.26100.0、MSVC19.44.35228，实际`/MD /W4 /WX /analyze /permissive- /utf-8`，最终零警告/错误；最初const PIDL数组类型错误C2664已修正，未降低告警。

探针按Parse→Desktop.BindToObject→IPersist.GetClassID/GetCurFolder→最多16项枚举→实际父Folder属性查询执行，每阶段记录PID、GetTickCount64、实际HRESULT、proof模块是否存在及路径是否匹配。无CreateViewObject/CreateViewWindow、ShellExecute或GUI调用。每例使用独立进程，外部10秒期限；只打印类ID/计数/属性，不打印枚举名称或PIDL内容。

同一注册窗口开始15:34:29.8686815Z，两个结果完成时间分别15:34:46.0464701Z和15:34:46.0985662Z：

| 项目 | CLSID根（PID13720） | 实际sandbox路径（PID26268） |
| --- | --- | --- |
| Parse/Bind | 均S_OK | 均S_OK |
| 模块阶段 | Parse前后、Bind前均未加载；Bind后加载且路径匹配 | 同左 |
| GetClassID | `{4FF8301D-2E73-4D49-9FE5-868D5F1EA302}` | 同一原CLSID |
| GetCurFolder | S_OK | S_OK |
| Parse后PIDL字节数 / GetCurFolder字节数 | 22 / 22 | 920 / 882 |
| ILIsEqual返回 | true | true |
| Enum结果 | 1项，末尾S_FALSE，未达16项上限 | 同左 |
| 父Folder返回属性 | `a8040000` | `e0040045` |
| 进程结果 | exit0，无stderr，无timeout | 同左 |

`proof-calls.log`只含两个探针PID的DllGetClassObject、Factory、Initialize及QI记录，与阶段标记一致。新证据表明这两个具体调用在Bind时才出现模块；不推广成所有SHParseDisplayName调用永远不会加载DLL。

必须保留一个限制：官方[ILIsEqual](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-ilisequal)说明是二进制比较。我最初消息误称“语义相等”，已向协调明确撤回。920和882取自不同阶段，未保存Bind后的parsed长度或原始快照；不能据此声称系统规范化、原始字节相等或语义等价。原数值保留，未为此追加试验。父属性差异也仅记录，不认定其导致Explorer分派差异。

wrapper finally创建stop，600秒guard正常exit0；15:34:46.1882132Z卸载两键false，之后verify再次确认。空目录经完整路径、Directory/非reparse及零子项核验后非递归删除。没有操作用户窗口、管理员工具、真实资产或系统策略。

实际构建入口：`cmake -S .runtime/explorer-path-bind -B .runtime/explorer-path-bind/build -G "Visual Studio 17 2022" -A x64`，以及`cmake --build .runtime/explorer-path-bind/build --config Release --target ExplorerPathBind --parallel 2 --verbose`，使用既有VS CMake绝对路径。源、wrapper、原guard、原始结果/日志和hash见[证据索引](path-bind-comparison/evidence.json)。两项独立绑定控制通过，不增加GUI通过数；无新框架/依赖/共享契约，不重跑原有图片/loader/全仓库套件。
