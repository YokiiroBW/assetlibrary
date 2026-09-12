# V03-018 交接摘要

状态ready_for_review。分支codex/v03-018-windows-browse-polish；基线ae59f44；实现commit 96a7814982e63654f3a564b4418ea33173ec5f72。此交接是后续独立提交，合并需包含整个任务分支。

## 完成内容

GetDisplayNameOf对FOREDITING/FORADDRESSBAR请求返回友好名称，包括同时带FORPARSING的0xc000/0xc001组合；生产root返回资产库，子项保留已校验的展示名。纯FORPARSING及INFOLDER|FORPARSING继续生成原自描述PIDL名称，fresh-instance/多层roundtrip及生产/Proof root隔离保持。未调用SetWindowText或添加新Shell属性接口，展示名称不能用于身份解析。

普通CompareIDs先比较当前页显示组：Library/Directory → File/Reparse等普通项 → NextPage，再保留所选名称/类型列与原opaque身份tie-break。CANONICALONLY绕过显示分组和名字，继续按epoch/node/kind及完整相对PIDL链比较。资源库和物理目录同一优先组，重解析点不被误作可进入目录。

运行修改仅apps/windows-shell/ExplorerFolder.cpp；tests/windows-shell/ProductTests.cpp补回归。Snapshot.cpp无需修改。没有改Host、图库、wire/contracts、依赖/CI、版本/安装器或其它任务目录。

## 依据、复用与边界

官方SIGDN将desktop absolute editing（0x8004c000）和parent relative address-bar（0x8007c001）定义为UI友好名称，纯parsing名不适合UI。低位SHGDN修改符需同时处理，不能仅测试FORPARSING便输出GUID+hex。

参考：[SIGDN](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/ne-shobjidl_core-sigdn)、[SHGDNF](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/ne-shobjidl_core-_shgdnf)、[CompareIDs](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellfolder-compareids)。

复用ProductIdentity、既有SnapshotPidl编解码/验证、TypeText、原排序tie-break与无注册COM测试fixture。依赖方向不变：Shell只适配本机快照展示，Core/Host仍拥有授权和数据；没有重复领域规则、跨模块写入、主语言/框架/依赖或数据库迁移。

每次名称处理只消费既有PIDL，排序仍单页最多101项，比较深度最多64段。没有网络、资产I/O、额外IPC或后台工作；50万资产下不新增全量遍历/索引要求。原150ms预算、生命周期/取消、权限与原件安全边界完全未动。

## 验证

严格Release /W4 /WX /permissive- /analyze /utf-8构建AssetLibraryExplorer、AssetLibraryExplorerProof、ExplorerProductTests一次通过。最窄CTest explorer_product通过，0.04秒；同时覆盖两种实际DLL。

新增回归包括10个UI flag组合、5种项目、255 UTF16中文长名称和嵌套目录；纯解析/身份隔离/展示名不能解析；两显示列的目录优先和分页末尾、反向比较符号、同名tie-break、column差异、多层canonical独立。用新测试EXE加载旧V03-016 DLL，按预期exit1并报告product root UI name is friendly，证明新检查能够拒绝旧标题行为。

verify_repository最终通过：437架构输入，21迁移manifest测试和14架构测试通过，Alpha仍blocked。完整本地输出位于.runtime/explorer-browse-polish/verify-repository.log；CTest输出在同构建目录Testing/Temporary/LastTest.log。未为小变更运行整套平台/故障/研究/G4检查。

本次生产DLL位于.runtime/explorer-browse-polish/product-shell/Release/AssetLibrary.Explorer.dll；SHA256为8EABC09C943400541CE16ECF96141FAEC1E9E7F3376336C8DA0AD23001602C30。仅为组件构建标识，最终包应取其新构建hash。

## 风险与合并建议

实际Explorer标题使用何种调用flag尚没有目标进程trace；本任务修复明确的名称契约缺口，不能凭组件测试宣布实际tab/window视觉已经验收。root需按新包观察标题和默认显示顺序。本任务没有GUI、注册或真实资产操作，没有更新已安装preview.2。

CompareIDs公开参数包含列及CANONICAL/ALLFIELDS，不提供升降序输入。本次承诺默认升序比较的当前页分组；Shell若反转比较，不能宣称降序仍将分页固定最后。未扩大到视图排序回调或服务器完整目录排序，也不宣称图库/瀑布流完成。

建议本任务独立合并，root负责最终版本、整包和GUI验收；图库方案与本任务无代码依赖，不应因此等待图库实现。任务状态由主协调登记，不宣布整个里程碑完成。
