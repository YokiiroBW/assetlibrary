# MSAA/UIA只读协作诊断记录

2026-09-13。范围为V03-019/V03-021辅助技术生命周期，只读检查与官方资料核对；没有在这些工作区修改文件，没有操作GUI/安装/生产管道。本记录的分流结果由Surface owner实测并通过协作消息提供，不把它们记成V03-020新增测试或已解决回收。

## 已有实测

- 普通MSAA-only `--msaa-retire`：关闭后owner引用正常回到1。
- 创建UIA `ElementFromHandle`后关闭：即使不调用FindAll，仍保留owner=2/providers=1。
- 加入FindAll：同样保留，故子项枚举不是必要触发条件。
- 先Release全部显式UIA对象，再通过仍持有的MSAA代理关闭：仍保留owner=2/providers=1。
- CoDisconnectObject返回S_OK、线程检查通过、旧MSAA代理拒读、窗口实际退休均已通过。三轮保留累计为owners=[2,2,2]/providers=3，30秒后不退；服务端STA CoUninitialize后回到[1,1,1]/providers=0。
- 真实选择状态正负对照通过：MSAA选中0x300002、未选0x300000；UIA Legacy状态相同；系统SelectionItem.IsSelected为选中1、未选0。这证明选择状态通道正确，不能据此宣称关闭资源已经回收。

## 当前判断与边界

触发点已缩到UIA bridge element创建，普通MSAA路径可回收。客户端代码显式Release并已退出，弱provider identity不形成强引用环；服务端公寓结束才回收更支持COM/桥接内部引用保留。尚未证明具体为TABLESTRONG票据，不能据此操作系统私有marshal数据或强制Release。

[CoDisconnectObject](https://learn.microsoft.com/en-us/windows/win32/api/combaseapi/nf-combaseapi-codisconnectobject)只承诺断开远程进程连接；S_OK不是所有桥接/本机引用归零的证明。[LresultFromObject](https://learn.microsoft.com/en-us/windows/win32/api/oleacc/nf-oleacc-lresultfromobject)会建立引用，当前工厂初始引用的Release配对未见明显遗漏。

后续验证应有明确边界：记录自有provider的AddRef/Release编号、计数、线程与固定模块栈，辨别最后引用来源；如评估替代方案，只做一次能控制对象身份与精确断开的可行性验证，不去除owner pin或放宽回收断言。

[IAccessibleEx官方方案](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-usingiaccessibleex)需要IServiceProvider、IAccessibleEx、每个简单子项的独立对象和IRawElementProviderSimple；仅增加身份接口不构成回收机制。若系统桥接引用无法按窗口受控解除，原生UIA provider应复用同一AccessibleModel/选择/激活，并依[UiaDisconnectProvider](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcoreapi/nf-uiautomationcoreapi-uiadisconnectprovider)对自有准确对象解除。不得在SendMessage上下文调用，退休期间不得重入返回同一provider；也不应用全局DisconnectAll扰动Explorer其它视图。
