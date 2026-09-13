# V03-025 — 图库偏好组件交接

状态ready_for_review。分支codex/v03-025-explorer-browse-preferences；工作区C:/YOKI/Codex/AssetLibrary-worktrees/AssetLibrary-worktrees/V03-025。组件提交c5ef10437747c30c7467fda445073faee9519d39，root已合；root负责View/Surface/CMake接线与整包验收。

## API与构建

apps/windows-shell/gallery/BrowsePreferences.h/.cpp，namespace gallery::preferences：Values { Mode mode=Mode::Gallery; UINT densityDip=DefaultDensityDip; }；Values Load() noexcept；HRESULT Save(const Values&) noexcept。LoadFromKey(HKEY)/SaveToKey(HKEY,const Values&)借用句柄，不打开或关闭它；供隔离测试，不从环境/配置接受生产路径覆盖。Encode/Decode只处理Record=std::array<BYTE,16>，失败归零字节或整体恢复默认。

root可将BrowsePreferences.cpp加入AssetLibraryGallery；ExplorerGalleryPreferencesTests执行GalleryPreferencesTests.cpp并链接Gallery。组件只需已有advapi32；测试GUID生成需ole32。C++17、/W4 /WX /permissive- /analyze /utf-8、Release /MT。产品CMake未由本任务修改。

## 实现与边界

只消费ADR0022：ABP1 magic0x31504241、version1、length16、mode0/1、density96..256/default176，显式小端编码，不序列化结构体padding。复用Surface Mode和密度常量，无重复领域逻辑。

固定HKCU64 Software\AssetLibrary\ExplorerPreferences/BrowseState单REG_BINARY16B。Load只打开/读取，缺失、权限失败、类型不符、过长/短、未知版本或非法字段整体默认，不创建/修复，不按返回长度扩容。Save校验后用最小KEY_SET_VALUE打开/创建产品键，一次RegSetValueEx写完整tuple；失败HRESULT交调用者提示，当前UI由root保持。无重试、轮询、RegFlushKey或关闭保存。

结果是值拷贝，没有共享可变缓存或跨窗口推送。不保存epoch/node/path、凭据、选择、大图或缩放；无Host偏好IPC、服务、网络、解码或资产读写。Win32同步注册表API没有单独取消参数，本实现按批准Theme模式固定小型工作，不声称硬超时或断电持久性。

## 安装与测试

静态确认Setup OwnedRoots只含WindowsClient等既有根；ReadTree递归/Write整树替换不涉及ExplorerPreferences兄弟键，所以升级/回滚/卸载保留。没有把REG_BINARY塞进只支持string/int的安装schema。未知邻近值保持。

严格临时CMake构建与explorer_gallery_preferences1/1通过0.01s。覆盖独立golden字节、两个mode的全部161种合法密度、损坏/类型/长度/版本/空指针、真实只读handle拒写且旧bytes保持、显式恢复坏值、邻近值不变及读快照隔离。EXE SHA256 675F0E415747ED134729A471105B6F671670237DE1F07F1F1212C835221D34C4。

只创建新GUID易失HKCU键Software\AssetLibrary.BrowsePreferences.Tests.{GUID}，拒绝已有键，结束精确删除并核不存在；未调用生产Load/Save。未改真实偏好/Explorer注册/Theme/NAS。最初测试误将255 DIP标为非法，改95/257负控后通过；实现没有放宽边界。

root确认Setup旧模型已有preview6证据且源码未改，不重跑。没有GUI/生产pipe/G4或重复旧套件。下一步root验证View回调、失败摘要、预览不保存与产品目标构建；建议组件c5ef104后合本交接。无新增依赖/语言/框架/shared contract或未落实TODO，不宣布整个V0.3完成。
