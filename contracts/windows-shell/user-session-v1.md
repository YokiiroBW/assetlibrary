# Windows 用户会话与安装协作 v1

Owner V03-005；2026-09-12为首个Windows可安装只读浏览包冻结。此契约只连接本机设置、后台与安装器，不改变AssetLink/core业务或快照wire v1。

产品：版本0.3.0-preview.1；CLSID `{BBC992DE-CE5D-48C8-A86C-7230C7D72B02}`，owner `AssetLibrary.Windows.Explorer`，显示“资产库”。同目录组件：`AssetLibrary.Explorer.dll`、`AssetLibrary.Host.exe`、`AssetLibrary.Settings.exe`、`AssetLibrary.Setup.exe`。安装根/版本/配置根按ADR-0020。

## 本机控制

Pipe `AssetLibrary.HostControl.v1.<TokenUserSID>.<WindowsSessionId>`；只允许同TokenUser SID/session，拒绝远程，first-instance拒占用。客户端也检查服务端身份。最多4连接，4字节LE无符号长度后UTF8 JSON，正文1..16384字节；500ms帧收发期限，connect含已有8秒HTTP的总操作12秒；串行会话变更，竞争返回busy，disconnect须取消正在连接的尝试。没有无限重试或命令/URI执行。

request：`version:1`、`request_id`规范GUID D、`operation`为status/connect/disconnect/shutdown；仅connect必须含connection，其字段为`origin`、`certificate_sha256`(null|string)、`account_name`、`password`、`remember_login`(bool)。拒绝额外/重复/缺失字段和不匹配operation的body。所有服务端Origin/TLS验证复用ServerProfile；命名管道不代替Core鉴权。

response：`version:1`、相同`request_id`、`ok`、`error_code`(null或invalid_request/busy/unavailable/access_denied/cancelled/storage_error)、`status`。status包含state(unconfigured/disconnected/connecting/connected/access_denied/unavailable)、origin/certificate_sha256/account_name/display_name/expires_at（未有值为null，expires_at为ISO8601）、remember_login(bool)。响应和日志不得含password/Cookie/CSRF或错误正文。status不在未认证的Shell快照pipe上传递账号或来源信息。

Host `--user-session` 为常驻模式，生产快照pipe `AssetLibrary.Explorer.v1.<SID>.<session>`，未配置/未登录明确Unavailable。`--shutdown-user-session`是控制客户端，最多5秒，无Host时幂等成功；不根据任意PID杀进程。Settings无参数打开配置窗口，仅首次设置/用户显式操作时显示。原Host `--profile ...` 保留Proof试验模式与旧端点。

记住登录默认关闭。未勾选只通过当前用户本机会话控制传递并内存使用；勾选后才用CurrentUser DPAPI持久化到严格DACL/owner的remembered blob。普通配置不含口令，退出清记住凭据和内存session/快照；拒权/超时/晚到请求不得复活旧身份数据。

## 安装包

包根Setup.exe、`payload/`、`manifest.json`；manifest `formatVersion=1`、`version=0.3.0-preview.1`、`rid=win-x64`、files数组(path/size/sha256)。所有路径相对payload，拒绝逃逸/绝对/重解析/大小或hash不符；包中组件同目录，重复依赖必须hash相同。CLI install --package <dir> [--quiet]、uninstall [--quiet]、status；sandbox模式只在临时根用文件注册后端且不启动用户程序/触发真实注册。Setup交互使用平台原生对话框，quiet仍返回准确退出码/错误/延期清理。

HKCU只注册上述生产CLSID及Desktop NameSpace；产品元数据和Run/Uninstall位置按ADR-0020。owner不匹配拒覆盖或删除，不改HKLM/UAC，不终止Explorer。先完整安装新版本再切注册，失败回滚。卸载已加载DLL的延期清理必须显式记录。

## 会话变化的视图更新

生产浏览必须在退出/换身份/失效后使已显示旧数据退出可用状态。具体公开Shell通知接线由V03-016/V03-015向root提交补充方案，未经冻结不得悄悄改变wire或使用私有Shell消息。最终实际安装验收必须覆盖此行为；不能用“服务端已拒权”代替旧画面清理。
