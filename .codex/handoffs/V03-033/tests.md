# V03-033 验证

|范围|最终结果|
|---|---|
|MSVC严格Shell/图库/生产包|通过，/W4 /WX /permissive- /analyze /utf-8 /MT|
|Shell|13/13：page View3.98秒，其余12项69.77秒|
|图库|受影响12/12，1.87秒；不变数值几何专项未重跑|
|Setup|locked restore/format/build及23/23|
|发行锁/许可证与包|16+6；6项目17第三方包许可证通过|
|最终真实Setup CLI|8/8，合成payload及临时注册适配，无真实HKCU|
|图片夹具|Python13/13、C#9/9、严格构建/format|
|实机|540hash、.9模块、100/20页与双向跨图、缩放复位/Esc定位/宽窄/断开清图|
|清理|258hash/mtime、6角色、Core/PG/runtime/HTTPS，Host/连接和2自有窗清理|

实际命令（工具均用已安装精确版本）：

```text
cmake -S tests/windows-shell -B .runtime/shell-tests -G "Visual Studio 17 2022" -A x64
cmake --build .runtime/shell-tests --config Release --parallel 2
ctest --test-dir .runtime/shell-tests -C Release -R '^explorer_gallery_view$' --output-on-failure
ctest --test-dir .runtime/shell-tests -C Release -E '^explorer_gallery_view$' --output-on-failure
cmake -S tests/windows-gallery -B .runtime/gallery-tests -G "Visual Studio 17 2022" -A x64
cmake --build .runtime/gallery-tests --config Release --parallel 2
ctest --test-dir .runtime/gallery-tests -C Release -E '^gallery_preview_viewport$' --output-on-failure
python -I -B scripts/verify_repository.py
python -I -B scripts/validate_windows_release_locks.py --packages-dir C:/YOKI/Codex/AssetLibrary-worktrees/V03-015/.runtime/nuget
python -I -B -m unittest discover -s tests/repository -p test_windows_release_locks.py -v
python -I -B -m unittest discover -s tests/windows-setup -p test_package.py -v
python -I -B tests/windows-setup/test_cli.py --setup .runtime/releases/windows/AssetLibrary-0.3.0-preview.9-win-x64/AssetLibrary.Setup.exe --report .runtime/preview-live/setup-cli.json
```

Setup restore/format/build/test与原README一致；正式build_package.py从干净1003157完整publish和MSVC生产目标生成，不拼装旧Shell。夹具命令/工具/日志见V03-036交接；实机来源为该任务afdc0df的真实Core与完整已验worker，无原NAS变更。

View定向回归覆盖同HWND/零BrowseObject、previous重查新token、失败F5同目标、64位置/循环、双向跨页、目录或空页仅读一页、清旧图/选择、hide/refresh/destroy取消及晚到拒绝、权限/epoch清历史、直接canvas和IShellView路由Escape取消自动预览、SetPage/SelectItem同步重入的新预览胜出、缩放零额外I/O。不是仅镜像实现断言。

UIA树有一帧延迟，图片与后续稳定树配合判定。断开后第一帧尚在通知前，随后回根清空才是通过；两个原始帧都保留。未把G4、完整V0.3、全库无限滚动或超宽图片全量缓存记成通过。归档日志在windows-preview9-delivery；不变/去重测试不累加重复执行次数。

最终仓库测试95/95（34.43秒）与verify_repository通过；Alpha有效性审计通过但发布仍blocked，explorer-v0.5允许沿用原G4豁免。日志已归档；测试资源均清理，未重新运行G4。
