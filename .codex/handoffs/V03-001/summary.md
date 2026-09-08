# V03-001 — 原生首版集成：Android 已交付，Windows Explorer 仍阻断

状态 **partial**。用户要求 Windows 与 Android 第一版按文档视觉/架构实现，随后明确 Windows 必须嵌入原生 Explorer，不能要求另开独立客户端。已按 ADR-0018 调整方向；没有以独立应用冒充完成。

## 已交付

Android 原生 Compose 手机/平板只读工作区、实际 APK、构建与原生运行证据已集成。登录/退出、八类库导航、真实目录、分页/服务端排序筛选、范围搜索、文件信息/定位/复制相对路径、扫描状态、浅深色和200%字体均有具体实现与验证。最终APK为 `.runtime/releases/native-clients/AssetLibrary-Android-0.3.0-readonly.1.apk`，30064814 bytes、SHA256 afd7cbd3f387aebc1f2d00ec34c3f824909673615546404b81afd2a24a79b8cb；两个本地交付副本均已重读强哈希。使用调试试用签名，非商店/正式生产签名。

Windows 已集成可复用的 .NET10 只读协议组件和 test-only C++ DefView 验证；真实 Core/PG 登录、查询、分页、详情、退出和拒权通过。真实 Explorer 入口仍在本项目类工厂前报无关联应用，根因未定位，**没有 Windows/Explorer 安装包**。G1..G4 与生产 Shell 未启用。

复用了生成 .NET/Kotlin SDK、既有 Cookie/CSRF 及核心权限/索引查询、Web语义主题与真实Core/PG测试fixture。没有服务端、数据库、wire schema 或生成SDK改动，没有跨模块写表或复制文件/权限业务规则。测试fixture只改测试程序集的有界生命周期与证书释放，旧真实Web E2E也已回归通过。

## 验证与真实边界

最终统一仓库校验已通过，详见 `tests.md`、子任务交接及 `delivery.json`。Android 23个原生测试、Windows 23个协议/真实Core用例、fixture 11项、审计边界5项和共用仓库35项共97个不同逻辑检查通过；同一用例的重复执行/视口与各分支重复仓库检查不重复求和。另有1个真实Explorer入口场景失败；G2/G3/G4尚无真实视图证据，不折算为跳过通过。

101个实际APK runtime坐标的在线OSV/POM许可与475组件校验元数据通过，公开证据在V03-003交接。初次大批HTTP被重置后由保持TLS验证的Node分10条查询，协调脚本对响应时效、完整查询与inventory SHA256重新校验；没有关闭TLS、删漏洞或把缺失响应当通过。

真实Core/PostgreSQL样例均正常停止：138合成文件hash/mtime保持不变，6角色、临时库、进程、监听、私密连接与Android reverse/模拟器已清理。NAS仅用原部署公开证书完成 `/readyz` HTTPS只读检查，200且指纹一致；没读取真实口令、资产列表或修改NAS。

Windows本任务HKCU注册已独立读回全清理。界面输入返回0x80070005，已异步请用户确认桌面登录/解锁；8个本任务测试窗口待可用输入恢复后重新核对并关闭，见 `desktop-cleanup-pending.json`。不能依赖过期句柄或关闭用户原有窗口，不能修改UAC/HKLM/安全设置或重启用户Explorer。

## 状态与后续

V03-003、V03-004 completed；V03-001、V03-002 partial。当前V0.1和完整V0.3不宣告完成。下一步从实际Explorer入口与可用桌面输入继续，不重新实现Android，也不退回独立Windows入口。HyperOS/Android11真机、50万资产原生压力、内容预览/同步/写入、设备配对/主备和正式签名仍缺独立证据。

分支 codex/v03-001-native-client-first-delivery；集成源码提交 ce454c61fc00a9c75b22f71964a6b6778a9e66e6。合并顺序为共享规划/主题与fixture → Android → Windows部分验证 → 集成证据；Windows部分合入不等于功能启用。所有交付改动在本独立worktree完成，用户主目录仅收到匹配哈希APK与最终fast-forward。
