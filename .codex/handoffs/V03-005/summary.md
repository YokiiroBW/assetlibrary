# V03-005 — 并行浏览与预览协调

状态 **partial / 统一集成与交付收尾**。继续使用四个既有独立窗口、分支和worktree。Web与Android已在同一真实Core/PostgreSQL/受限解码引擎上通过图片验收；Windows原生Explorer入口和剩余平台矩阵、NAS实际图片发行验证仍在进行，不宣告整批或完整版本完成。

## 已集成与已验收

- 38aedca冻结唯一图片wire、ADR0019、SkiaSharp4.151.2与生成SDK指纹。Core负责授权、有限只读broker、强hash/版本、缓存和HTTP；图片解析在独立NativeAOT进程。后续时间精度、严格sBIT和迟到清理名额修复已集成，原文件只读、缓存不绕授权。
- Linux默认Worker40d2d69在开发机6.14、非root且无外层seccomp下完成15项图片/隔离/资源/父退出场景，无GC环境覆盖；包、锁、MIT/native notices及hash可追溯。不能替代NAS5.10.55或通用Provider门禁。
- Android1838b96及真实证据1b149bc已集成：同一Linux服务下手机2/2、平板图片1/1（两个不同用例），4截图逐张复核，两份JUnit与APK强hash重算一致。候选在.runtime/releases/native-clients，仍为调试试用签名，SHA256 8461631cdd24fa077abe35794a92ab599cdff1036cfff7aa4cd743595fea158b。设备私密文件、reverse、模拟器及端口清理确认；真机/正式签名/完整版本门禁独立。
- Web真实联调发现并修复已挂载虚拟列表的键盘焦点问题，77271e0已合入，新有效回归与相关QuickLook/多选4/4通过；不同浏览器用例现66。真实16项图片/错误检查与独立interaction continuation完成：JPEG/PNG/WebP、方向、alpha、中文同内容改名、损坏/超限、Space/Escape/回焦/历史、404/401、对象URL释放和真实CSP。两份原receipt和索引SHA已重算一致，保留原failed顶层记录，不声称一次全跑。见V03-008/real-core/acceptance-index.json。
- Windows a21a145生命周期源、a497权限测试与25a7bed共享StartAsync已组装。root复现普通可写目录一次设置Owner+DACL失败后，53bc455先写严格私有DACL再核Owner，原失败目录独立7/7通过、0skip、零告警。历史5失败保留；未修改workspace ACL或放松保护。

## 真实共同服务与清理

临时开发环境使用干净Core874fb6a、Worker40d2d69、PG16.15与148个固定合成文件。实际Linux图片接口2/2通过；Android和Web共用此生命周期。Web焦点修复后，仅替换经源码/强hash核对的静态产物并保留旧副本，Core/数据库/资产未变。

两个消费者结束后，root核对所属容器ID并显式stop，实际NATIVE_CLIENT_CLEANUP verified：148源hash/mtime不变、6个LOGIN移除、Host/PG退出、HTTPS关闭、runtime删除、容器不存在。Windows/远端connection导出副本已删除；Web按PID/创建时间/程序路径关闭所属SSH，root独立确认44147监听为0。公开原始结果real-core-cleanup.json，SHA751cc658fef1eb608d247f4842be68b359cdf9c10f344b00693ae0b8e2aaa04b；完整原始记录在.runtime/linux-real-core-evidence。没有改NAS生产或用户资产。

## 当前接续与分工

1. Windows最新只读状态在2026-09-08T16:44:08Z已恢复WTSActive，真实“此电脑”窗口可见。root已授权立即恢复一次有界Explorer入口观测，不再沿用旧Disconnected阻断。Windows owner先结束已开始的profile幂等小组，再优先推进真实入口/IPC；不改HKLM/UAC/全局策略、不重启用户Explorer。尚无Windows安装包，独立出图/loader矩阵不等于G1..G4完成。
2. NAS旧包只带Host，缺图片引擎。f673a12限定授权V03-007接入NAS Docker/离线包及负例，d18b224已集成；Linux包验证11/11通过。root已从同提交0f4d0c6实际构建，首次AOT publish报NETSDK1112，原因是restore与publish的SelfContained属性不一致，正由所有者修正后重建。NAS5.10.55、Docker24.0.2和agent-210只读访问已核；实际目标平台测试、备份/回滚/受控启用归root，缺省图片仍关闭。其他通用/原生包缺口独立记录。
3. 收齐Windows新入口与平台证据、实际NAS包/目标测试后再统一交付；继续更新result/tests/注册表。当前已通过的客户端源码套件不重复运行，仅对后续修复和缺证据补测。完整Alpha/V0.3、生产资产写入及既有Provider/平台门禁保持独立。

## 验证口径

ad5d316整solution格式和Release零告警，常规dotnet test 340通过/0失败/34明确NotExecuted；其后补实际Host/Python的源broker1、扫描取消1和故障回收6，新增嵌套reaper2及授权3均通过，Windows修复后7/7通过。后端完整组装21/21与真实Windows Host/PG/HTTPS2/2通过。不同阶段/平台和重复执行不直接相加，历史失败及原始TRX在tests.md与.runtime/v03-005-final-tests。

线程/路径见windows.json与注册表，模块按任务包单写；共享wire、中央依赖/CI、版本、迁移和最终集成由root维护。
