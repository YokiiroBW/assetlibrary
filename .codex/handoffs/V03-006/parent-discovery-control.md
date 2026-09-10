# 独立父级发现与未完成的进程时序控制

两个标准父级查询都能发现官方样例：含INCLUDEHIDDEN和不含它时均完整枚举3项，父CompareIDs匹配官方目标1项；对匹配项请求FOLDER/BROWSABLE/HIDDEN/NONENUMERATED，实际返回20000000，仅FOLDER。不能以hidden过滤解释此前真实ThisPC的2项；仍未证明宿主具体根因。

含hidden控制于2026-09-09 17:36:58Z执行。发现进程24308先绑定stock MyComputer，枚举完毕保存3个PIDL/76B，再解析官方目标并用父CompareIDs比较，无比较失败，样例模块始终未加载。未先CoCreate/Bind样例污染发现。另一新进程15936只做直接Bind/GetClassID正控，返回官方BA16…类，DLL在Bind阶段才加载。两进程均exit0、无stderr/timeout；每进程外部10秒，枚举最多128项/每PIDL最多16KiB，无其他项名称或路径输出。原STA guard正常清理，通知成功，GUI操作0。

随后在另一原样注册窗口中，普通FOLDERS|NONFOLDERS查询由新PID33260于17:49:47Z运行，仍3项/匹配1项/比较失败0；匹配项属性查询S_OK，HIDDEN/NONENUMERATED均未返回。其原件和源单独保留，不覆盖含hidden查询。探针最终严格Release x64 /W4 /WX /analyze构建通过，未改官方DLL或注册字段。

同一轮准备了唯一“先注册后新Explorer”的GUI控制：注册17:45:44.1920424Z，PID35732创建17:49:47.9881482Z严格更晚，唯一自有窗口2885792，SDK实际view正控成功。但随后CUA激活失败且长turn遇usage limit中断，**ThisPC正控和目标导航均未发生**。不能把未完成步骤计作失败，也不能据新PID声称底层broker冷启动。

guard已于9日17:55:44附近自然到期清空4个HKCU根，STA清理通知成功。10日恢复时重新核对PID35732精确创建FILETIME、原生HWND与SDK标题/17项，确认仍属本任务才关闭旧窗口；原1247028及无关Chrome保留。新鲜窗口清理记录和恢复边界已保存。

[原件索引](parent-discovery-control/evidence.json)包括两个不同枚举场景、独立绑定正控、源/构建/guard、未完成时序准备和恢复清理。下一步仅重建并完成未发生的时序GUI控制，不重跑这些已完成查询。产品入口仍未完成，未改写旧E0/官方对照结论。
