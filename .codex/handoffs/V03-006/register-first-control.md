# 注册先于新Explorer进程的完整控制

本次补完了先前因usage limit中断、只到SDK正控的未完成项。原中断记录不被改写，也不计作此前失败；含/不含hidden父枚举均未重跑。

原F298A352…官方DLL及同一Inproc路径、四根HKCU原字段、owner、正确STA通知均不变。注册于2026-09-10 02:25:12.1141029Z完成，通知实际STA/type0、CoInitialize=S_FALSE、Folder=S_OK、调用/返回/join均成功。之后才通过同一CUA launch方法创建唯一窗口6162274；原生PID24488创建02:25:32.8820494Z，严格晚于注册，不是原用户或Desktop进程。SDK正控成功，随后实际ThisPC类20D04…/PIDL22B/canonical匹配/ItemCount2的正控也已成功。

02:29:31.135Z F5后仍无官方条目。按批准分支仅一次BrowseObject，提交02:29:44.510Z；控制器先确认唯一身份、实际ThisPC父级，外部解析官方子根42B PIDL，仅flags1。实际出现自有“无关联应用”模态39258360，10秒外限终止控制器，没有browse.return HRESULT，不能记为返回S_OK或某个推测失败码。

关闭模态后、原注册仍有效时，实际view仍是MyComputer类20D04…/22B，与官方42B目标canonical不符；目标匹配观察器正确退出2。另对已允许ThisPC的只读匹配成功，ItemCount2。观察器全过程sample=false，目标PID24488在有效期内独立模块快照无ExplorerDataProvider.dll。

stop/finally于02:33:13.0566077Z正常exit0，4个HKCU根及4个HKLM检查全部absent，CleanupErrors空，STA清理通知真实成功。新鲜确认后关闭自有6162274及模态39258360；原1247028和无关Chrome8128618保留，没有操控其他窗口。

该结果说明本轮单独改变“注册完成后再创建测试Explorer进程”仍未修复入口。它没有建立底层共享broker冷启动，不足以认定或排除所有缓存机制；没有追加接口、属性、策略或注册变体。根因仍未定位，G1保持partial。

[证据索引](register-first-control/evidence.json)保留本轮完整时刻、身份、成功通知、SDK/ThisPC正控、F5发现、唯一超时/模态、有效期view与模块、清理及源。观察器/控制器二进制沿用已验证版本，未重建或重跑不变业务套件；只对新记录做交接和hash校验。
