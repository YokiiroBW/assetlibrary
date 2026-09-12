# 原生图库同源验证

本目录构建 ADR-0021 的 Win32/GDI 控件独立测试程序。生产仅消费 `apps/windows-shell/gallery/Surface.cpp`、`Layout.cpp`、`Accessible.cpp`、`Uia.cpp` 与已有 snapshot 语义；harness 的渐变 PBGRA 是明确标注的合成测试数据，不是产品客户端或 NAS 预览后备。

使用 Visual Studio 2022 Build Tools、Windows SDK 10.0.26100.0、C++17；所有目标保持 `/W4 /WX /permissive- /analyze /utf-8` 与 `/MT`。UI 宿主测试 EXE 嵌入 Common Controls v6、PerMonitorV2 与 asInvoker 清单；生产 DLL 使用 Explorer 自身宿主环境。

```powershell
cmake -S tests/windows-gallery -B .runtime/gallery-tests -G "Visual Studio 17 2022" -A x64
cmake --build .runtime/gallery-tests --config Release --parallel 2
ctest --test-dir .runtime/gallery-tests -C Release --output-on-failure
```

| CTest | 覆盖 |
| --- | --- |
| gallery_layout | 36组宽度/DPI/模式矩阵，保比例舍入、无重叠、命中映射、空页、101项边界、可见普通文件≤101、极端横/竖比例 |
| gallery_surface | 隐藏但真实 HWND、页面/缩略图代次、像素 capacity 预算、选择与焦点、范围选择、内部/边界Tab、F5/Enter/菜单回调、滚动/隐藏释放、Clear先清名称再回调、外部DestroyWindow、回调删除C++对象、退休MSAA无旧数据 |
| gallery_accessibility_external | 独立MTA客户端读取完整MSAA名称/选择/默认动作，原生UIA List/ListItem语义，创建STA的owner引用与正常退场 |
| gallery_accessibility_retired_proxy | 客户端持有旧代理且在默认动作返回后异步关闭控件；拒绝旧名称并验证真实引用回收 |
| gallery_accessibility_page | 同一HWND替换页，旧provider不可读取新名称，新provider可正常读取 |
| gallery_accessibility_listener | 可控全局focus/选择订阅、未观察root不创建provider、真实选择通知与关闭回收 |
| gallery_accessibility_cycles | 同STA三轮关闭、provider/退休队列/调度窗归零 |
| gallery_uia | 512上限/无分配退休队列、513拒绝且无旧桥fallback、接口/焦点/状态/边界 |
| gallery_budget | 101可见、29小图、16MiB拒绝诚实不可用 |
| gallery_rendering | 内存DC调用同一WM_PRINTCLIENT绘制，512×1/1×512 contain、PBGRA透明合成、32次绘制后GDI/USER计数不增长 |

自动用例创建的顶层窗口保持隐藏，不控制系统 Explorer、不注册 COM、不调用真实 Host 或网络。所有 CTest 必须通过，才能把该实现交给主任务继续真实 Explorer 验收；具体运行结果与尚未覆盖项见 V03-019 handoff。

可由主任务手动运行同源可见窗口：

```powershell
.runtime/gallery-tests/Release/GalleryHarness.exe --show
```

如果通过进程 API 启动，使用 `CreateNoWindow=true` 隐藏控制台；不要以 `WindowStyle Hidden` 覆盖其首个 GUI `ShowWindow`。窗口和每个条目均标明合成数据；图库/列表、密度、滚动、选择和辅助技术使用同一生产控件。目录默认动作仅记录回调，不伪装实际目录导航；后者属于 V03-021 与真实 Explorer 验收。

不显示顶层窗口的同源画布输出：

```powershell
.runtime/gallery-tests/Release/GalleryHarness.exe --render .runtime/gallery-wide.bmp 1100
.runtime/gallery-tests/Release/GalleryHarness.exe --render .runtime/gallery-narrow.bmp 360
```

输出路径必须不存在，避免覆盖旧证据。BMP 来自实际 GDI 绘制，不是设计稿；它只含画布，不代表 Explorer 命令栏、工具栏样式或跨显示器 DPI 已完成实机验收。

控件只处理当前有界页。原生按钮提供图库/列表、缩小/放大、刷新和设置；方向/Home/End/Page、Ctrl/Shift多选、Space选择切换、Enter导航回调及键盘右键可用。内部Tab进入工具栏，边界Tab及Ctrl/Alt+Tab交给宿主。没有加入原图/1600px预览、小图弹层、标签或时间轴业务，F2/文件写操作保持不可用。

持久图像只保存不可变PBGRA共享缓冲，计数按vector容量而非仅长度；每视图≤101候选/16MiB；16仅请求批次上限。无持久GDI位图副本，单次AlphaBlend临时DIB≤1MiB，绘完即释放。布局对极端比例限制tile几何，但实际图像始终按原尺寸contain，不随tile比例拉伸。颜色来自生成的共享Web语义主题；高对比度用系统颜色覆盖。

原生UIA和MSAA共用当前页模型。调度窗和provider只保留独立Folder/DLL pin；窗口事件只临时保留View。旧provider退役拒绝current名称，UIA客户端旧Selection可返回空数组S_OK。详见handoff生命周期对照。

工具栏当前摘要由 `Surface::SetStatusText` 原样显示View计算结果，原生STATIC子项ID106，可通过GetDlgItem读取。最多256个UTF-16单元，宽窗旁置、窄窗换行，Clear/Destroy清空；测试覆盖native可访问名和重排回调销毁。不依赖Windows11宿主底部DefView缓存计数。
