# 固定官方样例的受控运行

官方样例未成功成为可用Explorer入口。有效注册期内，补正并实际完成MyComputer通知后，ThisPC仍未发现样例项。此前唯一原生打开产生“无关联应用”模态并触发10秒超时，但它发生在通知补正之前，不能说成“正确通知后的打开也失败”。根因尚未证明，转向宿主/部署条件，未修改原样例或原proof的接口。

## 原件与范围

V03-005提供18份固定Git blob源码的原样构建，revision434f6002bdf9cf9829406c3ff2b33387982d6168，DLL SHA256 F298A352439C6209D274B567081191819BD55A45BC2F63A025EE28FC69C42E96/186880B。本任务核对并复制原DLL；没有调用DllRegisterServer、PSRegisterPropertySchema或任何写交互。原样例没有本任务Factory日志，未伪造或据日志缺失作结论。

按明确批准仅复现四个HKCU根：根CLSID BA16CE0E-728C-4FC9-98E5-D0B35B384597、菜单CLSID BB8F539D-3B97-4473-9E07-C8248C53248E、FolderViewSampleType及MyComputer/NameSpace/根CLSID。原Attributes A0000020、Apartment、shell32.dll,-42和菜单关联不变；只增加本任务owner元数据，禁止Delete/rename等示例写交互。HKCU/HKLM64八项临写前均absent；RegCreateKeyEx disposition拒绝已有根/竞态，owner及完整节点/值/类型检查后才逆序删除。HKLM只读，未改系统策略或UAC。

独立官方观察器沿用E0身份/64项枚举上限/唯一frame及PID创建时间检查，输出仅类ID、路径匹配、计数、HRESULT和样例模块标记。固定SDK/ThisPC/官方MyComputer子根白名单，外部10秒期限。原E0产物未覆盖，官方观察器和fallback控制器均严格Release x64 /W4 /WX /analyze构建通过。

## 注册前正控与实际发现

新自有窗口88280002/PID36396，创建16:42:17.0347466Z；原用户窗口1247028保留。SDK正控为系统FS类、17项。第一次点击ThisPC遇CUA geometry unavailable，未导航；thispc-before-registration.json准确返回SDK/373B、与预期MyComputer不符、exit2。原记录保留，不作样例失败。

随后通过已核对的MyComputer CLSID地址栏进入ThisPC。thispc-baseline.json于16:44:24Z全链成功：类20D04FE0…、PIDL22B、canonical相等、2项；这是注册之前的有效正控。16:44:46.9562607Z开始600秒注册，四HKCU根均正确owner，HKLM保持absent。原guard记录NotifyDrivesCompleted=false，没有被当成通知成功。

ThisPC于16:46:15读取和16:48:28 F5刷新后仍2项，无FolderView SDK Sample。按批准的缺失分支，仅一次原生BrowseObject：16:49:07.778Z，当前view严格匹配ThisPC，外部解析官方MyComputer子根得到42B PIDL，flags仅SAMEBROWSER|ABSOLUTE。调用阻塞于自有explorer.exe“该文件没有与之关联的应用”模态；控制器10秒到期结束，没有browse.return HRESULT，不能记S_OK。模态6227256实际文字已记录并关闭，未再次Browse。目标模块快照未见样例DLL，但该快照在通知补正之前。

## 通知辅助缺陷与范围修正

原脚本为new Thread显式CoInitializeEx(STA)，却未在启动前SetApartmentState。独立通知诊断（外部10秒，每线程join3秒）于16:52:44完成：

| 模式 | 实际apartment / CoInitialize | Folder查询 / UPDATEDIR |
| --- | --- | --- |
| 默认线程 | MTA/type1，80010106 | 未到达；Notify未调用 |
| 显式STA线程 | STA/type0，S_FALSE | GetSpecialFolderLocation=S_OK，原MyComputer UPDATEDIR实际调用并返回，2ms |

这确证辅助脚本模式的缺陷，不是样例本体根因。原guard/原false/原fallback timeout均保持原字节。16:53:16.338Z，在成功STA通知之后且注册尚有效时，fresh ThisPC依旧2项，无官方项；该原WindowState另存discovery-within-registration-after-sta.json。有效区间没有再取目标模块快照，也没有再尝试打开，因此这两项缺证据明确保留。

16:55:55的F5和E0已晚于guard清理，它们仅表示清理后的ThisPC状态，不能用于官方发现/加载结论。文件名中的after-successful-notify不代表仍在有效注册期；原时刻和final-cleanup中的LateRefreshAndObserverExcluded明确排除。

为后续复用准备了guard-explicit-sta-prepared.ps1，仅加入启动前STA设置；它没有被运行或再次注册。修复依据是已实际通过的独立STA通知步骤，不声称新guard整条运行已通过。

## 清理与结论

原guard于16:54:47.1247024Z自然到期，exit0、CleanupErrors空，四HKCU根及四HKLM检查全部absent；stop文件16:55:55才创建，未把它描述为主动触发。原guard清理通知仍false，随后16:58:50显式STA补原UPDATEDIR成功。自有88280002及模态6227256均关闭，最终只保留原1247028和Codex。未创建任何物理示例资产目录。

只对本次自己PID36396做创建时间匹配后的启动元数据读取，命令为explorer.exe /factory,{75dff2b7-6936-4c06-a8bb-676a7b00b24b} -Embedding；它只是宿主启动形态，未认定为根因或据此运行新变体。

[原始索引](official-runtime-control/evidence.json)含注册前失败与成功正控、完整guard、两阶段通知、唯一超时/实际错误、有效期内发现及晚到读回、清理、观察器源和hash。没有把官方样例的父级/属性/实现差异混成单一变量，也不把被缺陷影响的首段当成完整正确的官方运行。G1仍未关闭；下一步由主协调针对宿主/部署条件裁决，不盲改接口或注册策略。
