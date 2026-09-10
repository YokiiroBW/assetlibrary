# 同条件双来源与枚举顺序属性对照

主协调审查后仅执行一次双模式流程。两个独立进程均在同一原样F298/323CD843四根注册窗口内完成，各自新建一个MyComputer父对象、外部10秒上限，无GUI或调试附加。A enum-first（PID18668）完成20次，B parse-first（PID18700）完成26次；均exit0、无超时、stderr为空，46次HRESULT全00000000、输出均有效。

两模式的枚举child与解析child都是22字节，完整字节比较相等；所有前后SHA256均为 `951c3bc3e37630ba023015d1294c0cd7a398bbfa383d04c7d59b7419e8d893db`。B在枚举前六次查询前后及枚举后也保持相同指纹。没有解码或保存其他项目的私有载荷。

## 结果矩阵

paired阶段每个mask均执行enum→parsed和parsed→enum两种顺序。下表合并完全相同的mode、source和顺序输出；每个实际callId、次序、输入、输出、HRESULT、有效性及tick保留在[46行完整矩阵](attribute-pidl-control/query-matrix.json)，未丢弃任何不一致行。

| mode | phase | source | input | output | HR | 对应调用数 |
| --- | --- | --- | --- | --- | --- | --- |
| A、B | paired | enum、parsed（双顺序） | 28180000 | 20000000 | 00000000 | 8 |
| A、B | paired | enum、parsed（双顺序） | 20000000 | 20000000 | 00000000 | 8 |
| A、B | paired | enum、parsed（双顺序） | 40418000 | 00000000 | 00000000 | 8 |
| A、B | paired | enum、parsed（双顺序） | 40000000 | 00000000 | 00000000 | 8 |
| A、B | paired | enum、parsed（双顺序） | 2044007F | 20000024 | 00000000 | 8 |
| B | pre-enumeration 1 | parsed | 20000000 | 20000000 | 00000000 | 1 |
| B | pre-enumeration 2 | parsed | 40418000 | 00000000 | 00000000 | 1 |
| B | pre-enumeration 3 | parsed | 40000000 | 00000000 | 00000000 | 1 |
| B | pre-enumeration 4 | parsed | 2044007F | 20000024 | 00000000 | 1 |
| B | pre-enumeration 5 | parsed | 28180000 | 20000000 | 00000000 | 1 |
| B | pre-enumeration 6 | parsed（重查） | 20000000 | 20000000 | 00000000 | 1 |

此独立流程中，改变PIDL来源、paired查询先后及是否先枚举均未改变结果，输入也未变化。此前真实host的20000000→0、2044007F→26仍与本轮不同；不能将新进程等同共享系统缓存冷态，也不能据此认定具体宿主根因或擅改Folder属性、注册字段或PIDL。没有追加变体，G1仍未关闭。所有记录的sampleLoaded均false，工具没有显式Bind样例。

## 验证与清理

最终源SHA `96EB432CA0EC5E10D1069519C02FA61427E7475FA616B4642D504328F3A18D83`；EXE SHA `DB0700310571C6540E3DFE50CB2DCB718C3AE1E63D1CA25B365A04A5DB411E4C`；wrapper SHA `3A7362EB3A9FD1346C75382260F325283E183D0AB902998DBD8172A22BEC4F90`。原四根guard及官方DLL未改。

最终Release x64 /MT /W4 /WX /permissive- /analyze构建通过；准备阶段首次deleter指针限定符告警修复后通过，旧失败日志保留。PowerShell AST和无副作用validate通过。主协调完成最终审查后执行一次 `pwsh -NoProfile -File .runtime/explorer-attribute-pidl-control/run.ps1 -Action run`，wrapper对A/B查询顺序、HRESULT有效性和前后指纹的读回校验通过。

每个probe前后都核对原4CU根Present及精确Owner、4LM absent和guard有效期。finally正常stop，guard exit0/stdout和stderr为空，05:43:22.9770273Z最终8根均absent、双通知成功、两probe退出已确认。GUI动作0、附加0，未操作原窗口。枚举最多保存128个单child、每个≤16KiB，最多额外一次Next判定终止；内存及调用数有界，不涉及真实资产或50万资产列表。

复用既有父级公共Shell查询、Browse固定绝对解析路径、323CD843 owner guard与F298 DLL；仅临时诊断工具及本任务文档，无产品代码、依赖方向、权限模型、关联配置或发布门禁变化。[证据索引](attribute-pidl-control/evidence.json)封存运行原件、准备源码/脚本/构建记录和完整结果矩阵；其中准备README按执行前原样留存，其“未执行”仅描述准备时点。
