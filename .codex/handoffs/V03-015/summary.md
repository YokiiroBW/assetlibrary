# V03-015 — Windows 连接设置与用户会话后台

状态：ready_for_review。代码提交 `40091921e4249e922f4168959ab2bed8d56d2b25`；分支 `codex/v03-015-windows-connection-session`。主协调线程已收到稳定代码提交用于整包构建。此包不声称完整 V0.3 完成。

## 交付行为

- WinUI 3 / Windows App SDK 2.4.0 stable 的单实例连接设置窗口，只负责 HTTPS NAS、证书 SHA256 指纹、账号、登录/退出、记住登录及每2秒有界读取后台状态。资产日常入口仍是原生 Explorer。原生控件/系统主题、可滚动缩放、密码正常粘贴、焦点顺序和可访问名称保留。
- `AssetLibrary.Host.exe --user-session` 独占本用户、本 Windows session 的生产控制与快照 pipe，未配置时明确 Unavailable；无600秒试验超时。`--shutdown-user-session` 是5秒有界客户端，无Host幂等成功；不会任意按PID杀进程。默认原Proof程序集/`--profile` CLI及Proof endpoint不变；生产发布通过 `AssetLibraryProductionHost=true` 固定WinExe输出名。
- Control v1 使用16KiB内4字节LE长度+UTF8 JSON，拒绝多余/缺失/重复字段、非规范GUID、越界帧及不匹配operation的connection。4 listeners、500ms读帧、12秒操作上限，HTTP沿用8秒。客户端校验服务端进程TokenUser/session并使用Identification SQOS；服务器用实际TokenUser受限DACL、first-instance和拒远程。
- Host拥有唯一Cookie/CSRF/身份快照。状态转移串行、正在登录可取消，切换前清空旧快照，401/403/404和expiry清会话；晚到响应不能恢复旧身份。目录页/权限/身份/路径业务全部复用Core，不新增业务副本。
- 默认不记住登录；勾选才保存CurrentUser DPAPI blob，普通配置无密码；owner/DACL只当前TokenUser且拒绝重解析。退出/拒权/到期删除记住凭据。启动最多自动尝试记住登录一次，失败不循环重试。
- 生产session变化后由有界自有通知helper向固定root发公开SHChangeNotify。父进程只等自有helper最多3秒，必要时终止该helper；通知不阻塞控制请求，Proof不通知。

## 产物与集成

Settings/Host自包含发布成功，产物在本worktree `.runtime/sandbox-storage/V03-015/publish-settings`、`publish-host`。检查189个重复文件SHA256全部一致。发行锁已按root要求放入 `infra/windows-client/locks`，5个组件锁；normal Windows solution锁保持各自TFM。

准确命令见 `apps/windows-client/Settings/README.md`。restore必须显式单数 `-p:RuntimeIdentifier=win-x64`，使Directory.Build.props在评估时选择绝对AssetLibraryReleaseLockRoot。Settings通过FrameworkReference定点固定.NET 10.0.11；禁止向它传全局RuntimeFrameworkVersion（会污染Windows SDK Ref版本）；Host可传全局10.0.11。两个组件统一 `Version=0.3.0-preview.1`。

WinAppSDK是Microsoft EULA而非MIT；14个file-license及SDK BuildTools legacy metadata已由root精确批准，31包审计通过，root提供随包notices。许可清单及发布体积/hash见本目录JSON。

## 验证与边界

最终normal locked restore、format verify、Release build零警告、76项非NativeLive测试通过。架构/重复代码/敏感日志/依赖检查通过。文件与账号安全测试仅用系统临时目录和synthetic HTTP fixture。无资产内容读取/写入，没有注册表/GUI/真实NAS操作；真实Core及Explorer渲染、自动刷新/清空和安装由root统一验收。G4是用户豁免未测，不能当成运行证据。

50万资产场景仍使用现有100条分页/64快照/8192令牌/2网络worker上限。设置状态为O(1)，配置与control正文大小固定，不引入全量扫描、缓存或数据库。无新迁移、公开Core契约、权限模型或第二框架。

## 实机发现后的稳定修复

root实机发现旧Settings缺应用PRI而退出0xC000027B，以及通知helper默认MTA请求STA返回0x80010106。修复提交 `bbe480efe9b5c388860c52d1ee1c8c829b556f60` 已启用资源工具/发布缺PRI硬失败，并以显式STA隔离通知且记录stage/hr。详细证据见 [startup-fixes.md](startup-fixes.md)。修复后GUI和通知由root统一验收，不沿用初版组件构建结果声称实机成功。
