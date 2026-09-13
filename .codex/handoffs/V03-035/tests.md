# V03-035 验证

在本工作区先创建.runtime，沿用已安装VS2022 BuildTools的Launch-VsDevShell.ps1（Arch amd64、HostArch amd64、SkipAutomaticLocation），无工具链安装/系统配置。

```text
cmake -S tests/windows-gallery -B .runtime/gallery-tests -G "Visual Studio 17 2022" -A x64
cmake --build .runtime/gallery-tests --config Release --target GallerySurface --parallel 2
cmake --build .runtime/gallery-tests --config Release --target GallerySurfaceTests --parallel 2
ctest --test-dir .runtime/gallery-tests -C Release -R '^gallery_surface$' --output-on-failure
cmake --build .runtime/gallery-tests --config Release --parallel 2
ctest --test-dir .runtime/gallery-tests -C Release -E '^gallery_preview_viewport$' --output-on-failure
```

最终严格all build通过；受影响12个唯一CTest全通过（2.25秒）：gallery_layout、surface、accessibility_external、accessibility_retired_proxy、rendering、budget、uia、accessibility_page、accessibility_cycles、accessibility_listener、preview、preview_external。没有把前面重跑surface算新增；不变gallery_preview_viewport未执行且未计入12项。

新增PageNavigation真实隐藏HWND用例与原有SurfaceTests同目标：native name/state、disabled与手工WM_COMMAND拒绝、±1一次回调、全部9项Tab/disabled跳过、禁用焦点回canvas、SetPage/Clear/隐藏清flags、Begin/EndPreview保留有效flags、1000/320/180DIP布局、回调清页/销毁、EnableWindow重入更新flags胜过旧调用，以及EnableWindow/焦点回调删除Surface。使用已有Owner/Drain，owner引用回1，无后台COM或新测试服务。

失败轨迹：初次测试编译/analyze C6387发现辅助Require不向静态分析器证明HWND非空，改显式if/throw校验，没有屏蔽诊断。首次运行命中禁用焦点回canvas断言，确认EnableWindow已将焦点清空，生产修正aefa1f4后该项通过。随后旧wide-summary物理宽度不足8按钮，改该夹具为1000DIP并检查末项115；窄窗320/180DIP仍真实通过。最终所有受影响项同一代码批次通过。

原始日志保留：.runtime/gallery-configure.log、gallery-source-build.log、gallery-build-final.log、gallery-affected.log；前面surface定向失败记录gallery-surface.log（焦点）和gallery-surface-final.log（旧wide夹具）。没有修改旧失败结果为通过。

无GUI、现有Explorer/注册/安装、Host/pipe、NAS或真实资产操作。原预览数学未改，数据查询/权限和实际产品交付由root/另owner执行。

最终 `python -I -B scripts/verify_repository.py` 通过（539架构输入、21迁移模型测试、14架构测试及既有依赖/SDK/主题政策）。Alpha仍blocked；日志.runtime/gallery-repository.log。
