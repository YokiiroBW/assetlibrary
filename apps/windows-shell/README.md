# Windows Explorer 只读浏览组件

Windows 11 x64 的原生资产库入口，C++17 / Windows SDK 10.0.26100.0 / 系统 DefView。
V03-016 将已验收的 test-only 实现移入此目录作为唯一生产源；并非重写浏览实现。
`tests/windows-shell` 的原 Proof DLL、Probe 和六项 CTest 继续引用本目录。

```powershell
cmake -S apps/windows-shell -B .runtime/explorer-product -G "Visual Studio 17 2022" -A x64
cmake --build .runtime/explorer-product --config Release --target AssetLibraryExplorer --parallel 2

cmake -S tests/windows-shell -B .runtime/explorer-product-tests -G "Visual Studio 17 2022" -A x64
cmake --build .runtime/explorer-product-tests --config Release --parallel 2
ctest --test-dir .runtime/explorer-product-tests -C Release --output-on-failure
```

生产输出 `.runtime/explorer-product/Release/AssetLibrary.Explorer.dll`。构建保留 `/W4 /WX /permissive- /analyze /utf-8`。
生产 DLL 及其静态库统一 `/MT`，无需外置 VC Redistributable；Proof 保留原 `/MD`。
COM 边界仅使用系统接口、CoTaskMem 与 BSTR，没有跨 DLL 的 STL/CRT 对象所有权。
直接导入由 `explorer_product_imports` 限定为 ole32、oleaut32、shell32、user32、shlwapi、advapi32、comctl32、kernel32。
没有新增第三方包；MSVC runtime 按编译工具链再分发条款静态链接，工具链安全补丁更新需重建组件。

产品只展示授权资源库、物理目录和每页最多100条及一个“下一页”项目；名称与类型列排序仅限当前页。
目录双击/Enter 在当前窗口打开，图标来自系统资源。文件与链接不打开内容，不提供传输、改名、删除、粘贴或伪造成功操作。
Explorer 不发网络请求、不读取资产、不哈希或媒体解码，不加载 .NET、WinUI、数据库或 Provider。
它使用已冻结的只读快照 wire v1 和 PIDL，保持150ms总等待、四项IPC名额及已确认的取消资源保活。

右键资产库视图的空白处选择“连接设置”，以固定绝对路径启动当前 DLL 同目录的 `AssetLibrary.Settings.exe`，无额外参数。
不查询任意URI、不使用命令解释器/PATH、不继承句柄、不等待设置程序输入空闲。设置程序需由安装器与 DLL 放在同一版本目录。
启动失败向 Shell 返回真实 HRESULT；尚未登录或连接失效时，状态行明确提示设置与重新打开资产库。
F5只有一次快照查询，不能宣称它会重启已结束的有限Loading观察。

## 身份与安装字段

| 字段 | 生产 | 兼容 Proof |
|---|---|---|
| CLSID | `{BBC992DE-CE5D-48C8-A86C-7230C7D72B02}` | `{4FF8301D-2E73-4D49-9FE5-868D5F1EA302}` |
| 标题 | 资产库 | AssetLibrary 集成验证 |
| DLL | AssetLibrary.Explorer.dll | AssetLibraryExplorerProof.dll |
| pipe前缀 | `AssetLibrary.Explorer.v1.` | `AssetLibrary.ExplorerProof.v1.` |

两种 pipe 均追加实际 TokenUser SID、句点与Windows session id；绝不互相后备。
生产 owner 为 `AssetLibrary.Windows.Explorer`，版本由安装组件统一固定为 `0.3.0-preview.1`。
安装器负责每用户、包外真实注册；本项目无自注册导出、注册表写入或安装脚本。
`HKCU\Software\Classes\CLSID\{生产GUID}` 的默认标题与 owner，`System.IsPinnedToNameSpaceTree=1` (DWORD)，
`InprocServer32` 默认值为当前版本 DLL 绝对路径、`ThreadingModel=Apartment`，
`ShellFolder\Attributes=0xA8040000` (DWORD)，`DefaultIcon` 可复用系统 `shell32.dll,3`。
同时在 `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace\{生产GUID}` 注册标题与相同owner；
安装器需保留拒覆盖、HKLM冲突检查和按owner清理。

## 会话失效通知

生产 Host 在连接状态提交或会话/缓存/epoch清空后对生产root发 `SHCNE_UPDATEDIR`，使用
`SHCNF_IDLIST | SHCNF_FLUSHNOWAIT`；wire不增加消息。所有生产视图通过公开
`SFVM_GETNOTIFY` 监听同一根，callback独立持有root PIDL至析构。
`SFVM_FSNOTIFY`仅接受同一root及该事件，合并排队到自有UI消息，重新验证线程、site、窗口与观察代次后操作。
根视图Refresh；子目录视图通过 `SBSP_ABSOLUTE | SBSP_SAMEBROWSER` 回根，避免活动视图留下旧身份条目和面包屑。
目录导航失败会尝试刷新旧epoch，使Host拒绝旧内容，但不能据此声称面包屑已清理成功。
系统Explorer的历史记录不由扩展清除；旧位置始终受Host epoch拒绝。

事件可重启已结束的500ms/10秒/20次Loading观察；活跃观察期间的通知不会延长截止时间。
同一待处理事件合并；这是状态变更通知，不是轮询/心跳。生产最多四个活动视图，超额返回`ERROR_BUSY`，
不能降级为不接收失效通知的视图。Proof不接入此通知，继续原有测试行为。
无注册的COM记录器只验证机制，Host到真实Explorer的root/目录失效与设置启动仍须整包验收。

公开依据：[CreateViewObject](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellfolder-createviewobject)、
[GETNOTIFY](https://learn.microsoft.com/en-us/windows/win32/shell/sfvm-getnotify)、
[FSNOTIFY](https://learn.microsoft.com/en-us/windows/win32/shell/sfvm-fsnotify)、
[SHChangeNotify](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shchangenotify)、
[CreateProcessW](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-createprocessw)。

## 迁移映射与边界

原 `tests/windows-shell/ExplorerProof.cpp` → `ExplorerFolder.cpp`，`ExplorerProof.def` → `Explorer.def`；
Snapshot、SnapshotPidl、SnapshotPipe、SnapshotIcon、NavigationMenu、LoadingRefresh、ProbeDiagnostics、G3Measurements的cpp/h同名移入本目录。
新增 ProductIdentity 仅选择构建身份，SettingsMenu/SettingsCommand仅适配固定设置入口。
G1/G2/G3已验收、G4由用户豁免未测；新产品DLL仍以自身hash参加整包验证，不代替完整V0.3或长时稳定性结论。
