# 安装器验证

测试仅在系统临时目录创建合成应用组件。文件复制、SHA256、版本切换、持久注册事务、回滚和清理使用生产逻辑；注册存储替换为临时 JSON，从不写真实 HKCU、不启动 Host/Settings、不触碰 NAS。

```powershell
dotnet restore tests/windows-setup/AssetLibrary.Windows.Setup.Tests.csproj --locked-mode
dotnet format tests/windows-setup/AssetLibrary.Windows.Setup.Tests.csproj --verify-no-changes --no-restore
dotnet build tests/windows-setup/AssetLibrary.Windows.Setup.Tests.csproj --configuration Release --no-restore
dotnet test tests/windows-setup/AssetLibrary.Windows.Setup.Tests.csproj --configuration Release --no-build --no-restore --logger "trx;LogFileName=setup-tests.trx" --results-directory .runtime/setup-test-results
python -I -B -m unittest discover -s tests/windows-setup -p test_package.py -v
```

发行安装器的实际 CLI 冒烟（先按 infra/windows-client 的 RID 锁参数完成 Setup 自包含 publish）：

```powershell
python -I -B tests/windows-setup/test_cli.py --setup <self-contained-publish>/AssetLibrary.Setup.exe --report .runtime/setup-cli-results.json
```

覆盖：安装/相同包重装/新版切换/幂等卸载、损坏/漏项/多余组件、大小与 SHA、绝对/父目录/反斜杠/ADS/设备名/尾空格尾点、大小写重复、真实 NTFS junction、foreign owner、同版本不同文件、取消、空间不足、staging 中断、注册写失败回滚、进程崩溃日志恢复、Host 退出失败、真实 DLL 文件锁及重试、保留配置与源包。测试创建 junction 使用 Windows 内置 mklink /J，不要求开发者模式或修改系统策略。

CLI 测试真实运行自包含 EXE，但 payload 是明确合成的四文件 fixture；通过并不表示真实产品包可浏览资产。最终生产组件与系统 Explorer/HKCU 的用户闭环必须由主任务另行验证。50 万资产不影响安装复杂度：安装成本仅随包文件数/字节数线性增长，清单最多 4096 文件、2 GiB，目录遍历最多 8192 项、深度 16，资产目录不在安装器输入中。
