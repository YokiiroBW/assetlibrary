# V03-016 测试记录

2026-09-12，Windows x64；独立worktree C:/YOKI/Codex/AssetLibrary-worktrees/V03-016。
MSVC19.44.35228.0（工具目录14.44.35207），WindowsSDK10.0.26100.0。Release /W4 /WX /permissive- /analyze /utf-8；生产所有库/MT，Proof/MD。

## 实际命令

CMake/CTest不在默认PATH，实际使用完整路径，以下变量用于重现：

```powershell
$cmake='C:/Program Files (x86)/Microsoft Visual Studio/2022/BuildTools/Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe'
$ctest='C:/Program Files (x86)/Microsoft Visual Studio/2022/BuildTools/Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/ctest.exe'
$python='C:/Users/Administrator/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe'
$linker='C:/Program Files (x86)/Microsoft Visual Studio/2022/BuildTools/VC/Tools/MSVC/14.44.35207/bin/Hostx64/x64/link.exe'
& $cmake -S tests/windows-shell -B .runtime/explorer-product-tests -G 'Visual Studio 17 2022' -A x64
& $cmake --build .runtime/explorer-product-tests --config Release --parallel 2
& $ctest --test-dir .runtime/explorer-product-tests -C Release --output-on-failure -R '^explorer_(product|session_notification|product_imports)$'
& $ctest --test-dir .runtime/explorer-product-tests -C Release --output-on-failure -R '^explorer_(snapshot|fault_harness|navigation_menu|loading_refresh|loading_view_wiring|diagnostics)$'
& $cmake -S apps/windows-shell -B .runtime/explorer-product -G 'Visual Studio 17 2022' -A x64
& $cmake --build .runtime/explorer-product --config Release --target AssetLibraryExplorer --parallel 2
& $cmake '-DPRODUCT_DLL=C:/YOKI/Codex/AssetLibrary-worktrees/V03-016/.runtime/explorer-product/Release/AssetLibrary.Explorer.dll' "-DLINKER=$linker" -P tests/windows-shell/VerifyProductImports.cmake
& $python -I -B scripts/verify_repository.py
```

两组CTest覆盖9个唯一测试；imports脚本最后大小写归一化后仅重跑该项，通过0.10秒。相同imports检查以/MD Proof DLL为负控，实际非零并报告msvcp140.dll不在allowlist（预期拒绝，不是未修失败）。

## 结果

| 项目 | 结果 | 范围 |
|---|---|---|
| 严格测试目标构建 | pass | 生产/Proof同源，/analyze开启 |
| 独立生产构建 | pass | 实际生产入口与/MT静态库 |
| explorer_product | pass，0.03s | 两DLL互拒CLSID，生产标题/解析根，真实TokenUser/session pipe；设置背景菜单及owner生命期；纯命令路径/参数/Unicode/长度/不安全输入与verb负控 |
| explorer_session_notification | pass，1.42s | 独立root生命期、核事件/root/线程、通知合并；根Refresh、子目录同窗口root；不延长期限、新事件重开观察；失败/错窗口/断site/重入/第五view拒绝 |
| explorer_product_imports | pass | 八个系统库，无动态VC/.NET/媒体或网络依赖 |
| explorer_snapshot | pass，0.81s | 独立wire向量、PIDL/分页/畸形、真实本机pipe、150ms预算、取消/四名额、COM/Probe |
| explorer_fault_harness | pass，1.06s | silent/invalid-version/partial-frame/crash、拒占端点、期限/连接上限、取消/恢复 |
| explorer_navigation_menu | pass，0.02s | 目录默认打开/当前窗口，拒绝文件和写verb，系统图标、COM释放 |
| explorer_loading_refresh | pass，43.56s | 原500ms/10秒/20次、实际呈现状态、配额/线程/重入/生命周期、真实十秒负控 |
| explorer_loading_view_wiring | pass，1.26s | 原有隐藏自有DefView回调接线；非真实生产资产UI |
| explorer_diagnostics | pass，0.02s | 原只读数字诊断/G3键 |
| verify_repository | pass | 基线/最终架构、契约、依赖/SDK/源；367输入含生产Shell、21迁移/14架构测试；Alpha blocked |
| 最终diff | pass | 可识别17项迁移，无第二份运行逻辑、越权文件或wire变更 |

原6项46.75秒，新增3项首次1.48秒。测试端点在原6项结束后已通知协调者和Session owner释放。
中间构建因测试缺少shellapi声明、常量普通if及分析器无法证明测试断言而失败；已使用正确include、if constexpr、明确初始化/空值处理修复，没有降低告警或关闭分析。最终构建通过。

## 产物与未覆盖

生产.runtime/explorer-product/Release/AssetLibrary.Explorer.dll，SHA256 092F9A84D8010A3F2CD28678FF4ADCB512EFD9F28ACF889F2085561AAEF9F7CE。
dumpbin实测ole32、OLEAUT32、SHELL32、USER32、SHLWAPI、ADVAPI32、COMCTL32、KERNEL32；整包重建须取实际hash。

未做实际用户GUI、生产/Proof注册、Settings进程启动、NAS或资产写入。通知测试使用公开callback及自有隐藏窗口记录器，不代表Host→实际Explorer通知交付。未运行既知未通过proof-owner-lifetime或enum-done研究，不计已修。G4豁免未测，没有20轮/8小时。9项CTest失败0跳过0；未执行整包/发布验证不折算通过。
