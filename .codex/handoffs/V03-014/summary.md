# V03-014 — G3 真实进程计时与生命周期量测

状态：ready_for_review。完成测量组件及回归，真实Explorer的G3裁决仍由V03-005协调者负责；没有执行G4或关闭任何门禁。

分支：`codex/v03-014-g3-measurements`。实现提交：`87a882a343f5fc4019b19ab6ace5005d7e05ba3a`；本交接单独提交，依次合入实现及交接即可。
验证DLL：`.runtime/explorer-snapshot/Release/AssetLibraryExplorerProof.dll`，SHA256 `4AE3477602D7DDF15659A9C86C17C443630A986A933C6803CE17FF00C15184A5`。

## 完成内容与边界

- 保留原ProbeDiagnostics pid1/v1；相同fmtid新增pid2/v1，只接受空item，返回≤2048字符数字JSON。原未知pid2测试改pid3。读取只取固定内存计数和QPC，无枚举、查询、Refresh、文件或网络副作用。
- 全模块记录EnumObjects、Query、既有UI Refresh调用次数、活动数、最近起止/持续时间、最大持续时间和线程，避免活动view clone自身未枚举而漏掉真实调用。
- 每view记录初始观察QPC与首个实际有效非状态Ready PIDL的观察QPC/线程。空视图、错误行、Signal的Ready发布不能冒充RenderedReady。
- 实际CancelIoEx次数、延后回收次数、资源完成次数、在途计数和从取消到资源析构结束的时间；原objects/locks/LiveCallbacks/ActiveViews计数与操作pin交还阶段独立输出。

字段、单位与使用限制见 `tests/windows-shell/README.md` 的G3表。`hz`为每秒ticks；秒数=持续ticks/hz。字段为非事务快照，并发完成可能让各最近元数据交叠，即使随后live归零也不能把并发的end-start当真实单次耗时；独立last/max均来自一次实际调用。读取者必须核对pid属于目标Explorer。

## 资源释放决策

协调者已批准最小顺序变化：保存module与cancel起点，delete Operation（wait、缓冲、pipe/event/peer及对象存储全部结束），然后记录资源完成并active--。
同步deleter以FreeLibrary为最后动作；DLL内Query唯一调用者是持有有效Folder COM引用的EnumObjects，该引用保活调用者。其余调用者为SnapshotTests/FaultHarnessTests静态EXE，无新增导出入口。
异步保留原完成事件与线程池等待，仍以FreeLibraryWhenCallbackReturns作最后交还；pins/pin_sync/pin_pool只能证明本组件持有/交还安排，不能说明OS已经卸载DLL。

保留Query150ms/4操作，观察500ms/10秒/20次/4视图，Core5秒有效期、错误状态、重开和Refresh策略。无新线程、锁、trace、I/O日志、额外依赖或强Release。

## 验证与实际数据

严格Release `/W4 /WX /permissive- /analyze /utf-8`构建通过。六项相关CTest通过，含真实本地pipe超时/取消/恢复、四操作上限、ReadG3无副作用/边界、Loading及受控owner最后Release/卸载。完整命令及中间失败修复见 `tests.md`。

本次组件样本：query_n=12、cancel_n=7、reap_n=7、defer_n=0、op_live=0、pins=0；最大取消资源回收614 ticks，hz=10000000，即0.0614ms。实际未出现延后回收，不伪造该路径证据。
G3最宽注入输出1170字符；把全部数字均扩到uint64最大宽度后的保守上界1411字符。六项总耗时46.65秒。

默认Loading测试的ControlledDllOwner完整释放断言通过。系统Desktop owner wiring仍保留callback1，但该owner不是proof DLL；不能据其DLL S_OK声称真实proof全部释放。历史proof-owner保留问题及可选研究失败未重跑，也未改成通过。

## 架构、安全、性能与兼容性

范围仅`tests/windows-shell/**`及本任务包/交接，复用原快照、PIDL、回调/预算、COM和CTest。无Host/Core/registry/共享协议/数据库/权限模型变化，不复制业务规则。
首次扫描、原资产、真实Explorer窗口均未接触。固定计数空间为O(1)，不随50万资产增长，不枚举额外条目；原分页和IPC资源界限保留。
原诊断键保持兼容；新增键属于未注册test-only诊断，不是公开产品契约。架构/契约/SDK及仓库验证通过，Alpha发布保持blocked。

## 合并与后续

先合入实现提交，再合入本交接提交；无需依赖其它新任务。协调者使用核验SHA的相同DLL执行预定五个真实恢复样本，核对实际PID、250ms返回、取消资源回收≤2000ms、重开根初始观察至RenderedReady≤10秒和对象是否回归/固定有界。
后两界限是本次夹具预定义口径，未更改门禁原条款。真实G3样本、合法外部COM持有和最终卸载仍未裁决；不执行G4，不能宣布里程碑完成。
