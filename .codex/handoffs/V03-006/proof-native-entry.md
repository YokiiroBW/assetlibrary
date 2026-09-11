# 自有4FF test-only proof：原生进入与卸载闭环

本项目自身的最小入口已实际验证：从虚拟Desktop发现并一次正常GUI双击进入，新窗口活动类为`4FF8301D-2E73-4D49-9FE5-868D5F1EA302`，22字节目标根PIDL binary/canonical均相等，真实视图与CUA均为1项“示例资源库”。原生注册/清理路径得到验证，但这不是完整Windows客户端、真实资产列表或G2..G4完成。

## 原始失败与最小修复

最初获准独立verify在登记前拒绝：外部进程17820的父Explorer/同用户/会话/创建顺序等已正确，但self token仅QUERY，WindowsPrincipal成员查询抛SecurityException；OrdinaryUser默认false不是提权证据。原verify-result.json保留。只读对照显示权限8失败、权限10成功；bafd144仅为self token增加TOKEN_DUPLICATE，parent仍QUERY，所有准入条件不变。当前包内执行器修后查询成功且普通用户，但仍因父来源非Explorer被拒绝，未以NoPackage绕过边界。

同批增加probe原始流与exit/timeout留证、guard计时起点在register调用之前，以及包外只读HKLM两根冲突前置；9个HKCU字段没有增减。原notification只补STA/COM初始化和失败传播，原4FF字段、事件/顺序/flags及C++ DLL未改。

## 实际执行

| 阶段 | 证据与结果 |
| --- | --- |
| 新broker | 自有SDK窗口525692/PID13592，creation134336117190102514；系统Explorer、同用户/session、普通令牌、Active/Default及17项核验，未复用已关闭15848/788038 |
| 包外verify-02 | 通过该窗口Document.Application.ShellExecute普通open启动16532，父13592、所有来源条件真；独立COM/PIDL/DefView、重复注册拒绝及finally清理通过 |
| 原始probe输出 | probe4008，exit0/无超时、流完整；新GUID目录stdout/stderr/result已保存；原probe SHA90FCC911…，DLL0174… |
| 冲突与初始缺失 | 包外HKLM同名2根均absent；另包外reader 9字段missing通过，不与HKCU字段计数混淆 |
| 原生guard | 外部进程18328，同一来源路线，15:18:41.9063954Z起算，原期限15:28:41.9063954Z；两HKCU根/原值，登记后9字段包外匹配 |
| 新目标窗口 | PID10088、creation134336135445905668、HWND263622，新建独占；SDK正控17项 |
| 虚拟Desktop前置 | 实际类00021400、2字节根PIDL binary/canonical相等、33项，包含AssetLibrary集成验证；不是仅凭标题或物理桌面路径 |
| 唯一正常进入 | CUA对观察到的Desktop列表项双击一次；实际类4FF、22字节目标根PIDL完全相等、1项，CUA为“示例资源库”；未新增Browse控制器或进入子层 |
| DLL归属 | target10088加载1份proof DLL，强hash仍0174DB9B1B4CCD4925D3A28470930FA6FAEBD4070348CC374A7CB87313E2FD15 |
| 清理前字段 | 另包外reader再次9/9匹配 |
| 同进程注销 | stop后guard18328在15:24:25.2972678Z完成原unregister/通知，无Failure/CleanupError、当前视图干净；该guard进程随后不存在 |
| 独立负控 | 包外新reader9missing；目标返回虚拟Desktop，实际00021400/2B相等、32项，CUA本项目发现项消失 |
| 最终现场 | 目标263622与自有broker525692均关闭；末次按PID读回两个进程仍在、MainWindowHandle0、未见proof DLL。该末次模块快照未单独再次核creation，不扩读为持续卸载保证。原desktop6212未重启，无新debugger/UAC/HKLM写入 |

GUI排他权已归还root，本窗口此后仅归档。没有重跑微软样例或扩展Host故障矩阵。原trace文件仅用于PID/方法调用佐证，其默认HRESULT入口标记不能全当成方法返回成功；actual class/PIDL/ItemCount是主验收证据。

## 来源与验证

正式代码来源ad84644、bafd144。runtime薄guard E581B582…、9字段reader BAF69565…、机器冲突检查317F017E…、run-view9C7588B9…均经root整批审查；只读4FF视图源15E94A2F…/exeFFD3C441…严格Release `/W4 /WX /analyze`通过。新增Desktop模式用CSIDL_DESKTOP及CLSID_ShellDesktop，proof模式强制4FF与1项；不包含导航/注册/网络。原DLL与probe只复制到本次proof-build，未改C++或让trace写旧目录。

19策略/报告案例、2入口接线、实际当前进程令牌查询回归、17现有Shell契约和仓库架构/契约/源码/依赖检查已通过。新流留证的成功路径此次真实执行；超时/捕获故障路径仍为静态检查，不额外制造失败或重跑。通知返回本身不证明所有缓存刷新，已用原生Desktop及入口消失独立负控支撑本周期结果。

[54项证据索引](proof-native-entry/evidence.json)含原始失败、修正只读对照、包外正控、GUI结果、字段/冲突/清理、源与构建差异及probe原始流。6份含profile路径的副本明确标注并分别记录原件/副本SHA；原件和二进制留runtime，不收私人Desktop完整列表/截图或大Procmon文件。准备manifest中的旧hash/状态保留为其时点，新执行以实际源、bafd144及当前结果为准。

## 下一最小接入缺口

`tests/windows-shell`目前仍是无Host IPC的合成文件夹；`tests/spikes/windows-shell/src/AssetShellProtocol.h`只是4KiB/250ms的spike Ping/Pong，明确不是AssetLink业务协议。下一步应由root冻结一个最小的“已授权库根首屏只读快照”接入任务：在进程外AssetHost复用`apps/windows-client/Core/ReadOnlyClient.cs`、`WorkspaceState.cs`及会话/传输组件连接真实Core，再通过获批的有界IPC向Shell提供快照及明确错误/取消。权限继续由Core重新鉴权；Shell不承担HTTP登录、文件访问或重型解码。当前缺少该Host的生产启动/用户会话归属、快照契约与真实Core联调证据，暂不新增协议、生产Shell或宣布完整Windows首版完成。
