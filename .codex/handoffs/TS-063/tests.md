# TS-063 实际验证

代码：`6fcb807b0ccc502ae5bce713da9108c13dc92561`。所有命令在本任务AssetLibrary worktree执行；工具只读复用Python、SDK10.0.111及PG16.15，包缓存和构建在自有.runtime，临时数据库/资产在自有系统临时目录。

## 命令与结果

- `python -I -B scripts/verify_repository.py`：通过。含架构边界、重复代码/敏感日志、23迁移摘要/所有权、21迁移单测、SDK生成同步、14架构规则测试。Alpha验证结果仍blocked，未解除发行门禁。
- `dotnet restore AssetLibrary.slnx --locked-mode`：通过；仅自有NuGet缓存，锁无变更。
- `dotnet format AssetLibrary.slnx --verify-no-changes --no-restore`：通过。
- `dotnet build AssetLibrary.slnx --configuration Release --no-restore`：通过，0警告/错误。
- `dotnet test AssetLibrary.slnx --configuration Release --no-build --no-restore --logger trx --results-directory .runtime/ts063-final-regression`：446通过/0失败/53跳过，进程exit0。AssetLink13、Build2、Packaging63、Preview111、ReadCore57、TaskHealth32、TransferOperation64、WebGateway104。包含新凭据3、新证明1、新运输拒绝1、新库权限3测试。
- `python -I -B tests/integration/tianshu/run_service_read.py --execute --dotnet <SDK10.0.111> --postgres-bin <PG16.15/bin>`：通过；最终`.runtime/ts063-service-b992e258b0cb/service-read.trx`聚合1通过/0失败/0跳过，仅为以下真实断言的容器，不能用计数代替覆盖说明。审阅用脱敏摘要和TRX SHA256保存在evidence.json。
- 完整diff检查、`git diff --cached --check`：通过；生成SDK差异逐项确认只有摘要/元数据；未变历史迁移、锁、其他产品。

## 真实PG/HTTPS覆盖（逐项通过）

| 门禁 | 实際执行证据 |
|---|---|
| 安装/最小数据库角色 | 全23生产迁移真实应用；六模块登录、runtime只能受控EXECUTE，不能直接写ACL/读写credential表，Gateway不能调用Library写函数 |
| 正向只读 | libraries.list/get、entries.browse/get、assets.search，中文检索，137目录项分页100+37且无重复 |
| 主体与库隔离 | 两独立服务主体只见指定库；跨库get404；给两库权限后仍拒绝跨库旧cursor，排序变化旧cursor400，page_size101为400 |
| 认证拒绝与只读调度 | 未知token401；混用Cookie/Origin/Sec-Fetch/多Authorization拒绝；服务不设置Cookie；管理/扫描/预览/文件操作不能到达dispatcher |
| 运输默认关闭 | 独立默认关闭/HTTP/错authority/path/method/空浏览器header单元负例先拒绝，不进入认证存储或dispatch |
| 浏览器兼容 | 真HTTPS登录、原Cookie会话、错Origin/CSRF403、普通读成功、Host重启会话有效；HIBP依赖用原测试响应器 |
| 存在性 | 无权与不存在库404使用原not_found；列表隔离；未授权scope不泄露结果 |
| 库撤权 | 事务提交后下一页browse/get entry/get library均404；list为空；scoped search404，不沿旧页授权 |
| 部分失败 | 初次授权含不存在库整批回滚，空权限主体发起读取无库；已有ACL在失败批次后保持不变，失败无token回执 |
| 幂等 | 相同授权请求单条审计；冲突correlation拒绝；撤权后的旧grant重试state_conflict且不复活权限 |
| 主体边界 | 普通用户、管理员及已禁用主体不能取得service grant；已禁用主体仍可撤权；grant只产生read_only |
| 凭据生命周期 | 默认30天、真实2秒期限过期401（不改机器时钟）；原子rotate后旧token401/新token可读；revoke后401；disable停用所有token |
| Host重启 | 正常服务和浏览器状态持久；过期、轮换撤销、显式撤销、禁用状态跨重启不复活 |
| PostgreSQL重启 | 停止/重启自有PG后凭据/撤销/禁用/可认证计数与ACL快照完全一致 |
| 备份恢复 | 现有正式迁移工具生成验证备份，恢复到另一自有数据库，生命周期/可认证/ACL快照一致 |
| 迁移失败恢复 | 自有staged下一迁移创建表后除零，失败整事务回滚，无半表/状态漂移；当前正式manifest再次应用无待办 |
| 文件与清理 | 扫描138个合成原件，结尾SHA256/mtime全部不变；自有Host/连接/六登录/数据库/PG集群/私密临时目录/证书清理成功 |

单元证明另验证action、authorization/operation/principal/operator/expiry/lifetime绑定、篡改、其他deployment、旧bootstrap证明、key轮换和证明过期均失败。摘要凭据每请求重新调用存储，无成功缓存回退；异常传播由Host安全映射503。

## 缺环境/未执行边界

53项一般套件跳过逐项保存在skipped-tests.json：ReadCore25（专属实库/原生进程fixture或POSIX条件未启用）、WebGateway4（独立PG分页、native长期服务、服务聚合、真实browser UI runner未由普通suite启用）、Preview24（专属图片PG/Host、Linux、symlink或LPAC/AOT fixture未启用）。其中本任务service聚合已用专属runner另行真实通过；其余跳过仍是缺证据，不由本次aggregate替代。未运行真实浏览器UI、Android/Explorer/LPAC发行、完整历史数据库全套、50万计划/P95、NAS、Platform消费者或生产操作。

64KiB请求、100行分页和Host5秒沿现有实现；新夹具结果只做1MiB消费者检查，未验证所有长字段响应的上限或宣称新增服务端上限。未将离线旧列表当授权快照，也未实现Platform缓存策略。

早期验证失败及修复：LF标准化导致内嵌迁移清单旧摘要，被就绪检查拒绝后刷新清单并重建；批准文档更新导致SDK摘要stale，通过原生成器机械刷新；迁移版本断言21更新23；共享token字节实现消除重复源码；运输单元fixture缺服务容器已补齐。最终结果按上列新报告，未保留失败为通过。
