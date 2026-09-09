# 单次静态CRT真实入口对照

结论：test-only `/MT` DLL的独立Desktop根绑定成功，但相同沙箱文件夹CLSID入口仍显示普通空目录，未观察到Explorer模块加载或Factory日志。此次结果不支持将静态CRT作为实际入口修复；也不能仅凭Explorer预载`*_APP` CRT就声称发生冲突。G1..G4保持开放，结束本轮DLL/注册试探。

## 对照来源与唯一编译选项

本次由V03-005主协调明确批准，先完成独立根绑定再执行唯一一次真实GUI入口。独立目录`.runtime/explorer-proof-mt-control`从当前仓库源完整构建，仅传`-DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded`，没有修改CMakeLists、GUID、接口、SDK或生产默认候选。动态原DLL0174DB9B…和静态新DLL37AD9B0ED4B692C593F0974F0CD1DE884AABE680CA5949699C14EE842B8C0B31分别保存在自有`.runtime/explorer-live/20260909-mt-control/dynamic`和`gui`目录，没有覆盖原产物。

当前ExplorerProof.cpp/def原始字节与V03-002工作树因CRLF/LF不同而hash不同，换行规范化后完全相同；逐项hash见source-comparison.json。不是未披露的源码修改。生成的Release ClCompile属性逐项比较，唯一差异为RuntimeLibrary从MultiThreadedDLL改为MultiThreaded；实际verbose CL命令包含`/MT`、`/W4 /WX /analyze /permissive- /utf-8`。SDK10.0.26100.0、MSVC19.44.35228.0不变，构建零警告/错误。

静态DLL实际导入仅ole32、OLEAUT32、SHELL32、SHLWAPI、KERNEL32；普通MSVCP140/VCRUNTIME140/UCRT不再作为直接依赖。动态DLL完整导入表同时保留。没有把同名或不同名CRT的预载状态等同于实际失败原因。

## 实际结果与清理

继续使用原SHA90FCC911…的动态probe EXE，未同时更换观察器运行库。02:23:34.2767171Z，独立STA进程23252的RootParse、GetDesktopFolder、DesktopBindRoot、RootBoundCreateView均S_OK、exit0、stderr空、未超时；十秒外层期限与finally适用。root-bind专属DLL副本日志只归属该probe。02:23:34.2969401Z卸载确认两键false。

按新鲜窗口集合确认1575786为自有Ctrl+N窗口。GUI使用另一份相同hash静态DLL以区分日志来源，02:24:33.2900684Z注册成功、600秒guard开始。02:25:19.328Z在该窗口实际输入与前一动态对照完全相同的sandbox FolderName.{固定CLSID}绝对路径；02:25:40.160Z仍显示“此文件夹为空。”和0项，见[裁剪原始视图](explorer-mt-control/gui-empty-view.png)。

02:25:40.6438207Z注册两键仍true；五个Explorer的模块查询均成功且无任何路径的proof DLL，GUI副本旁无trace。独立probe的日志没有被计作Explorer证据；没有开展动态反控或第二次GUI注册。

stop文件触发guard正常exit0，02:26:09.4046591Z finally两键false。自有1575786经新鲜确认关闭，最终list_windows只剩用户原1247028及Codex。空目录经完整路径、Directory属性、无reparse、零子项核验后非递归删除。无HKLM/UAC/策略/用户进程重启或真实资产操作。原始日志、编译工程/缓存/选项比较、导入表、时间线、截图和清理记录见[证据索引](explorer-mt-control/evidence.json)。

## 命令与后续

```powershell
cmake -S tests/windows-shell -B .runtime/explorer-proof-mt-control -G "Visual Studio 17 2022" -A x64 -DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded
cmake --build .runtime/explorer-proof-mt-control --config Release --target AssetLibraryExplorerProof --parallel 2 --verbose
```

实际使用已发现的VS2022 CMake/dumpbin绝对路径；dumpbin的`/dependents /imports`输出完整归档。独立root-bind十秒控制和GUI六百秒guard源文本均随证据保留。一次新root-bind正控通过，真实GUI入口失败；不重算之前loader/root-bind/图片矩阵，不重复全仓库或业务测试。

归档保持原始字节。CMakeCache、build日志及实际执行guard的末尾空行曾触发Git diff检查，三个精确文件的属性允许原始末尾空行，仍检查行尾空白和制表符；未删改原始记录或降低生产源码检查。

复用同一系统COM/Shell接口与owner保护注册脚本，无新增主语言、框架、依赖或共享契约，生产源码无diff。静态CRT仅存在于隔离测试产物，不能替代生产依赖决策。测试规模固定，不提供大资产库性能或稳定性保证。下一步交主协调安排具有所需权限的精确COM/注册读取/DLL加载跟踪，不继续扩接口、GUID、属性或注册位置。
