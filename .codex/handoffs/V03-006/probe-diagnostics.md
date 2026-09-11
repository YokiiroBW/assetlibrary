# V03-006 — 真实回调链只读诊断

第三冷GUI在正确4FF类/22B根与新DLL上69.258秒仍显示Loading，Host已有Ready；本补丁按Root裁决只提供观测，不改clone、线程准入、刷新预算或Core TTL。真实读取由协调端现有actual IFolderView observer执行，本窗口不注册/操作真实Explorer。

## 读取协议

`tests/windows-shell/ProbeDiagnostics.h`冻结未注册的私有PROPERTYKEY：fmtid `{2F242D38-C686-4E35-87C3-36C9BAF44EFE}`，pid `1`。只在GetDetailsEx的该key+空item时支持（nullptr或有效两字节终止PIDL）；返回v1 JSON的VT_BSTR，最多2048 UTF-16字符。未知key、非空item保持拒绝，没有新增可见列、属性schema注册或AssetLink契约。

先验证JSON的`pid`等于已核真实Explorer目标PID；不符时只能说明执行发生在其他进程，不能据本地重建Folder裁决真实状态。`folder/source/last_clone/cookie`为诊断对应号，不是资产身份；cookie按UInt64读取，避免double丢精度。零表示尚未观测的计数/标识，未调用HRESULT为E_PENDING（十进制2147483658）。无Signal明确返回`view:null`，不能当成成功状态0。

顶层固定字段：`v,pid,tick,folder,source,last_clone,folder_tid,enum_n,enum_tid,enum_status,last_cb_hr,slots,view`。其中source为本clone的来源Folder；last_clone为本Folder最近创建的view Folder；last_cb_hr为本Folder最近创建view时的callback结果；enum_status为最近完成Query的状态，不代表原子事务快照。

view固定字段分组：

- Signal：`cookie,started,published,current,status`。current=1才表示最新generation完成；started/published是独立原子采样，可用稳定复读校验。
- 创建：`cb_hr,cb_slots,cb_tid`。cb_slots是callback创建时名额占用，顶层slots是读取时名额；容量失败也记录HRESULT与4名额，ctor未调用则cb_tid为0。
- 站点：`site_n,site_tid,site_hr,site`。早退之前记录调用线程与最终HRESULT。
- 窗口：`window_n,window_tid,owner_tid,window_hr,reported_hwnd,attached_hwnd`。WINDOWCREATED wParam给reported HWND；owner_tid是该窗口所属线程，跨线程拒绝之前也记录。
- 投递/定时器：`post_n,post_error,arm_n,arm_tid,arm_error,timer,tick_n,attempt_n`。
- Tick调用链：`service_hr,active_hr,getwindow_hr,active_hwnd,match,skip,refresh_n,refresh_hr,detach_n`。HRESULT为无符号32位值；Refresh未实际调用时仍E_PENDING。

skip最后值：0未拒绝/未进入；1无活动timer；2重入刷新；3新枚举未完成；4状态/预算停止；5未到间隔或次数/截止；6活动view/HWND/site/episode/generation复核失败；7服务/view/窗口/Refresh调用失败。计数与last值不构成事务快照，不把最后一条忽略的队列消息当成首次失败根因。

Getter只读取原子数值并格式化固定字段，不调用EnumObjects、Query、QueryService、Refresh或定时器，不写文件、网络、注册表，不持有新COM引用；无名称、路径、SID、凭据、Core token或接口地址。窗口线程查询只在原WINDOWCREATED埋点读取一次公开GetWindowThreadProcessId，用于覆盖原早退。

## 依据与判据

[SetWindowSubclass](https://learn.microsoft.com/en-us/windows/win32/api/commctrl/nf-commctrl-setwindowsubclass)禁止跨窗口线程挂subclass，[SetTimer](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-settimer)要求窗口属于调用线程，[WINDOWCREATED](https://learn.microsoft.com/en-us/windows/win32/shell/sfvm-windowcreated)只规定wParam是view HWND。这些文档没有保证callback构造线程等于后续site/window线程，因此暂不修改thread_=ctor。

执行PID正确且enum_n>0、view:null才是实际无Signal枚举的重要证据；enum_n=0时可能尚未枚举或是重建对象，不能单独认定clone根因。有Signal时先看cb_hr/cb_slots，再看cb_tid与site/window/owner TID及早退HRESULT；attach成功后按post→arm→tick→服务→activeview→Refresh链定位。仍由Root取得一次真实读回后裁决行为修复。

## 验证和交接

严格DLL/诊断目标与全目标Release构建通过；explorer_diagnostics通过原Folder缺Signal、source→clone对应、无副作用读取、异线程早退记录、容量失败记录、未知key拒绝、DLL引用归零及最大值长度。4份样例另以Python标准json验证完整字段白名单和无符号数值，最大998字符，见probe-diagnostics/samples.json。原4项wire/menu/loading/system-callback CTest通过。

本轮没有真实目标读取，未证明或修复冷Loading根因；已有G2最终COM释放反例仍未通过，不因诊断新增而晋级。分支codex/v03-006-windows-explorer-native-integration，合并本交接所在提交后由Root新build/hash并更新reader。技术债V03-006/windows-shell-owner：根因修复及冷GUI复核完成后，由Root决定移除临时PKEY与计数器。

对应号只在同一进程、同一DLL实例内使用；归档样例来自不同测试作用域/静态实现副本，仅用于schema，不作为真实视图关联证据。最终仓库交接/架构/契约/依赖与35项既有回归通过，Alpha保持blocked。
