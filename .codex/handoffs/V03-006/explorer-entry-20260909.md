# Explorer实际入口与Desktop根绑定对照

状态partial。Windows图片隔离既有测试未重跑，本轮只观测原生Explorer并增强test-only Probe.cpp。源码提交 `47806297fe6989e3cd04e83e692f78ae7ed4bce2`；未修改DLL、GUID或注册语义，未进入生产Shell/Host IPC，未关闭G1..G4。

## 已知有效入口与真实失败

用户明确重新连接后，computer-use实际读取、键盘输入和导航可用。此前测试窗口2229954按新鲜app/title/UI确认身份；裸 `::{4FF8301D-2E73-4D49-9FE5-868D5F1EA302}` 提交后显示“Windows找不到…请检查拼写并重试”。错误后注册readback仍两键true。该轮错误模态与窗口已关闭，用户原窗口1247028保留。没有将无trace本身当作未加载或根因证明。

下一轮使用新鲜自有窗口1968004。开始时Ctrl+N和launch的窗口发现有延迟，几次没有新窗口；未导航用户原窗口，600秒guard也未提前启动。随后前后窗口集合确认1968004为新窗口，真实正控 `shell:Desktop` 于01:28:24.749Z提交并成功显示桌面视图。

该视图包含此电脑、网络、控制面板、回收站、图库等虚拟项目，不能描述为只看到普通物理目录。未读取该GUI视图的实际IShellFolder parsing-name/PIDL，故其精确Shell身份证据仍限于可见虚拟内容与正控导航。

注册完成时间01:29:25.796123Z，读取64位合并HKCR确认Inproc指向本任务attempt4 DLL、ThreadingModel=Apartment、Attributes=0xA8040000；DLL hash仍为旧不可变0174db9b…，没有更换接口试错。

F5后Desktop仍32项，顶部与下部UIA列表覆盖item ID0..31，未出现AssetLibrary集成验证项，因而没有可真实双击的自有item。仅保留数量/覆盖范围，不归档用户桌面文件名。

完整 `shell:::{4FF8301D-2E73-4D49-9FE5-868D5F1EA302}` 于01:32:02.001Z提交；实际独立对话框1443954显示“该文件没有与之关联的应用来执行该操作…”。[错误截图](explorer-entry-20260909/uri-error.jpg)仅含此对话框。01:32:02.369994Z与01:33:48.041256Z的即时readback均两键true；01:37:12.385120Z检查Desktop发现时仍为true，排除了600秒watchdog提前清注册的混淆。

导航错误时Explorer PID6284/17944/24644/24772/26128均Session2，逐进程模块查询均成功，均无任何路径的AssetLibraryExplorerProof.dll；没有打印其他模块路径。当前目录没有trace不能单独证明根因，但本次模块快照降低了“缓存旧DLL、只检查新日志位置”的可能性。没有观察到实际Explorer factory、DefView或IPC。

root另行只读核对这些Explorer进程的用户SID均与执行用户相同，未发现注册到另一用户的差异；本窗口没有重复该检查。

## 有区分力的根绑定对照

Probe.cpp新增 `--root-bind`，顺序严格为SHParseDisplayName → SHGetDesktopFolder → Desktop.BindToObject(root) → CreateViewObject，无显式CoCreateInstance前置。仅此模式stdout无缓冲，外层十秒终止仍可保留最后完成阶段；拒绝成功HRESULT却返回空PIDL/view。旧模式也在parse失败/空PIDL时提前停止，避免空PIDL属性调用。

最终相同DLL的注册正控01:49:06.193627Z：RootParse、GetDesktopFolder、DesktopBindRoot、RootBoundCreateView均S_OK，exit0、stderr空。自己的trace记录probe进程进入DllGetClassObject/Factory/Initialize/DefView。

卸载负控01:49:06.236744Z：RootParse=80070057、PIDL absent，exit1；不继续Bind/View。最初01:43的成功/失败结果也保留在.runtime，最终证据使用加入无缓冲后的产物。Probe SHA256为90FCC911D6569B2D48EBB70792343413C58E48A9C62D7B399E6CC4E40F164E1C。

这排除了“独立控制必须先CoCreate才能绑定”的未覆盖点，不能替代真实GUI成功。当前矛盾仍是独立Desktop根绑定成立，真实Explorer的Desktop发现/完整URI失败；不得据此擅自改变DLL业务接口、Open verb、GUID或注册位置。

## 清理与验证

attempt4 guard在600秒内由本任务主动停止，退出1；显式unregister/verify确认两键false，记录清理时间01:37:37.889288Z。自有错误模态1443954、Desktop窗口1968004、同轮出现的URI派生窗口1312652已按新鲜状态逐一关闭；最终list_windows只剩用户原窗口1247028。没有重启Explorer、改HKLM/UAC/全局策略/loopback。

MSVC目标构建 `/W4 /WX /analyze` 通过，root-bind正负两个控制均通过预期断言，diff检查通过。没有重跑未变化的CRT、图片隔离、网络或全仓库业务套件。原始时间线、注册/模块读回、root-bind结果和安全trace见[证据索引](explorer-entry-20260909/evidence.json)。

下一步由主协调结合实际Explorer发现层/策略路径裁决，不再无观测重复注册。GUI Desktop的精确parsing-name尚未取得；通用Provider与各版本门禁保持不变，Windows独立图片成功不等于原生Explorer首版完成。
