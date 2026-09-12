# V03-015 验证

日期2026-09-12，Windows，精确SDK10.0.111。dotnet路径按任务包；所有以下结果来自本worktree。NativeLive实机集成由root独占，未以跳过记成通过。

| 检查 | 结果 |
|---|---|
| normal Windows solution `restore --locked-mode` | 通过 |
| `dotnet format apps/windows-client/AssetLibrary.Windows.slnx --verify-no-changes --no-restore` | 通过；工作区加载提示警告，但构建无warning/error |
| `dotnet build ... --configuration Release --no-restore` | 通过，0 warning / 0 error |
| `dotnet test ... --configuration Release --no-build --no-restore --filter "TestCategory!=NativeLive"` | 76通过、0失败、0跳过；新增19实际用例 |
| `python -I -B scripts/verify_repository.py` | 通过；包含架构/源码/SDK/迁移等，Alpha仍blocked |
| `dotnet package list --project ... --include-transitive --vulnerable --no-restore --format json --output-version 1` | 6项目，无报告漏洞 |
| `python scripts/validate_dotnet_dependencies.py --solution apps/windows-client/AssetLibrary.Windows.slnx --packages-dir .runtime/nuget --vulnerability-report .runtime/windows-vulnerabilities.json` | root许可证132571b后通过，6项目/31锁定包 |
| Settings/Host发行 `restore --locked-mode` + `publish --no-restore` | 通过；Version0.3.0-preview.1/win-x64，绝对发行锁目录 |
| 两发布目录重合文件SHA256核对 | 189个相同文件全部一致 |
| 最终稳定diff审查/`git diff --check` | 通过；全部文件归属Session/Settings/Host/新增测试/批准发行锁 |

新增覆盖：正常connect帧及诊断不泄密；重复/缺失/额外字段；非D格式GUID；body错配；0/负/超长frame预分配拒绝；真实本机pipe首实例拒占用、status/shutdown；恶意超大/半帧超时后listener恢复；记住登录默认关闭、DPAPI密文、只用户ACL、忘记/腐败blob/宽权限目录拒绝；换身份清旧epoch、坏密码无自动重试；取消正在登录、竞争busy；到期无用户查询也清除会话；失败的持久凭据只自动尝试一次后删除。

修正后重验的问题：新建DirectoryInfo缓存需Refresh后检查reparse属性；InvalidDataException不是IOException，control listener须明确捕获；两个pipe共用真实取消/回收生命周期以消除重复；Settings RuntimeFrameworkVersion必须定点metadata；restore需单数RuntimeIdentifier以使用发行锁。没有降低分析规则、跳过失败测试或放宽TLS/许可策略。

实机未执行：设置窗口实际主题/键盘/缩放、生产GUI进程及真实Explorer通知/安装/卸载；root统一检查。不以组件测试代替>600秒长期或G4运行证据。
