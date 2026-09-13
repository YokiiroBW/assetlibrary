# TS-060 验证入口

所有命令从 TS-060 AssetLibrary worktree 根执行；不接受 NAS 地址或现有数据库。候选校验与真实 Core 请求各自报告，不能相互替代。

## 候选测试

使用 Python 3.12+。`jsonschema==4.26.0` 仅为测试工具，安装在本任务忽略的环境，不改产品锁文件：

```powershell
python -m venv .runtime/ts060-venv
.runtime/ts060-venv/Scripts/python.exe -m pip install jsonschema==4.26.0
.runtime/ts060-venv/Scripts/python.exe -I -B -m unittest discover -s tests/integration/tianshu -p test_*.py -v
```

测试校验提案结构、合成实例、引用与哈希关系、缺失能力、越权字段、动画/sidecar保留语义；没有调用分析服务，不意味着查重/分类/优选已实现。

## 真实 Core HTTP

复用 `tests/integration/native-clients/serve.py` 的真实 `TrialHostFactory`、HTTPS、持久认证、21份生产迁移、六个最小权限角色和真实扫描子进程。只在自有临时 PostgreSQL16.15及合成138文件上运行。HIBP沿用该测试程序集既有响应替身；认证逻辑和数据库均是真实实现。

使用 `global.json` 的 .NET10.0.111：

```powershell
dotnet restore tests/dotnet/AssetLibrary.WebGateway.Tests/AssetLibrary.WebGateway.Tests.csproj --locked-mode
dotnet build tests/dotnet/AssetLibrary.WebGateway.Tests/AssetLibrary.WebGateway.Tests.csproj --configuration Release --no-restore
python -I -B tests/integration/tianshu/probe_core.py --execute --dotnet <exact-dotnet.exe> --postgres-bin <PostgreSQL16.15/bin>
```

探针自行创建本任务 `.runtime/ts060-http-*/`，不接收任意服务器、账号或数据目录。静态目录只有明确标记的测试HTML壳，以满足Host配置；**没有浏览器/Web构建或前端验收**。客户端从本次私密会合文件取得合成账号及叶证书pin：无凭据探测取得证书，SHA256相符后才以正常主机名/有效期验证发送HTTP；不修改系统信任。请求有超时和正文上限，不输出会话/口令或原始响应。

真实检查：401、授权库发现、100+37分页且ID不重复、游标范围/越界拒绝、稳定详情不虚构哈希、中文搜索、首次扫描已提交、五个候选操作名返回400 `unsupported_operation`、Origin/CSRF拒绝、真实普通账号空列表/404/403、两账号退出撤销。五个候选名仅为缺失路由探测，不发布为实际API。

最后必须等待现有fixture确认原件SHA256/mtime不变、临时库/六角色删除、监听和Host/PG退出、私密目录清理，再输出 `http-checks.json`。只有READY或只通过HTTP不算成功。失败保留本次运行日志供定位；不得提交 connection.json、PG状态、Cookie或密码。跨进程崩溃/生产耐久、平台连接、真实NAS及图片解码仍未验收。

## 已有计划沙箱

```powershell
dotnet restore tests/dotnet/AssetLibrary.TransferOperation.Tests/AssetLibrary.TransferOperation.Tests.csproj --locked-mode
dotnet build tests/dotnet/AssetLibrary.TransferOperation.Tests/AssetLibrary.TransferOperation.Tests.csproj --configuration Release --no-restore
dotnet test tests/dotnet/AssetLibrary.TransferOperation.Tests/AssetLibrary.TransferOperation.Tests.csproj --configuration Release --no-build --no-restore --logger "trx;LogFileName=ts060-plans.trx" --results-directory .runtime/ts060-plans
```

真实应用端口配合测试沙箱的文件I/O，覆盖确认、重放、源变化、冲突、权限、保护、容量、租约、恢复等。`MemoryOperationStore`、授权/空间故障注入和测试事件sink为替身；不能称为生产计划HTTP、真实授权配置、持久数据库幂等或归档发布。

根准备检查仍为 `python -I -B scripts/verify_repository.py`；合并前另外运行 `git diff --check`。未修改应用/生成SDK/锁或迁移，未扩展到无关Web/Android/Explorer与全产品构建。产品完整 fast-merge、相关平台和生产发布门禁由集成人按实际合并范围处理，不能用本任务局部验证关闭。
