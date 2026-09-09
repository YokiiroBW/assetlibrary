# Explorer 限时诊断执行记录

2026-09-09：真实资源管理器的 Desktop、URI、文件夹 CLSID 入口及同源码静态 CRT 对照均未观察到扩展激活。独立根绑定成功，当前根因未确定。全部自有测试窗口、空目录和两项 HKCU 注册已清理，用户原窗口保留。

下一步已准备为一次 **60 秒 Windows ETW 跟踪**。这是需要管理员令牌的系统诊断，原测试窗口操作授权持续有效；用户随后明确回复“允许”，该项授权已经取得；现在按原范围执行，不再询问同一权限。

实际工具位于 `C:/YOKI/Codex/AssetLibrary-worktrees/V03-005/.runtime/explorer-etw-owned/`，完整源码、运行命令、清理步骤见该目录 README.md；manifest.json 固定19项源码、证据、计划和运行产物的原始 SHA256。root已逐项重算通过，并实际执行只读 `--check-plan` 确认当前5个Explorer的PID/创建时间仍匹配。当前DLL为97,280字节，SHA256 `d5a6b73e03f70c88f36c259089b194b9059f79a34eb42cc161350237e2f42e8c`。

记录范围限于选定进程、本任务 CLSID、proof DLL及明确运行库的相关事件，只持久保存匹配标识、模块名、事件/进程/时间和数字状态。不保存未筛选ETL、原始载荷、注册表值或用户路径列表。PID筛选不受支持、目标退出、格式变化或丢事件时失败，不退回全局采集。

采集器在匹配的只读注册访问事件实际到达后才发出 ready。独立守护在超时/异常时停止本会话；它校验真实父进程、创建时间、固定dotnet路径，并拒绝所有被观察的Explorer PID，只可能结束自己所属的采集器。停止/查询先于日志写入，日志故障不得阻止清理。正常采集60秒，故障回收最外层期限90秒；操作系统停止失败必须记为未确认，不能凭退出码宣布清理成功。

当前仅完成零告警构建、49项ABI检查、12项合成事件解析检查，以及新增的9项守护身份/只读父进程检查。真实PID筛选、事件投递和会话停止尚未执行。COM事件只覆盖固定CLSID的class-not-registered；Image事件只覆盖成功加载/卸载，不能把无事件当作没有尝试加载。

执行顺序：获得本项授权；重新核对自有窗口/进程并按需刷新过期计划；由Windows窗口启动已有有期限注册；启动此采集器，等待ready后仅做一次GUI入口；确认采集器与守护结果及精确会话不存在；最后清理注册和测试窗口。不会更改系统策略、安装驱动、重启用户Explorer或修改资产文件。

当前计划 `0851cd0f117747749ec1dc300dbf8e20.plan.json` 有效至03:25:05 UTC。过期后由协调者以同一已审查工具重新读取当前进程生成新计划，不能沿用旧PID/时间或旧二进制哈希。Windows安装包、G1..G4和完整版本仍未完成；NAS内部图片解码架构的另一项待决策也保持独立。

## 已授权后的实际执行

用户明确允许后，首轮计划10e5e388实际Start/Open/三次Enable均返回0，consumer收到固定CLSID注册读取正控并发出ready；随后一条未识别来源的PID归属事件触发严格停止，原始payload未读取/保存。root在04:27:52Z独立确认会话4201/absent、采集器和guardian均退出。GUI入口未提交。原始记录见explorer-first-live-trace/evidence.json；Windows两轮准备/清理记录见V03-006/explorer-etw-preparations.md。

当前ae1c1ee1工具仅补异常的provider/source/event ID/version/header PID诊断，不改接纳规则或读取越界payload；零告警构建和2项针对检查通过。历史d5a6b73e的19项源/产物已冻结；新版本20项manifest由root核对并冻结于.runtime/explorer-etw-owned/history/ae1c1ee1。第二轮计划083be90e的系统权限启动于04:41:11Z返回“操作已被用户取消”；无collector/guardian日志，root独立status仍4201。两轮测试窗口、空目录、注册已清理，实际GUI入口提交总数0。

现停止自动重试UAC，用户原授权保留；待用户能处理Windows提示再刷新过期计划继续，不再次询问同一授权。不能把首轮异常推定为Image provider或放松PID边界。原准备阶段的“未执行”说明保留为历史，实际首轮证据以上述记录为准；修正后的诊断尚未实跑。

## 用户回复继续后的范围收敛

第三轮bc7c7ebf真正识别Kernel-Registry event4/version0/headerPid10716来自非选定进程；读取payload之前即停止。root独立确认会话4201、collector28708/guardian26460退出，GUI未提交。公开PID/V2/SystemProvider契约不能提供所需内核筛选保证，已停止这条配置，见explorer-kernel-pid-reproduction/limitations.md。

当前改为更窄的用户态候选37e984dd：只启用原COM ClassNotReg、本机User-Loader 3/8/10加载失败和明确属于本工具的私有控制事件。没有两个Kernel来源，不作全局缓冲后筛选；PID保护、60秒和guardian保留。零告警、61 ABI/17解析与metadata/2头部检查通过，root核对20项manifest。源码及产物在.runtime/explorer-etw-owned/user-mode，已冻结history/37e984dd。私有控制只证明采集链路，不代表Microsoft事件覆盖所有失败。

计划74a6db21的Windows启动在12:24:06Z返回取消，用户态采集器和guardian均未开始，root独立status4201；第四轮现场清理。此前四轮未提交GUI入口。当时核对.NET Host提示可见性的问题，已由用户随后要求一次确认复用及实际成功启动取代；不再重复询问原授权。

## 用户要求一次确认

用户两次提出“如何一次性授权，不用每次点”。专用管理员诊断会话已实现并实测：13:15:58Z由Windows一次确认启动，期限至13:45:58Z（北京时间21:45:58）。同一会话完成两次固定37e984dd观察器60秒捕获和状态查询，后续无新UAC；14项离线检查、零告警构建、13项manifest核对通过。仅允许capture/status/stop受限计划，原guardian与清理规则保留；不改UAC、不创建服务/任务、不接受通用管理员命令。固定输入锁定，已安装SDK仍是既有可信前提。用户关闭控制台或期限结束即停止接受请求；本记录保存时控制台仍留给用户，到期清理不得预先记为已验证。nonce和请求队列未归档。见single-consent-session/evidence.json。
