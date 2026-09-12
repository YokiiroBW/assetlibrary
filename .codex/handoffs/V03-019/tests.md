# V03-019 测试记录

环境：Windows 11 x64，MSVC 19.44.35207/Visual Studio Build Tools 2022，Windows SDK 10.0.26100.0；Release /MT、C++17、/W4 /WX /permissive- /analyze /utf-8。所有测试 UI 宿主内嵌 Common Controls v6、PerMonitorV2、asInvoker 清单。自动测试仅创建本任务隐藏窗口和合成图像。

## 实际命令

```powershell
cmake -S tests/windows-gallery -B .runtime/gallery-tests -G "Visual Studio 17 2022" -A x64
cmake --build .runtime/gallery-tests --config Release --parallel 2
ctest --test-dir .runtime/gallery-tests -C Release --output-on-failure
python -I -B scripts/verify_repository.py
```

本机 cmake/ctest 来自 `C:/Program Files (x86)/Microsoft Visual Studio/2022/BuildTools/Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/`；Python 来自 `C:/Users/Administrator/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe`，进程设置 UTF-8。

## 验证内容

- gallery_layout：宽度/DPI/模式矩阵、无重叠、分页/目录顺序、极端宽高比与 101 可见项。
- gallery_surface：真实隐藏 HWND、代次、像素 capacity、键盘/范围/Ctrl+A/空白取消、内部/边界 Tab、回调重入/直接删除、Loading/空页 Hide/Show 通知和 Shown 意图、MSAA 名称/选择、退役数据先清。
- gallery_rendering：真实 GDI 绘制，极横/竖 contain、透明合成、32 次 GDI/USER 计数不增长、负滚动加非零 viewport origin 时画布外哨兵像素不变。
- gallery_budget：101 candidates、29 张小图全部可见/第17项接纳、16 MiB拒绝诚实不可用。
- gallery_uia：静态 QI 能力、root/child互斥焦点、窗口外命中拒绝、状态文本、直接旧Name/Selection拒绝；512 live/固定退休槽、第513拒绝和失败不返回自定义MSAA；所有对象真实归还、dispatcher窗=0、最后Folder pin归还前不能有dispatcher。
- gallery_accessibility_external：独立 MTA 客户端，MSAA完整名称/选择+原生UIA全部30项、Selection/SelectionItem、目录Invoke；客户端先退出再关闭。
- gallery_accessibility_retired_proxy：客户端持有UIA/MSAA时触发异步Invoke关闭，等待真实HWND销毁；旧root/item CurrentName失败，旧Selection为空；对象/调度窗回收。
- gallery_accessibility_page：同HWND替换页，旧名称不可读，重新获取provider可读新合成名称。
- gallery_accessibility_cycles：同STA三次调用中关闭；每轮owner回1，MSAA/native providers=0，pending=0、dispatcherWindows=0、pixels/selection清空；累计精确断开31/62/93。最长30秒等待只是失败边界，不是G4。

严格构建已通过；最终修正的 Surface/UIA 定向及4个外部路径均通过，最终完整10/10 CTest通过（1.20秒），0失败、0跳过。仓库快门禁也已通过（464个架构输入、21个迁移测试、14个架构测试；Alpha发布审计仍保持blocked）。选择通知计数验证进入 native UIA 请求路径，不冒充真实辅助客户端收到事件。

Windows UIA客户端在退休后 GetCurrentSelection 实测返回 S_OK+空数组；直接 provider 返回 UIA_E_ELEMENTNOTAVAILABLE。两者均不含旧项。CurrentName失败才记录为HRESULT拒绝。旧桥失败对照详见 investigation，不并入成功测试计数。

未执行：实际Explorer/生产Core图片、安装/卸载/发布、跨显示器手动DPI、G4耐久。本任务的可见harness由root运行，已通过列表/密度/多选/窄窗/最大化/滚动裁剪和宽屏29张；证据归root `.runtime/gallery-validation/`，不替代后续实际Explorer。

## 最终受控监听场景

`gallery_accessibility_listener` 由独立 MTA probe 在读取任何本画布 provider 前先订阅全局focus，再通过固定测试消息触发当前画布选择，确认native provider仍为0；获取本画布root后订阅Selection_Invalidated，实际收到2次选择通知。持订阅调用目录Invoke、等待真实关闭、释放订阅与客户端后，owner=1、MSAA/native providers=0、pending=0、dispatcherWindows=0。最终全组10/10通过，三轮同STA0.35秒，listener0.22秒。

focus订阅不是以抢前台方式测试；其实际事件来源可能是当前Explorer，测试不要求隐藏harness一定收到全局焦点变化；本次受控运行也收到1次本进程focus。精确root/child HasKeyboardFocus已有直接断言。

Rendering/Budget在全局listener开启时曾实测Destroy后owner4/providers2/pending2/dispatcher1，属于已排队退休；测试现在2秒上限pump后严格断全部归零，Budget也必须owner1/!wrongThread。不会把CoUninitialize后的回收代替退役成功。

## d215431 GUI文案补充验证

构建 `cmake --build .runtime/gallery-tests --config Release --target GalleryUiaTests --parallel 2` 严格通过；`ctest --test-dir .runtime/gallery-tests -C Release --output-on-failure -R '^gallery_uia$'` 1/1通过（0.05秒）。直接查询Ready Library/Directory/NextPage/Reparse的UIA HelpText/ItemStatus必须等于既有TypeText(kind)；同四种类型AccessDenied必须仍报告StatusText(AccessDenied)。没有重新执行无关套件；前述10/10和仓库快门禁是上一个完整检查点证据，root继续实际包验证。

## f2ad8e3 GUI键盘补充验证

严格构建 `cmake --build .runtime/gallery-tests --config Release --target GalleryLayoutTests GallerySurfaceTests --parallel 2` 通过；定向 `ctest --test-dir .runtime/gallery-tests -C Release --output-on-failure -R '^gallery_(layout|surface)$'` 2/2通过（0.08秒）。几何夹具刻意让整行目录中心比同排下一图片更近，修复前会选错；验证Left/Right同排优先、末列/首列沿order跨行、页末无目标、负偏移稳定及Up原几何不变。真实控件两列布局下Shift+Right焦点2且仅选1/2；普通Right从2到3，Left从3回2。仅做相关定向验证，完整10项/快门禁沿用此前检查点；root负责实际Explorer合并后的总验收。

## 003d8b7 自有摘要最终验证

`cmake --build .runtime/gallery-tests --config Release --parallel 2` 全目标严格通过；`ctest --test-dir .runtime/gallery-tests -C Release --output-on-failure` 完整10/10通过（1.22秒）。Surface专项新增：调用方30项/已选2文字在实际选择0时仍原样显示，证明没有Surface计数；STATIC原生accName与当前字体；宽窗按钮右侧和320DIP窄窗下一行的矩形不盖canvas；300字截至256、255字符加代理对不拆开；清空隐藏和Clear先清摘要再callback；长摘要改变视口时callback可删除Surface，owner最终归还。其它原生UIA关闭、换页、3轮和受控listener均保持通过。

本轮没有运行真实Explorer或安装；Native状态条不刷新是root实机确认的宿主兼容性边界，自有摘要由View同一现有字符串驱动，发布证据由root汇总。
