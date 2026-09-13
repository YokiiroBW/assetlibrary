# V03-031 测试记录

2026-09-13，Windows x64，真实MSVC19.44.35228.0（MSVC工具目录14.44.35207），Windows SDK10.0.26100.0。所有命令从本独立worktree执行。

| 检查 | 结果 |
|---|---|
| CMake Visual Studio17 2022/x64配置 | 通过，实际识别MSVC |
| GalleryPreviewViewportTests Release | 通过，包含GallerySurface库编译与/analyze，无警告/错误 |
| gallery_preview_viewport CTest | 1/1通过，0.01秒 |
| 数学内容 | 7定向场景、256形状/视口矩阵、2000次重复最大缩放/拖动饱和 |
| verify_repository.py | 通过，M0-009架构快速gate |
| 最终diff与生成编译选项 | 通过；无共享头改动、无GUI/IPC/IO |

实际入口：

```powershell
cmake -S tests/windows-gallery -B .runtime/v03-031-gallery -G "Visual Studio 17 2022" -A x64
cmake --build .runtime/v03-031-gallery --config Release --target GalleryPreviewViewportTests --parallel 2
ctest --test-dir .runtime/v03-031-gallery -C Release --output-on-failure -R "^gallery_preview_viewport$"
python -I -B scripts/verify_repository.py
```

cmake/ctest来自 `C:/Program Files (x86)/Microsoft Visual Studio/2022/BuildTools/Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin`。生成vcxproj核实ConformanceMode=true、EnablePREfast=true、WarningLevel4、TreatWarningAsError=true、Release RuntimeLibrary=MultiThreaded、stdcpp17和/utf-8，对应冻结要求/W4 /WX /permissive- /analyze /MT。

CTest原始记录 `.runtime/v03-031-gallery/Testing/Temporary/LastTest.log`，成功输出：`preview_viewport: 7 directed scenarios, 256 shape/view matrices, anchor/bounds/invalid/saturation passed`。架构记录 `.runtime/v03-031-gallery/repository.log`。

定向场景检查独立坐标：1600×1200在800×600画布，源像素(400,300)缩放前后均锚在(210,170)；800×600图片在偏移400×300视口四边夹取；源中心(1000,750)在ActualSize/Resize保留；1000×100图只能横移；小图和大有序LONG RECT居中；无效viewport往返不丢显式中心；设备像素视口变化不改变100%的图尺寸。矩阵额外检查每轴较小即居中、较大不露空白。

没有重跑未变化的成功检查，没有执行GUI、NAS、真实Core、生产注册/pipe或根版本操作。Surface与root继续真实交互及最终Explorer验收。
