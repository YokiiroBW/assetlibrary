# V03-032 测试

## 实际命令

在本工作区导入已安装VS2022 BuildTools的Launch-VsDevShell.ps1，amd64/HostArch amd64/SkipAutomaticLocation。没有工具链安装或环境持久配置。

```text
cmake -S tests/windows-gallery -B .runtime/gallery-tests -G "Visual Studio 17 2022" -A x64
cmake --build .runtime/gallery-tests --config Release --parallel 2
ctest --test-dir .runtime/gallery-tests -C Release -R '^gallery_rendering$' --output-on-failure
ctest --test-dir .runtime/gallery-tests -C Release -R '^gallery_preview$' --output-on-failure
ctest --test-dir .runtime/gallery-tests -C Release -E '^gallery_(preview|rendering|preview_viewport)$' --output-on-failure
```

实际第一次targeted过滤为`^gallery_(preview|rendering)$`：rendering通过0.19s；PreviewTests当时因/analyze C6031未检查GetKeyboardState返回值未生成EXE，故该项Not Run，不能算运行失败或通过。修复检查返回值后，只重建/运行gallery_preview，1/1通过0.25s；最后补全8项Tab回归后该项重新严格构建并1/1通过0.26s。Surface/Rendering没有随后变动，所以没有重复运行其已绿项。更早Surface初次静态编译发现DrawText需要非const RECT，已修后严格编译通过，无诊断屏蔽。

最后12个唯一控件测试全部通过/0失败/0未执行。其余10项一次通过1.24s，含gallery_layout、surface、accessibility_external、retired_proxy、budget、uia、accessibility_page、cycles、listener和preview_external。真实几何专项gallery_preview_viewport由V03-031已有独立结果，本线程未重复执行，不混同数量。

## 证据

.runtime/gallery-build.log记录首次all build及测试编译诊断；.runtime/gallery-preview-build-final.log与gallery-preview-final.log为最后Preview严格构建/结果，gallery-targeted.log保留最初rendering通过和preview未生成EXE事实，gallery-affected.log为其余10项。不是更改旧测试输出为通过。

资源/生命周期与像素断言见提交的PreviewTests.cpp和RenderingTests.cpp：捕获/焦点是真实Win32调用，所有窗由本用例新建且隐藏；绘图用原生产WM_PRINTCLIENT及源尺寸DIB。无真实Provider、IPC、HTTP或用户图片；无注册/安装/原Explorer/G4。owner/provider/dispatcher回基线，32次放大绘制GDI/USER不增长。

最终 `python -I -B scripts/verify_repository.py` 通过，含539项架构输入、21项迁移模型测试和14项架构测试、依赖/主题/SDK政策。Alpha仍blocked；没有因此扩大发布结论。日志.runtime/gallery-repository.log。
