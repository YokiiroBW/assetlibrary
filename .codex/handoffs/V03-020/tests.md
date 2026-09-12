# V03-020 验证记录

2026-09-13，Windows x64，SDK10.0.111。命令均从本worktree运行；真实Core服务由root拥有，profile从环境读入且没有打印凭据。

| 项目 | 结果 |
|---|---|
| Windows solution restore --locked-mode | 通过（首次WinUI依赖下载4.9分钟） |
| Windows solution format --verify-no-changes --no-restore | 通过；框架工作区加载提示，不是编译warning |
| Windows solution Release build --no-restore | 通过，0 warning/0 error |
| Windows Tests Release --no-build --no-restore --filter TestCategory!=NativeLive | 最终101通过，0失败/跳过 |
| ThumbnailLiveTests 独立filter+私密native profile | 最终1通过，0失败/跳过；两张真实派生图均Ready |
| verify_repository.py | 通过；架构/源码/SDK/迁移等，Alpha保持blocked |
| package list --include-transitive --vulnerable + validate_dotnet_dependencies.py | 通过：6项目/31锁定包，无新增依赖 |
| Host production locked restore + self-contained publish | 通过，使用现有preview.2 release锁及.NET10.0.11 |
| 生产WinExe辅助模式，匿名pipe+实际128MiB Job+WIC PBGRA smoke | PRODUCTION_WIC_JOB_PBGRA_OK |
| 最终diff/权限边界/重复逻辑检查 | 通过；仅任务归属文件及root批准的SessionExpiryTests修正 |

具体入口：

```powershell
dotnet restore apps/windows-client/AssetLibrary.Windows.slnx --locked-mode --disable-parallel
dotnet format apps/windows-client/AssetLibrary.Windows.slnx --verify-no-changes --no-restore
dotnet build apps/windows-client/AssetLibrary.Windows.slnx --configuration Release --no-restore
dotnet test tests/windows-client/AssetLibrary.Windows.Tests.csproj --configuration Release --no-build --no-restore --filter "TestCategory!=NativeLive"
dotnet test tests/windows-client/AssetLibrary.Windows.Tests.csproj --configuration Release --no-build --no-restore --filter "FullyQualifiedName~ThumbnailLiveTests"
python -I -B scripts/verify_repository.py
```

Live命令额外要求ASSETLIBRARY_NATIVE_TEST_PROFILE指向root明确启动的隔离样例；此变量不是凭据参数。没有样例不得将Inconclusive当通过。

新增24个非Live用例包含：独立13向量逐条接受/拒绝；实际WIC精确alpha像素；无Job拒绝；APNG/超大IHDR/CRC/尾字节拒绝；401/403/404/302错误正文不读取；MIME/2MiB限制；429仅两次重试；一个图片不占导航保留容量；旧epoch不发HTTP、404不退出账号；late decode失效；实际pipe partial500ms与断开取消；实际Job终止阻塞stdin子进程；同步HTTP/decoder不能阻塞caller临界区。另有1个真实Core→WIC→pipe测试。

没有执行GUI/已安装app/真实NAS/生产thumbnail端点，也没有把模拟pixel输入当作NAS预览证据。G4用户豁免未测。
