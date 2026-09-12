# Windows 用户级安装器

V03-017 的 OS 部署适配。独立 C#/.NET 10 WinExe、自包含单文件发布；只使用系统 TaskDialog，无第二套 UI 框架。共享生产 GUID、版本、管道与退出入口由 `contracts/windows-shell/user-session-v1.md` 和 ADR-0020 冻结。

包根为 `AssetLibrary.Setup.exe`、`manifest.json`、`payload/`。清单包含 formatVersion、owner、version、rid 和每文件 path/size/sha256，路径相对 payload。同目录 payload 含安装器自身及 Host、Settings、Explorer DLL、自包含运行时和 notices。哈希只验证完整性，不代替发布者签名。

双击默认安装；应用设置中的卸载项使用已安装 Setup 的 `uninstall`。发生待清理后再次双击解压包 Setup 会继续卸载清理。CLI：

```text
AssetLibrary.Setup.exe install --package <extracted-directory> [--quiet]
AssetLibrary.Setup.exe uninstall [--quiet]
AssetLibrary.Setup.exe status [--quiet]
```

`--sandbox <new-directory-inside-system-temp>` 强制文件注册后端、无 UI、无真实 HKCU、无 Host/Settings 启动和 Shell 通知。与实际安装使用同一复制、哈希、冲突、事务和清理逻辑；合成 payload 仅用于此测试模式。

安装依次校验全部源文件/清单外文件、确认本产品归属、获得单安装锁、校验空间、写独立 staging 并重读全部 SHA256、重命名为版本目录、停止旧 Host、持久记录注册回滚日志、切当前用户注册并回读。旧版本保留；相同版本不同清单拒覆盖。进程中断后的日志在下一次写操作恢复；遇外来注册修改保留日志并失败。

只注册生产 CLSID 与 Desktop NameSpace、Settings App Paths、当前用户 Run 的 AssetLibrary 单项、Uninstall 的 AssetLibrary 项和产品安装元数据。所有根都有产品 owner；未知项或 foreign owner 拒绝修改。其他 Run 值保持不变。不得把 `status` 当前进程注册视图解读为系统 Explorer 可见性证据。

卸载先请求经哈希核验的旧 Host `--shutdown-user-session`（7 秒外部界限，Host 自身契约 5 秒），失败保留注册。然后取消本产品注册并仅删除清单对应且哈希一致的应用文件。DLL/Setup 被占用、文件变化或不属于清单的内容留在原处，并写 `pending-cleanup.json`；退出码 3010 表示仍有待清理内容，不会终止 Explorer。用户配置位于版本目录之外，安装器不接触它。

退出码 0 成功，1 失败，1223 用户取消，3010 已注销但仍有待清理。`--quiet` 仍输出 JSON、错误代码和 pendingCleanup。没有自动下载、证书安装、HKLM 写入、UAC/信任更改、资产扫描或资产操作。

构建/测试命令见 [安装测试](../../../tests/windows-setup/README.md) 与 [打包入口](../../../infra/windows-client/README.md)。
