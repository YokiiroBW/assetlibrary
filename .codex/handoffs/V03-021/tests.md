# V03-021 测试记录

2026-09-13 Windows x64；MSVC19.44.35228.0/工具14.44.35207，WindowsSDK10.0.26100.0；生产/MT、Proof/MD不变。所有构建保持/W4 /WX /permissive- /analyze /utf-8。

## 实际命令

```powershell
$cmake='C:/Program Files (x86)/Microsoft Visual Studio/2022/BuildTools/Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe'
$ctest='C:/Program Files (x86)/Microsoft Visual Studio/2022/BuildTools/Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/ctest.exe'
$python='C:/Users/Administrator/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe'
& $cmake -S tests/windows-shell -B .runtime/explorer-gallery -G 'Visual Studio 17 2022' -A x64
& $cmake --build .runtime/explorer-gallery --config Release --parallel 2
& $ctest --test-dir .runtime/explorer-gallery -C Release --output-on-failure
& $python -I -B scripts/verify_repository.py
```

最终完整套件12/12通过，69.14s：

| CTest | 时间/结果 |
|---|---|
| explorer_snapshot | 0.71s pass |
| explorer_fault_harness | 1.05s pass |
| explorer_navigation_menu | 0.01s pass |
| explorer_loading_refresh | 43.55s pass |
| explorer_loading_view_wiring | 1.23s pass |
| explorer_diagnostics | 0.01s pass |
| explorer_product | 0.01s pass |
| explorer_session_notification | 1.38s pass |
| explorer_product_imports | 0.03s pass |
| explorer_gallery_thumbnail | 20.11s pass |
| explorer_gallery_requests | 0.78s pass |
| explorer_gallery_view | 0.25s pass |

完整回归后，仅补强Create中incoming Browser.AddRef前generation捕获并增加其重入Destroy负控；实际再次严格构建AssetLibraryExplorer/ExplorerGalleryViewTests，再运行同一explorer_gallery_view，0.23s通过。没有把重复测试计为额外通过数。12个唯一CTest失败0跳过0。

最终verify_repository通过：482架构输入、497 C#源检查、21 migration manifest测试、14 architecture测试、SDK/依赖/主题检查；Alpha decision=blocked。日志：.runtime/explorer-gallery/final-build.log、final-tests.log、view-final-build.log、view-final-test.log、verify-repository-final.log。

## 新测试覆盖

GalleryThumbnailTests从root独立thumbnail-vectors-v1.json生成C++只读fixture，验证13个固定字节向量；此外验证epoch/node、premultiplied alpha、空状态、最大1MiB/尺寸溢出。真实同用户同会话命名pipe检查合法帧、不等EOF、坏版本、截断+取消、两个保留操作/第三个Busy、取消实际回收，以及真实约20s总期限。测试端点FirstInstance拒抢占，已退出释放；没有HTTP/NAS。

GalleryRequestTests用plain可控Sources验证UI查询次数0、Loading有界重试/Ready不poll、四page工作上限、16可见候选与两个image线程、取消active/queued work、hide/离视口后的late ticket丢弃、同index离开重入、已取出完成批次经过UI call-out后的Current复验、关闭和容量复用。后台不持任何COM对象。

GalleryViewTests使用真实View/Surface和COM browser/folder记录器。隐藏窗口验证IFolderView、内存枚举、模式、普通选择与SVSI_EDIT复合位、真实最后Release未Destroy、外部Destroy幂等、四共享slot/第五拒绝、production非根custom route。root授权的无激活屏外自有窗口/假Sources验证真实page完成时CompareIDs重入Destroy和Refresh不能回灌旧数据；GetWindow嵌套Create与incoming AddRef销毁不能复活或漏slot；Destroy时browser.Release重入Create被拒；Loading零候选时hide立即取消、show后台重新查询、UIActivate失焦不取消、已填充View正常选择。

公开通知正控使用root批准的内部非导出notificationRoot参数，指向新建系统临时目录的真实SHParseDisplayName PIDL。实际SHChangeNotifyRegister/NewDelivery/Lock/Unlock收到UPDATEDIR后，browser记录到清空后的同窗口回root调用；临时目录和监听随测试清理。此测试不是production已注册根的接线验收。

## 中间失败与纠正

- 最初用未注册私有PIDL发Shell通知未收到消息，明确失败且未算通过；换成真实自有Temp PIDL机制正控，没有改成空Desktop或伪造Windows系统PIDL布局。
- 新可见View回归最初准确暴露loading-hide（pages1/canceled0/jobs1），没有通过延长等待掩盖。Surface新增Shown意图读数后，Viewport/Refresh使用相同已提交状态，hide/show回归通过。
- 独立review指出Apply/创建COM重入与配额问题；加入入口generation/HWND、每个比较与发布前复验、creating状态、旧异常不得清新状态，以及对应有区分力负控。
- 中间编译中的ADL Request歧义、测试IUnknown多继承转换、静态分析的BOOL命令返回/聚合nothrow分配、测试SettingsCommand链接等已按类型/构建契约纠正，没有关闭警告或分析。

## 产物和限制

生产DLL .runtime/explorer-gallery/product-shell/Release/AssetLibrary.Explorer.dll，最终SHA256 596F736528ADFC5C913D8F88B8B6C414C5A5121E26A4624E1745880E0054EEE7。GDI32/msimg32/oleacc由ADR允许并纳入受限imports；没有动态CRT/.NET/网络/图像codec库。

没有真实Explorer、生产注册/安装或NAS资产操作。可见性测试仅获批的屏外/不激活自有窗口，Sources全假；不是前台用户GUI或真实图片验收。Surface外部MSAA provider保留引用的v6/PMv2结果由V03-019/root另管，本任务不声称已关闭。G4未执行，完整客户端/图库发布未宣布。
