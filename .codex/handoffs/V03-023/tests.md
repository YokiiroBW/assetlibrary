# V03-023 测试记录

环境：Windows11 x64、MSVC19.44/Visual Studio2022 Build Tools、Windows SDK10.0.26100.0、C++17 Release /MT /W4 /WX /permissive- /analyze /utf-8。UI测试EXE使用既有Common Controls v6/PerMonitorV2/asInvoker清单。所有自动窗口隐藏，未控制Explorer或读真实NAS。

## 真实命令

```powershell
cmake -S tests/windows-gallery -B .runtime/gallery-tests -G "Visual Studio 17 2022" -A x64
cmake --build .runtime/gallery-tests --config Release --parallel 2
ctest --test-dir .runtime/gallery-tests -C Release --output-on-failure
python -I -B scripts/verify_repository.py
```

本机cmake/ctest位于Visual Studio Build Tools的Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin，Python为Codex primary runtime缓存Python；Python进程使用UTF-8。真实源码路径为双层worktree，未在旧V03-019继续写。

## 新增覆盖

- gallery_preview：Page30普通文件、目录不允许预览/Space不导航；Space/按钮激活、CtrlSpace选择、CtrlEnter拒降级、Ctrl/Alt修饰Esc/左右交宿主；Tab跳过禁用Previous。
- 保存原selection/focus/scroll；进入清全部thumbnail、Visible为空、SetThumbnail拒绝；切换释放旧shared像素；关闭精确恢复浏览状态与canvas焦点。
- 1600×800实际GDI fit比例、1600×1600最大10,240,000B、1600×1/1×1600 contain；连续绘制临时GDI对象不增长。
- 非法1601尺寸清旧图、超16MiB capacity拒绝；无图失败状态；旧index/serial/关闭后完成拒绝，隐藏与权限Clear清图，Begin回调可删除Surface且owner归还。
- 旧图库MSAA不可读；预览只一个Graphic和完整当前名字；切换旧preview provider拒读。
- preferencesChanged：默认/no-op不通知，实际Mode/Density各一次，clamp后相同值不通知。
- gallery_preview_external：独立MTA客户端通过File Invoke打开；List30退役后新root为Pane，ListItem=0/Image=1；图片无SelectionItem模式；Native Next/Back Invoke、旧image current Name拒绝、恢复List和原MSAA selection；进程退出后harness严格断owner/provider/queue/dispatcher归零。

首次实现全11/11通过（1.04秒）；新增外部预览1/1通过（0.14秒），修饰键定向1/1通过（0.08秒）。最终完整12/12 CTest通过（1.48秒），0失败、0跳过；verify_repository.py通过（512个架构输入、21个迁移测试、14个架构规则测试，主题/依赖/源边界检查通过，Alpha审计仍为blocked）。旧图库、渲染、budget、UIA512、同STA三轮和受控listener均保留。

## 手动同源入口

`GalleryHarness.exe --show`仍明确标为合成数据。普通文件Space/Enter/双击进入1600px合成预览，上一/下一仅遍历这个固定测试页，目录动作仅记录、不伪造真实Core导航。root执行GUI；本任务未启动可见窗口。

尚未覆盖：真实Core/WIC/命名管道、当前页授权撤销集成、注册表偏好实际持久化/写失败、真正Explorer系统快捷键与包发布。这些属于root与兄弟任务，G4按用户豁免未执行。
