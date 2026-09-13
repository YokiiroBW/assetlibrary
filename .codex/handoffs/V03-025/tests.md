# V03-025 测试记录

Windows x64，VS2022 BuildTools/MSVC19.44、SDK10.0.26100.0。root批准.runtime临时CMake，只编译BrowsePreferences.cpp和GalleryPreferencesTests.cpp；C++17、/W4 /WX /permissive- /analyze /utf-8、/MT，UNICODE/_UNICODE/WIN32_LEAN_AND_MEAN/NOMINMAX，链接advapi32/ole32。不改产品CMake。

```powershell
$cmake='C:/Program Files (x86)/Microsoft Visual Studio/2022/BuildTools/Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe'
$ctest='C:/Program Files (x86)/Microsoft Visual Studio/2022/BuildTools/Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/ctest.exe'
& $cmake -S .runtime/preferences-native -B .runtime/preferences-build -G 'Visual Studio 17 2022' -A x64
& $cmake --build .runtime/preferences-build --config Release --parallel 2
& $ctest --test-dir .runtime/preferences-build -C Release --output-on-failure
```

最终1/1通过0.01s，日志.runtime/preferences-native/configure.log、build.log、tests.log。正式接入由root把组件加入AssetLibraryGallery，测试源链接Gallery；无需额外依赖。EXE SHA256 675F0E415747ED134729A471105B6F671670237DE1F07F1F1212C835221D34C4。

python -I -B scripts/verify_repository.py通过handoff、架构、依赖、主题及14项架构回归；日志.runtime/preferences-native/verify.log。changed_files由git diff --name-only -z c10eddf生成。Alpha保持blocked。

独立golden字节验证ABP1/version/length/little-endian16B和Gallery176/List256；两个mode各96..256 round-trip。NULL/0/15/17/65536长度、magic/版本/头长/模式损坏、95/257密度失败且整体默认；非法Encode归零。首次负控误将合法255 DIP算作损坏，改95/257后通过，未关闭警告或放宽实现。

实际HKCU仅新建GUID易失专用键。缺失Load不创建；REG_SZ/REG_DWORD、未知版本、0/15/17/64B binary默认且原字节/类型不变。只读handle实际写返回ERROR_ACCESS_DENIED，旧16B值readback完整；非法设置也不写。成功Save只改BrowseState，旁边DWORD42保持；新reader读新tuple，先前Values副本不被改变。析构删除专用键并核RegOpen返回ERROR_FILE_NOT_FOUND，无ACL、生产偏好或产品注册变更。

Setup静态审核确认ProductKey是Software\AssetLibrary\WindowsClient，OwnedRoots不含ExplorerPreferences；兄弟键不进入只支持string/int的schema或卸载事务。root确认preview6已有未改Setup模型证据，按验证纪律不重跑。系统PATH仅有dotnet host，无SDK10.0.111，但此可选检查没有用于宣称失败或通过。

没有GUI/生产pipe/G4。实际View/Surface回调和错误摘要由root单写；View测试使用fake store，避免新的默认Load/Save写真实偏好。
