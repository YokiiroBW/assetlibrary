# 匹配绑定调用的完整请求IID

按根审查的严格元数据增强版执行同一官方场景一次：仅输出已读取请求IID的完整GUID，不新增目标读取或改变匹配、生命周期、上限。观察器SHA256 8BB9D85A2FE7D910B7B7DB3D917FCFD1989BFBE344235AF1D70E725EC789AB1A；原822FEE轮保持不变，原F298/四根/双通知STA/600秒也不变。

新自有3147562/PID32356，creation134334883960723909，epoch在CUA创建前记录；SDK/ThisPC实际view与唯一身份正控通过。新plan 5f68953ee5ff489ba557ccfa238e1e3b在固定iid目录。ready tick253947078，记录UTC04:34:59.2676638Z，包装立即联动唯一Browse，提交04:34:59.620Z/tick253947453、官方42B PIDL/flags1。

| callId | requestedIid | pbc存在 | 实际HRESULT | 输出参数存在 / 接口null |
| --- | --- | --- | --- | --- |
| 1 | `{BC110B6D-57E8-4148-A9C6-91015AB2F3A5}` | false | 80070490 | true / true |
| 2 | `{886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99}` | true | 80004002 | true / true |

两对匹配都在tick253947453，与旧other类别轮的返回一致。这里只记完整元数据，具体IID由主协调按SDK/官方文档解释，不直接把失败当作必需IShellFolder请求失败；未读取pbc内容或修改关联。

随后首机会E06D7363记录与callback health保护导致约29.750秒提前结束，2入口2返回/149非匹配，不是完整60秒。初始running状态查询失败保留，之后成功暂停于同一拥有目标，两个断点删除/数量0/Detach均S_OK，post-detach查询无debugger且同进程存活；passed=false与cleanupVerified=true分开记录。

确认脱离后关闭自有无关联模态3279078，原Browse控制器已达10秒外限，无browse.return HRESULT。注册有效期内view仍ThisPC20D04…/22B，与官方42B不匹配；ThisPC读回成功/2项，观察器与目标样例模块快照均未见样例。没有提升异常或计数上限。

stop/finally于04:37:53.8226721Z正常exit0，4CU/4LM全absent、双通知清理成功。自有3147562及模态关闭，原1247028和无关Chrome窗口保留。[原件索引](bind-iid-control/evidence.json)含计划/包装、实际匹配、早停/清理、有效期view/模块与现场清理。根因和G1仍未关闭，不追加变体。
