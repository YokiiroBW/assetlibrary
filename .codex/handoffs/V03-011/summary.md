# V03-011 — rendered-view-loading

状态：ready_for_review。代码提交`22700ee`，分支`codex/v03-011-rendered-view-loading`，基线`5424af4`。工作目录`C:/YOKI/Codex/AssetLibrary-worktrees/V03-011`。

## 结果

窗口与站点就绪后启动一次有界观察。每500ms从既有站点取得当前IShellView，核对HWND，通过IFolderView读取条目数（0..101）和首项；复用SnapshotPidl::ReadPidl，仅实际显示的Loading状态行允许Refresh。普通项、错误、未知/畸形PIDL或API异常停止。首次枚举零条目只观察到固定10秒期限，不触发查询，不伪造Core成功。Signal枚举发布仍记录诊断，但不能开始或终止观察。

保留20次观察上限、4个活动名额、owner线程、站点修订/窗口/周期/期限重入检查和COM引用保活。外部Release撤销站点同样不会重开旧循环。停止后F5仍是一次手动查询，重开获得新周期；没有Ready之后的常驻轮询。Core五秒新鲜期、本机IPC150ms及权限语义不变。

私有只读诊断增加`folderview_hr`、`count_hr`、`item_hr`、`rendered_n`、`pidl_valid`、`rendered_kind`和`rendered_status`；固定2048字符边界仍通过。诊断自身不访问资产或请求刷新。

## 验证与产物

- 严格Windows SDK10.0.26100/C++17 Release构建通过，`/W4 /WX /permissive- /analyze /utf-8`。
- 四项受影响CTest全部通过，合计44.96秒；含43.64秒的Loading状态、空枚举真实期限、调用跨期限及重入负控。
- `python -I -B scripts/verify_repository.py`通过：架构、契约、交接、生成SDK、依赖边界；Alpha审计仍为blocked。
- 本机DLL：`.runtime/explorer-rendered-loading/Release/AssetLibraryExplorerProof.dll`；SHA256：`34aa5d26121be3d5bfe46dffe61365a5ffb0a0b5ffa8f9ed64a5fe85b16ec82d`。
- 最终diff审查通过，变更限于Shell实现/测试/说明和V03-011任务交接。无注册、真实GUI或真实Core操作。

## 影响、风险与集成

复用既有站点/原生DefView接线、PIDL校验和有界快照查询，没有复制Core业务规则，无新语言/框架/依赖、数据库、网络或资产I/O。50万资产下每tick仍为本页计数加一个PIDL，O(1)，无新索引。回滚代码提交即可回到原枚举Signal行为。

V03-005已批准更新的共享快照契约是实现依据，本任务没有改写共享契约。建议先集成`22700ee`与本交接提交，再由V03-005使用新DLL执行真实冷Loading到Ready与目录导航验收。

标准system Desktop接线测试仅证明自动SetSite/WINDOWCREATED、活动view记录器及名额回收。既有独立`--proof-owner-lifetime`诊断未重跑，已知exit1/外部CViewSettings持有callback/DLL S_FALSE仍保留；没有强制Release，不宣称G2通过。真实GUI和G2/G3/G4、正式安装包与生产发布不由本任务完成或关闭。
