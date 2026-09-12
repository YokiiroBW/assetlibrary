# Explorer 受控 AssetHost

当前生产`--user-session`提供只读快照、连接控制及[派生缩略图](THUMBNAILS.md)。
生产入口状态与实机证据由上层Windows发布说明管理；以下保留V03-010的原始Proof使用说明。

## V03-010 原始Proof说明

V03-010 为 test-only 原生 Explorer 命名空间提供进程外只读页面。复用现有 Windows Core 适配器与生成 AssetLink SDK，不在 Explorer 中加载 .NET、HTTP、凭据、数据库或媒体处理。协议由 `contracts/windows-shell/read-only-snapshot-v1.md` 冻结；此入口不解除正式 Shell 发布门禁。

构建和测试使用 `apps/windows-client/README.md` 的独立 Windows solution 命令。产物为 `AssetHost/bin/Release/net10.0/AssetLibrary.Windows.AssetHost.dll`，以及同目录生成的 Windows 可执行入口。选择仓库 `global.json` 冻结的 .NET SDK/运行时。

```powershell
dotnet apps/windows-client/AssetHost/bin/Release/net10.0/AssetLibrary.Windows.AssetHost.dll --profile C:\private-fixture\connection.json --lifetime-seconds 600 --stop-file C:\private-fixture\host.stop
```

参数只接受私密连接文件路径、可选运行秒数（1..3600，默认 600）及可选停止文件路径。不要把密码、Cookie 或服务地址放入参数。私密文件由受控 Core/PostgreSQL 支架生成；支持它的 origin/certificate_sha256/account_name/password，以及已知的 invisible_account_name/invisible_account_password/library_id/sample_file_count/expires_at 元数据。文件最大 16 KiB，重复字段、未知字段、错误类型、非法 HTTPS 地址或凭据字段失败即停止。Host 不创建或删除这些输入文件。

主进程先登录，再独占预留当前实际 TokenUser SID 和 Windows 会话对应的本地命名管道。至多 4 个连接；每个交换含慢客户端排空等待共 500 ms。DACL 只授权实际用户 SID，拒绝远程和其他会话；不依赖 `CurrentUserOnly` 的令牌 owner 推断。第二个 Host 不能接管已有端点。命名空间客户端还须核验服务进程 SID/会话并使用 Identification 级别打开。

管道处理仅返回内存结果，最多 2 个独立 Core 查询。每页最多 100 条及 1 个显式“下一页”，保存最多 64 个页面和 8192 个不透明条目/位置令牌。超过容量时更换 epoch 并要求重开根。相同请求合并，成功结果 5 秒后只能返回 Loading 并重新查询，错误回退间隔 1 秒，不回放陈旧数据。401/403/404 与会话到期清空缓存、取消旧请求、换 epoch，禁止自动重登；410 游标到期换 epoch，允许重新打开根后取得新页。

Shell 对 Loading 页面做有界自动刷新（500ms、10秒/20次、最多4视图）；错误或超时后仍可F5/重新打开。已经由 Explorer 绘制的名称不会在权限变化时推送消失。Library/Directory/NextPage 可导航；File/Reparse 仅展示，不开放、写入或跟随链接。退出会取消并在明确期限内等候所属任务，最多尝试 3 秒服务端 logout；`server_unconfirmed` 与本地 `local_clear` 分开记录。日志只有 operation/status/elapsed_ms，不记录连接内容、条目名、Core ID、路径或异常正文；慢日志不能延长快照有效期。`host:ready` 表示管道已就绪，首个 Core 页面仍可能 Loading。

新测试位于 `tests/windows-client/Host*.cs`。普通测试的真实命名管道使用随机后缀，不占用默认端点。`HostLiveTests` 使用 `ASSETLIBRARY_NATIVE_TEST_PROFILE` 和真实 HTTPS Core 验证库、100 项跨页、相册/夏日嵌套导航及无权限账号；没有支架时明确 Inconclusive，不能作为通过。C++ 实际进程互通、真实 Explorer 画面、原文件哈希和受控注册清理由主协调统一验收。

生产登录/设置/自动启动、安装签名、预览/文件打开、推送失效、G2/G3/G4 全部证据仍属后续工作。

连接被接受后，即使managed状态已为Broken/IsConnected=false，也总是先Disconnect再重用。接受阶段失败停止监听并退出Host，避免对损坏实例空转；坏请求断开后退避100ms，单监听器错误日志最多每秒一条。
