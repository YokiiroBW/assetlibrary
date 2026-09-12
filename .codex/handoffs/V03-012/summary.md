# V03-012 交接摘要

状态：ready_for_review。分支 `codex/v03-012-explorer-fault-harness`；实现commit `d3b2ba34cf0f991987630978d1e6faae8aebaf6d`。协调线程已将实现合入root为 `88e6c21`。本任务仅交付故障工具，不表示真实Explorer G2、G3、G4或完整Windows首版完成。

## 交付与命令

新增ExplorerFaultHost.cpp和ExplorerFaultHarnessTests.cpp，登记tests/windows-shell的CMake/README；其它修改仅任务包和三份handoff。没有变动现有Shell DLL/客户端行为、AssetHost、契约、注册工具或门禁。

```powershell
& .runtime/explorer-fault-harness/Release/ExplorerFaultHost.exe --execute --mode silent --lifetime-ms 30000 --max-connections 32
```

mode支持silent、invalid-version、partial-frame、crash和ready。无execute仅dry_run；执行必须指定mode。lifetime范围1..119000ms，另最多1000ms确认取消，独立监护线程约束包括日志阻塞在内的总驻留不超过120秒。连接总数默认32/上限128，单实例/单并发，每连接500ms。停止事件为 `Local\AssetLibrary.ExplorerFault.Stop.<PID>`，同TokenUser DACL且拒绝复用已有事件。精确启动、等待listening和finally停止见[README](../../../tests/windows-shell/README.md)。

| mode | Shell状态 | CLI证据 |
|---|---|---|
| silent | Unavailable (2)，约150ms | request_received，无响应 |
| invalid-version | InvalidResponse (5) | response_written，40B，version=2 |
| partial-frame | Unavailable (2)，约150ms | response_written，20B，声明24B body但仅写4B body |
| crash | Unavailable (2) | request_received后expected_crash，exit 0xE0000012 |
| ready | Ready (0)合成空根；非根Expired (4) | 新随机epoch，实际request-id回显 |

crash只自终止该故障进程、不弹WER；它模拟突然进程退出，不声称触发真实Host内部异常。JSONL仅含mode/pid/client_pid/requests/elapsed_ms/response_bytes/event，不输出请求正文、token、SID、资产名、路径或凭据。client_pid取自实际连接，核session/PID失败即拒绝；GUI验收必须与已核验Explorer PID匹配。response_written只证明管道写入完成，不等于客户端读取完成。

退出码：0=正常stop/deadline/connection_limit或dry_run；1=初始化/I/O失败；2=参数拒绝；3=端点无法创建/已占用；70=取消或监护期限（可能无末尾日志）；0xE0000012=预期自终止（PowerShell为-536870894）。只有listening表示就绪；在途连接时其它请求可能Busy (6)。

## 复用、文件安全与规模

复用冻结snapshot-v1、PipeName、Request形状校验及现有Query，沿用SnapshotTests的有界OVERLAPPED取消/读后关闭模式。极小test-only帧构造和句柄管理留在测试文件，未扩展生产库。不复制Core业务权限/路径/身份/写入逻辑。仍为C++17/Windows SDK/CMake，无新语言、框架、运行时、依赖、迁移或契约变化。

真实TokenUser SID单ACE保护DACL、拒绝远程、同session/PID检查和first-instance维持本地信任边界。不提前释放未确认取消的OVERLAPPED；超时只终止自有故障进程。不读profile/资产，不访问Core、网络或注册表，不操作GUI。50万资产成本与零资产相同：不枚举不索引，48B请求/40B响应，一个pipe实例/I/O和一个监护线程，至多128连接和有界JSONL。

## 验证、风险与合并

严格Release构建、explorer_snapshot和explorer_fault_harness通过。协调审查补响应完成字节数后，仅重建新增目标及复验fault_harness，1/1通过；snapshot源码/输入未变未重跑。仓库架构/契约验证通过，Alpha保持blocked。详细命令和已知首轮编译错误见[tests.md](tests.md)。

不同SID/session及远程拒绝未跨账户实测；已有实际DACL检查、同身份成功链路及代码约束。日志堵塞/OS取消不完成的监护分支未注入。G2真实窗口故障状态/交互/重新授权恢复、G3保留COM对象增长、G4循环/8小时证据仍独立验收。G2不要求关窗时所有COM引用立即释放。没有新增产品技术债或生产启用。

实现已由root合入88e6c21，下一步只需合入本handoff提交；再由协调者独占pipe按原流程完成真实G2。本线程不调查历史COM/GUI环境问题。交接时自有运行资源为0：故障Host/测试进程均退出，管道已释放，无注册项或GUI窗口。
