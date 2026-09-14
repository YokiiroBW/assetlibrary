# TS-063 交接

状态：ready_for_review，仅本地开发完成。分支 `work/ts-063`，基线 `d284f651c520a83027f59870f03bc8ae9701b4e3`，实现提交 `6fcb807b0ccc502ae5bce713da9108c13dc92561`；最终HEAD另含本交接提交。未合并、推送或部署。

默认关闭的独立服务凭据通过精确HTTPS authority/path进入 `/assetlink/v1/control`，仅五个已有read操作。每请求验证PG凭据和主体，每页继续走现有SQL库权限查询。Cookie/Origin/Sec-Fetch/CSRF混用被拒绝，浏览器原登录、会话和CSRF保留。

GatewayAuth拥有非管理员service principal、32字节随机opaque secret的摘要、到期/撤销/原子轮换/禁用及审计。LibraryStorage拥有原library_permission表的原子read_only批量授权和撤权；它只调用批准的Gateway主体锁函数，Gateway不能写库权限。默认30天，上限配置30..365天。新建主体没有权限，赋权失败整批回滚，签发独立执行且只显示一次secret。

本地operator读取受保护key，并以独立服务动作、版本和purpose签名全部管理字段，不能挪用bootstrap/recovery证明。三个sealed token类型共享字节所有权、编码和清零实现；认证用例仍接受各自准确类型，未共享browser_session状态。

只新增迁移0022/0023，历史SQL/roles/依赖锁不变。认证附录使SDK契约摘要变化，按协调补充批准运行原生成器更新7个SDK摘要/生成元数据文件；三个生成源码仅首行摘要不同，schema/body/SDK运行逻辑不变。同步Python和Packaging迁移版本断言23。

验证详见 [tests.md](tests.md)、[evidence.json](evidence.json)。.NET全套446通过、0失败、53环境/平台跳过，逐项见[skipped-tests.json](skipped-tests.json)。真实临时PG16.15、六个最小模块登录、Host pinned HTTPS另行通过逐项业务和负例；138个合成原件hash/mtime不变，自有Host/PG/数据库/角色/证书临时目录清理完成。HIBP为已有替身，浏览器认证HTTP回归为真实；没有真实浏览器UI、Platform消费者、NAS或生产验收。

保留边界：撤权前已在途快照可能完成；发行Alpha门禁仍blocked；50万规模/P95、消费者1MiB及真实双产品联验后续。签发回执丢失时不重显secret，已知principal可禁用并明确建立替代接入。操作步骤见 `docs/integrations/tianshu/service-read-operator.md`。

建议协调先审查新增角色函数/凭据生命周期和真实负例证据，再串行合入；Platform消费者另开任务，不能由本任务宣称资产平台已接通。完整architecture_review在result.json中。
