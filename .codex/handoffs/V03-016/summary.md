# V03-016 交接摘要

状态ready_for_review；分支codex/v03-016-explorer-product；工作区C:/YOKI/Codex/AssetLibrary-worktrees/V03-016。
实现commit：e137514edfeef3db25621860fb8156f596e00671。此交接是后续独立提交，合并需包括本任务完整分支。

## 完成内容

原Proof的17个运行实现/头文件/def整体迁移至apps/windows-shell，只有一份源。ExplorerProof.cpp/.def更名ExplorerFolder.cpp/Explorer.def，其余同名迁移；原测试、Probe和注册诊断工具留在tests/windows-shell。共享CMake函数编译生产与Proof身份。

生产输出AssetLibrary.Explorer.dll，CLSID={BBC992DE-CE5D-48C8-A86C-7230C7D72B02}，标题资产库，pipe前缀AssetLibrary.Explorer.v1.；旧4FF CLSID、Proof pipe/DLL、Probe默认身份保持兼容，互不后备。所有pipe仍追加实际TokenUser SID/session。

复用原生DefView、授权库/真实目录、100+1分页、当前页名称/类型列、同窗口打开目录和系统图标。文件明确本版不打开内容，没有传输、写入、媒体或伪造成功逻辑。

生产背景菜单提供连接设置：只用已加载DLL同目录的绝对路径启动AssetLibrary.Settings.exe，无参数/URI/PATH解析/句柄继承/输入空闲等待。失败返回真实HRESULT；状态行准确提示设置及重新打开资产库，不承诺F5重开有限观察。

root批准CCP后接入公开GETNOTIFY/FSNOTIFY。生产callback持有root clone，只核该root的UPDATEDIR并合并UI动作，重新核线程/site/HWND/episode。根Refresh；子目录用SBSP_ABSOLUTE|SBSP_SAMEBROWSER回root。事件可重开已结束的500ms/10秒/20次Loading观察，活跃观察期限不延长。生产超过4活动视图返回ERROR_BUSY，不能无callback静默降级。Proof不启用通知。

root批准生产DLL和静态库/MT，Proof保持/MD；COM仅系统分配器，不跨DLL传STL/CRT对象。没有新增语言、框架或第三方依赖；静态MSVC运行库安全更新需重建。新增imports门禁只允许八个系统DLL。

## 构建与产物

真实生产入口及注册必需字段详见apps/windows-shell/README.md：

```powershell
cmake -S apps/windows-shell -B .runtime/explorer-product -G "Visual Studio 17 2022" -A x64
cmake --build .runtime/explorer-product --config Release --target AssetLibraryExplorer --parallel 2
```

产物.runtime/explorer-product/Release/AssetLibrary.Explorer.dll，SHA256：092F9A84D8010A3F2CD28678FF4ADCB512EFD9F28ACF889F2085561AAEF9F7CE。该hash只对应本次独立构建；整包重建按其实际hash验收。

## 测试与架构审查

严格Release /W4 /WX /permissive- /analyze /utf-8的测试构建与独立生产构建通过。原6项CTest全部通过（46.75秒），新增生产身份/设置纯命令、公开通知机制、生产imports三项通过，总9/9，失败0、跳过0。

imports实测仅ole32/oleaut32/shell32/user32/shlwapi/advapi32/comctl32/kernel32。相同检查对/MD Proof DLL按预期拒绝msvcp140.dll，防止空检查通过。

verify_repository基线和实现后均通过；最终367架构输入包含apps/windows-shell，21迁移manifest与14架构测试通过，Alpha仍blocked。最终diff检查确认单一生产源、Proof调用兼容、wire/150ms/取消/分页不变、无越界修改；测试详见tests.md。

依赖方向：Shell适配→本机Host快照→既有Core用例。没有跨模块内部访问、数据库迁移或共享契约文件变更。复用原Snapshot/PIDL/pipe/NavigationMenu/图标/LoadingRefresh/G3诊断和COM测试记录器，不复制权限/路径/文件身份/传输规则。

大目录仍单页O(101)，没有50万项遍历；通知单root、每视图至多一个待处理提示，无每项IPC/后台轮询。未读取/修改真实资产。权限与文件安全仍由Core和Host已有边界控制。

## 风险与下一步

本任务未做实际GUI、HKCU注册、Settings启动、NAS或真实资产操作。无注册的隐藏自有窗口/COM记录器只验证机制，Host→Shell公开通知→实际生产Explorer的根/目录清理，以及设置程序启动，必须由root整包验收。

子目录导航失败会尝试刷新旧epoch，但不能声称面包屑已清理成功。系统Explorer历史不由扩展清除；旧位置受Host epoch拒绝。Host必须在实际状态/epoch变化之后单次发通知，通知失败不得恢复旧身份。通知只是刷新提示，不授权权限。

本任务没有执行既知未通过研究诊断、G4/20轮/8小时。G1/G2/G3旧hash证据不能代替新产品DLL整包验收，完整V0.3未宣布完成。

建议合并顺序：root共享契约/架构/CI冻结→V03-015与V03-016组件→V03-017整包→root真实安装/连接设置/退出与切换/根和子目录失效验收。安装、Host/Settings、共享契约/版本由其各自owner修改。
