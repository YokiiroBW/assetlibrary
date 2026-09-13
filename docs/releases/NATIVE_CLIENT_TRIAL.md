# 原生客户端首版进度与 Android 试用

2026-09-13后续：Windows preview.8 已交付原生Explorer的预览缩放/平移、适应/100%和键盘替代；真实Core/Explorer及清理通过，见[最新Windows交付](WINDOWS_EXPLORER_PREVIEW.md)。下方保留既有平台与历史证据，完整V0.3边界不变。

2026-09-13更新：Windows原生Explorer图库与大图浏览preview.7已交付，支持保比例瀑布流、列表/密度记忆、可见范围缩略图、空格/双击1600大图和键盘切图、选择和自有计数摘要；真实Core/Explorer图片验收及清理已通过。同日后续已完成[NAS同机图片服务上线](NAS_IMAGE_PREVIEW.md)；Android真机与完整V0.3仍独立。最新包见[Windows图库交付说明](WINDOWS_EXPLORER_PREVIEW.md)。

2026-09-12更新：Windows11 x64 **原生Explorer只读浏览preview.2安装包已交付**，含连接设置/后台Host与会话失效清理；包、安装和实际验收边界见[Windows交付说明](WINDOWS_EXPLORER_PREVIEW.md)。下文2026-09-08的Windows状态仅保留为历史，不代表最新状态；Android状态不变。

2026-09-08，V03-001..004。Android 手机/平板只读首版已交付；Windows 按用户要求以原生 Explorer 为入口，没有可用的 Windows/Explorer 安装包。最新排查与验证状态见下方 Windows 小节；没有改为要求用户另开独立 Windows 应用。

2026-09-09补充：V03-005集成分支提供新的[Android图片兼容试用候选](../../.runtime/releases/native-clients/AssetLibrary-Android-0.3.0-preview.1-404-fallback.apk)，30,425,593字节，SHA256 `6d959b5ac000b6a7a932c9d50298b51a25c9992d92ed748c9371e7b710444ddb`，仍为APK v2调试试用签名。它包含已在真实Core验证过的图片交互代码，以及旧服务缺少图片接口时的准确降级提示；本次修订另通过11项状态和1项相关原生UI检查。现有NAS没有启用图片引擎，连接该NAS时仍作为基础文件浏览使用，不据此宣称NAS图片预览已上线。新旧候选与验证来源分别记录在[V03-009交接](../../.codex/handoffs/V03-009/summary.md)；下方保留原只读首版说明与旧包，不覆盖原证据。

## Android 安装与连接

本地试用包：[AssetLibrary-Android-0.3.0-readonly.1.apk](../../.runtime/releases/native-clients/AssetLibrary-Android-0.3.0-readonly.1.apk)。APK 是本地构建产物，不提交 Git。大小 30,064,814 字节，SHA256：

```text
AFD7CBD3F387AEBC1F2D00EC34C3F824909673615546404B81AFD2A24A79B8CB
```

使用 Android 调试试用签名，APK v2 签名校验通过。Android 11+ 为最低接口目标；本次实际运行证据来自 API 36 模拟器，包含手机、平板深色和 200% 字体。尚无 HyperOS/Android 11 真机证据。

连接现有 NAS 时填写 `https://192.168.31.210:5443`，使用原有 Web 账号。
在“NAS 自签证书设置”中填写现有部署的叶证书 SHA256（可输入带冒号格式）：

```text
96:B0:BB:7A:59:3D:59:48:27:18:18:4E:C8:EC:5D:B9:59:30:B0:32:54:2B:E5:41:DB:9A:28:87:F9:BB:83:C1
```

该指纹与原部署公开证书一致，2026-09-08 本轮使用该证书信任链完成 `/readyz` 只读 HTTPS 检查，返回 200；没有读取账号口令或扫描原资产。证书到期时间与初始账号说明见 [NAS 使用说明](NAS_READ_ONLY_WEB.md)。换证后须从可信部署记录重新核对指纹，不自动接受换证。

支持登录/退出、分类切库、真实目录与导航历史、列表/网格、有界分页、服务器排序/筛选、全部库/库内/目录子树搜索、文件信息/定位/复制相对路径和扫描状态。手机采用顶部切库、底部导航、详情抽屉；平板宽屏为三栏。关闭进程后重新登录。

当前显示文件信息与类型图标，没有内容缩略图/预览、下载原文件、同步、上传、改名、移动、删除、主备切换或设备配对。管理资源库继续使用 Web。首次扫描快照不是实时同步。

## Windows 原生 Explorer

方向已由 [ADR-0018](../adr/ADR-0018_Windows原生Explorer入口与最小视图.md) 修订为原生 Explorer 资产库入口，保留系统顶部与导航，右侧使用原生 DefView；网络/会话与查询留在进程外 C# AssetHost。

只读协议组件、独立 COM/PIDL/DefView 和 owner 保护的 HKCU 注册/卸载验证已经完成。2026-09-11 已定位实机排查中的注册表视图隔离：由 Codex 启动的登记和检查进程看到同一份包内视图，普通 Explorer 启动的同用户检查进程却看不到这些登记。相同微软样例 DLL、字段和清理工具改由正常 Windows 用户进程执行后，真实 Explorer 已发现入口并进入 BA16 类、42 字节 PIDL、10 项内容；注销后入口消失、此电脑恢复为 3 项，登记与自有窗口清理完成。完整对照见[注册表视图边界证据](../../.codex/handoffs/V03-005/registry-view-boundary/summary.md)。

这一结果解除的是微软样例控制中的执行环境阻断。进程外 AssetHost、真实服务端数据与最终安装流程仍须完成验证和集成，不能据此宣布 Windows 客户端交付。登记/验收脚本已增加执行来源检查并合入集成分支，包内局部自检不再作为系统 Explorer 注册成功的依据；随后自有test-only最小扩展也已在真实Explorer中通过发现、正常双击进入4FF类/22字节PIDL/1个“示例资源库”，以及注销后9字段缺失、Desktop列表恢复和入口消失的闭环。它仍是验证扩展，不是已连接真实服务端的完整客户端；[完整实机归档](../../.codex/handoffs/V03-006/proof-native-entry.md)已通过统一复核。

2026-09-12补充：已接入真实Core，原生Explorer冷加载库根、首屏100条+下一页、第二页38条、相册/夏日/文件，以及Host停止提示、重启后旧位置失效和重新打开根恢复，均完成受控实机验收。每次导航保持在原生窗口；首次加载无F5或独立probe预热。网络/会话仍在外部AssetHost，Shell以实际呈现条目状态做有界加载刷新。最终[验收记录](../../.codex/handoffs/V03-005/explorer-host-integration/README.md)保留此前失败、修复、实际范围和原始证据；所有自有测试资源已回收，合成资产hash/mtime不变。

这仍是test-only受控接入，没有正式登录设置/安装包、文件打开预览或主动权限失效推送。Windows视图设置保留COM对象的严格卸载诊断仍未通过；M0-002-G2..G4、完整 V0.3/Alpha 和生产文件写入门禁保持原状态。

## 证据与复现

- [Android 交接与截图](../../.codex/handoffs/V03-003/summary.md)、[真实构建命令](../../apps/android/README.md)。
- [Windows 当前任务与证据](../../.codex/handoffs/V03-006/summary.md)、[最新集成状态](../../.codex/handoffs/V03-005/current-windows-work.json)。
- [真实 Core/PostgreSQL 联调与清理](../../.codex/handoffs/V03-004/summary.md)。
- [本轮集成交接](../../.codex/handoffs/V03-001/summary.md)。

两端均复用已批准 AssetLink 及核心查询；没有新增服务器、数据库或文件操作逻辑。101 个 APK runtime 依赖的在线 OSV/POM 许可审计和 475 个组件的校验元数据通过；结果仅表示本次查询未发现已知 advisory，不代表未来安全状态。真实联调使用隔离合成文件，服务、数据库、角色、端口、模拟器与私密测试连接已回收。
