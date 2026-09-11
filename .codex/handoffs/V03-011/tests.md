# V03-011 验证

日期：2026-09-12。代码提交：`22700ee`。所有命令位于独立V03-011 worktree。

## 已通过

构建使用Visual Studio2022 BuildTools自带CMake/CTest，Windows SDK10.0.26100、MSVC19.44、C++17：

```powershell
cmake -S tests/windows-shell -B .runtime/explorer-rendered-loading -G "Visual Studio 17 2022" -A x64
cmake --build .runtime/explorer-rendered-loading --config Release --parallel 2
ctest --test-dir .runtime/explorer-rendered-loading -C Release --output-on-failure -R '^explorer_(loading_refresh|loading_view_wiring|navigation_menu|diagnostics)$'
python -I -B scripts/verify_repository.py
git diff --check
```

| 检查 | 结果 |
| --- | --- |
| Release `/W4 /WX /permissive- /analyze /utf-8` | 通过 |
| explorer_navigation_menu | 通过，0.02秒 |
| explorer_loading_refresh | 通过，43.64秒 |
| explorer_loading_view_wiring | 通过，1.28秒 |
| explorer_diagnostics | 通过，0.02秒 |
| 仓库架构/契约/生成/依赖/交接校验 | 通过；Alpha仍blocked |
| 最终diff边界与空白检查 | 通过 |

加载测试覆盖实际Loading到Ready/拒权、全错误状态停止、矛盾Signal、尚无发布的初始观察、两view独立、0/101/102条目边界、负数与失败计数、S_FALSE拒绝、Item失败/空/畸形PIDL、Refresh失败、过期旧timer、500ms节流、10秒期限与20次上限、调用跨期限、站点撤销/替换、窗口销毁和释放回调期间重入。Read/QueryInterface/ItemCount/Item仅读取内存记录器；对空视图使用真实十秒期限，确认没有Refresh。

实际DefView接线测试使用system Desktop owner、隐藏且本任务拥有的窗口和IFolderView记录器。验证系统自动SetSite/WINDOWCREATED、UI线程Refresh和活动名额回收；不是实际Explorer/Core数据验收。

原始CTest日志位于`.runtime/explorer-rendered-loading/Testing/Temporary/LastTest.log`。DLL SHA256：`34aa5d26121be3d5bfe46dffe61365a5ffb0a0b5ffa8f9ed64a5fe85b16ec82d`。

## 未扩展与未通过边界

快照IPC代码/输入未变，本次未重跑独立`explorer_snapshot`管道套件；四项受影响测试全部真实运行，没有用skip表示通过。没有执行真实Explorer注册/桌面/真实Core验收。

既有`--proof-owner-lifetime`严格诊断未重跑，已记录的exit1、外部CViewSettings保留callback及DLL S_FALSE仍是未关闭G2证据。其单独诊断实现和失败判断保持原样，无强制Release或卸载成功宣称。G2/G3/G4与正式生产发行仍开放。
