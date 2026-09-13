# V03-030 验证

| 范围 | 结果 | 证据 |
| --- | --- | --- |
| C++生产/Proof/Shell严格构建 | 通过，/W4 /WX /permissive- /analyze /utf-8 /MT | shell-build.log |
| Shell调用链 | 13/13；新View1.23s，其余12项69.81s | shell-view-tests.log、shell-remaining-tests.log |
| 数值几何 | 1/1，7定向/256矩阵/2000饱和操作 | V03-031 handoff |
| 原生控件/图像/辅助技术 | 12/12；真实隐藏HWND/GDI、捕获、旧代理/资源回收 | V03-032 handoff及本交付原日志 |
| Setup生产逻辑 | 23/23，locked restore/format/build通过 | setup-tests.trx、setup日志 |
| 发行锁及包 | 16+6通过；6项目17包许可证通过 | release-lock-tests.log、package-tests.log |
| 最终安装器CLI | 8/8；真实EXE+合成payload，无真实HKCU | preview8-setup-cli.json |
| 完整发行 | 540文件核hash匹配；新用户进程安装.8、真实新Explorer加载.8 DLL | installed-readback.json、explorer-complete-process.json |
| 实机 | 真实JPG/PNG/WebP/方向/透明；100/125/195、drag/Shift箭头、double-fit、0/1/+、923宽窗口、切图reset/坏图、Esc选择/滚动/退出 | 对应WGC与UIA证据 |
| 清理 | 两fixture各148hash/mtime及6roles；Core/PG/runtime/HTTPS全清、Host0/remembered无/测试connection清、3自有窗关闭、原Explorer未重启 | core-cleanup.json等 |

真实命令沿README，不自造层级：

```text
cmake -S tests/windows-shell -B .runtime/shell-tests -G "Visual Studio 17 2022" -A x64
cmake --build .runtime/shell-tests --config Release --parallel 2
ctest --test-dir .runtime/shell-tests -C Release -R '^explorer_gallery_view$' --output-on-failure
ctest --test-dir .runtime/shell-tests -C Release -E '^explorer_gallery_view$' --output-on-failure
dotnet restore tests/windows-setup/AssetLibrary.Windows.Setup.Tests.csproj --locked-mode
dotnet format tests/windows-setup/AssetLibrary.Windows.Setup.slnx --verify-no-changes --no-restore
dotnet build tests/windows-setup/AssetLibrary.Windows.Setup.Tests.csproj --configuration Release --no-restore
dotnet test tests/windows-setup/AssetLibrary.Windows.Setup.Tests.csproj --configuration Release --no-build --no-restore
python -I -B scripts/validate_windows_release_locks.py --packages-dir <existing-V03-015-cache>
python -I -B -m unittest discover -s tests/repository -p test_windows_release_locks.py -v
python -I -B -m unittest discover -s tests/windows-setup -p test_package.py -v
python -I -B infra/windows-client/build_package.py --dotnet <SDK10.0.111> --cmake <VS2022-CMake> --build-root .runtime/preview8-package-build --output .runtime/releases/windows/AssetLibrary-0.3.0-preview.8-win-x64 --notice <15 existing license files>
python -I -B tests/windows-setup/test_cli.py --setup .runtime/releases/windows/AssetLibrary-0.3.0-preview.8-win-x64/AssetLibrary.Setup.exe --report .runtime/preview8-setup-cli.json
```

包来源为干净67affa7，155526095字节，SHA256 3fc8d3bd763662dc89f6d1ebbc000496c2bbbf402ad3e6af178a308bd9571767。逐文件及模块hash读回，非仅launcher/返回码。新版安装exit3010只因旧preview.1占用，installed .8状态明确，不删除旧文件。

基线verify_repository通过；最终仓库/架构/Explorer门禁日志见交付。未更改Host/.NET业务，未重跑既有120离线测试；平台/功能缺项仍明确，G4不执行。初次发行锁检查误用不完整V03-005cache报缺Settings许可证，改用已有V03-015完整cache通过，无网络/许可绕过。初次实机Core启动误选仅EXE的native中间输出，缺libSkiaSharp；该测试实例完整清理后，用既有完整gallery-server worker重开新实例并通过。

UIA工具返回树可能比截图落后一帧；保留原证据，并由后续稳定树、实际画面与隐藏原生测试共同确认。第一次拖窗口边未调整尺寸的尝试不记成功；实际通过的是系统大小菜单产生923px宽窗口及后续fit。当前无实际跨显示器DPI/触控真机结果，不冒称通过。

最终verify_repository与Explorer门禁通过，Alpha有效性审计通过但发布仍blocked。repository初次95项中94项通过、1项因归档空CLI流误用JSON扩展名失败；改为保持空内容的.log后，仅重跑该项1/1通过，去重最终95项有通过证据，不重跑其余94。原始失败及补跑日志保留。
