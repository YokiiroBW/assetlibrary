# V03-018 测试记录

日期2026-09-13，Windows x64，MSVC19.44.35228.0（工具目录14.44.35207），SDK10.0.26100.0。
工作区C:/YOKI/Codex/AssetLibrary-worktrees/V03-018。仅当前任务2个源码/测试文件及任务/交接包发生变化。

## 实际命令

```powershell
$cmake='C:/Program Files (x86)/Microsoft Visual Studio/2022/BuildTools/Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe'
$ctest='C:/Program Files (x86)/Microsoft Visual Studio/2022/BuildTools/Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/ctest.exe'
$python='C:/Users/Administrator/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe'
& $cmake -S tests/windows-shell -B .runtime/explorer-browse-polish -G 'Visual Studio 17 2022' -A x64
& $cmake --build .runtime/explorer-browse-polish --config Release --target AssetLibraryExplorer AssetLibraryExplorerProof ExplorerProductTests --parallel 2
& $ctest --test-dir .runtime/explorer-browse-polish -C Release --output-on-failure -R '^explorer_product$'
& $python -I -B scripts/verify_repository.py
```

所有上述最终命令通过。构建保留/W4 /WX /permissive- /analyze /utf-8，生产/MT、Proof/MD不变；没有降低告警或关闭分析。CTest 1/1通过，0.04秒（总0.05秒），失败0/跳过0。

## 有区分力的覆盖

- 两实际DLL各自检查10种UI组合（NORMAL、INFOLDER、FOREDITING、ADDRESSBAR及相应FORPARSING组合），五种普通项目；UI名称保持中文/空格/emoji，不出现GUID/hex。
- 生产root友好名与纯parsing root分别断言，嵌套255 UTF16名称不截断；纯FORPARSING与INFOLDER解析在新Folder实例恢复完整PIDL，展示名不能冒充身份。
- 两显示列让名称zz/高nodeID的库/目录仍先于00/低nodeID的文件、链接及分页；文件/链接先于名称00的下一页。这些样例与旧名字/类型/ID顺序相反。
- 逆向CompareIDs符号、相等自比较、普通组内name/type列差异、同名opaque tie-break保持；canonical顺序特意与显示组相反，并验证名称变化不改变canonical等同、多层链仍被遍历。
- 既有ProductTests同时继续验证生产/Proof CLSID、pipe组成、绝对解析单项/嵌套roundtrip、互拒对方root、设置命令负控和COM owner生命期。

## 旧实现负控

实际执行本次新ExplorerProductTests.exe，参数为旧V03-016构建目录中的生产DLL与Proof DLL：

```powershell
& .runtime/explorer-browse-polish/Release/ExplorerProductTests.exe 'C:/YOKI/Codex/AssetLibrary-worktrees/V03-016/.runtime/explorer-product-tests/product-shell/Release/AssetLibrary.Explorer.dll' 'C:/YOKI/Codex/AssetLibrary-worktrees/V03-016/.runtime/explorer-product-tests/Release/AssetLibraryExplorerProof.dll'
```

按预期exit1：ProductTests: product root UI name is friendly。未修改旧worktree。这是旧行为负控，不是新实现遗留失败；不把此次失败当作待修构建问题。

## 仓库/架构与产物

最终verify_repository通过，437个架构输入，497个C#文件原策略通过；21个迁移manifest/14个架构测试通过，SDK、依赖与源约束保持，Alpha decision=blocked。

最终diff审查确认只改变展示请求和当前页CompareIDs普通分支；canonical、身份解析器、Host/IPC、权限、网络/资产I/O、依赖方向与版本没有改动。原SnapshotTests内type列负控的Library/Directory在同一新显示组，原语义保持，不需改动其fixture。

生产DLL SHA256：8EABC09C943400541CE16ECF96141FAEC1E9E7F3376336C8DA0AD23001602C30。
产物：.runtime/explorer-browse-polish/product-shell/Release/AssetLibrary.Explorer.dll。

## 未执行

无真实GUI、注册、Host启动/连接、NAS访问或资产操作。没有重跑原六项故障/时限测试、全平台套件、研究诊断或G4；本次仅显示名称/比较且相关组件已覆盖，未改那些路径。

没有实际Explorer标题调用flags或最终渲染验收；公开CompareIDs没有降序输入，未保证Shell降序时分页固定末尾。服务器完整目录排序和真正图库瀑布流不在本任务结果内。
