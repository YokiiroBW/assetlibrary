# TS063 本地只读服务接入管理

此功能默认关闭。仅在受保护的本机配置中设置 `service_read_enabled:true`；`service_read_maximum_lifetime_days` 默认365，可设30..365。默认签发30天，`lifetime_days` 可请求1天至配置上限。不要将服务凭据放进浏览器、命令行、日志或库文件。

使用现有 Host 的 `--trial-operator <私密配置路径> <动作>`，通过 stdin 传一个不超过16KiB的JSON对象。沿用部署私密目录、证书、受保护operator key和最小数据库连接；必须先通过原有 `initialize-key` 初始化本地key。该key的读取权限是本机操作者信任边界；operator_id是受证明绑定的审计标签。

所有动作必填：`authorization_id`、`operation_id`、`principal_id`（非空UUID）、`operator_id`（最多200字符）、`expires_at`（证明到期UTC，未来15分钟内）。证明期限与服务凭据期限不同。完整请求包括动作、库集合、目标和期限都进入新的服务证明签名域；不能使用bootstrap/recovery证明。

| 动作 | 附加字段 | 效果 |
|---|---|---|
| service-create | display_name | 创建没有库权限/凭据/本地登录的独立非管理员主体 |
| service-grant | library_ids（1..100个唯一UUID） | 单事务赋予原library_permission表read_only；任一未知库整批回滚 |
| service-ungrant | library_ids | 单事务撤回指定库权限；主体已禁用仍可撤权 |
| service-issue | lifetime_days可选 | 默认30天；成功仅一次返回token、credential_id、expires_at |
| service-rotate | credential_id，lifetime_days可选 | 原子签发替换凭据并撤销旧凭据 |
| service-revoke | credential_id | 即时撤销指定凭据，其他凭据独立 |
| service-disable | 无 | 禁用主体并撤销所有凭据；不可由这些动作重新启用 |

顺序为创建主体、原子批量赋权、单独签发。不存在一个声称“已创建并授权”的部分成功回执：创建成功只代表空权限主体，赋权成功只代表其库集合提交，签发才返回secret。初次赋权失败保持空权限主体且不返回secret；检查输入后用明确请求重试。跨库失败不撤销已有合法权限。关闭功能后仍可执行撤权/撤销/禁用，但不能签发。

逻辑重试复用operation_id和原请求；authorization_id/证明期限可更新。创建重试还必须匹配原主体、显示名和操作者。库授权重试匹配同操作者/operation_id/主体/规范化库集合/动作，若后续动作改变了权限则返回state_conflict，不能复活被撤回的权限。

签发或轮换遇到不确定结果时，不重新显示或重新返回secret；相同operation_id重放返回state_conflict。若已知旧credential_id可检查本机操作记录后处理；若成功回执丢失且未知新credential_id，禁用已知principal_id并创建替代主体、明确赋权和签发。不要把新一次签发误称原请求幂等成功。撤销/禁用可安全重试，主体和凭据生命周期保存在PG内，服务重启不会复活它们。

客户端只发单个HTTPS Bearer请求，不发Cookie/Origin/Sec-Fetch/CSRF。五个read操作与原body相同；每页重新授权，401/404后应清除相应旧页和凭据状态。总体5秒、body64KiB、页100沿用现有边界。长路径结果可能超过Platform建议1MiB，尚未完成真实双产品联验，不宣称服务新增1MiB响应上限。

新迁移仅0022/0023及manifest，使用现有迁移工具的更新前备份和验证恢复；不降级运行旧Host绕过最新迁移就绪检查。库权限写入只经LibraryStorage受控函数，Gateway没有跨模块写表授权，凭据只保存32字节SHA256摘要。所有管理记录只存操作者、目标、动作、关联ID和时间，不存secret。

本轮实际证据见 `.codex/handoffs/TS-063/tests.md`；Platform消费者、真实NAS与生产部署不属于本任务。测试命令见 `tests/integration/tianshu/TS063.md`。
