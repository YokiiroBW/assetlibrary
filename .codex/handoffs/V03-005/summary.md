# V03-005 — 并行浏览与预览协调

状态 **partial / Windows 受控功能通过，原生入口 G1 已关闭**。V03-006 Shell、V03-010 AssetHost与V03-011实际视图加载观察已集成；真实Explorer冷库根、分页、两级目录、Host不可用/重启与旧位置拒绝/根恢复通过，入口/自有窗口/Host/Core清理与合成原件完整性已核验。详见[本次接入验收](explorer-host-integration/README.md)和[原门禁裁决](explorer-gate-review/README.md)。V03-012 故障工具已完成并集成，下一步 root 实机验收补 G2 crash/timeout/invalid frames；G3 计时/生命周期、G4 循环/八小时独立保留。Web/Android既有证据保留，NAS图片发行仍受原目标内核阻断；正式登录安装、预览及完整客户端未完成。

## 已集成与已验收

- 38aedca冻结唯一图片wire、ADR0019、SkiaSharp4.151.2与生成SDK指纹。Core负责授权、有限只读broker、强hash/版本、缓存和HTTP；图片解析在独立NativeAOT进程。后续时间精度、严格sBIT和迟到清理名额修复已集成，原文件只读、缓存不绕授权。
- Linux默认Worker40d2d69在开发机6.14、非root且无外层seccomp下完成15项图片/隔离/资源/父退出场景，无GC环境覆盖；包、锁、MIT/native notices及hash可追溯。不能替代NAS5.10.55或通用Provider门禁。
- Android1838b96及真实证据1b149bc已集成：同一Linux服务下手机2/2、平板图片1/1（两个不同用例），4截图逐张复核，两份JUnit与APK强hash重算一致。候选在.runtime/releases/native-clients，仍为调试试用签名，SHA256 8461631cdd24fa077abe35794a92ab599cdff1036cfff7aa4cd743595fea158b。设备私密文件、reverse、模拟器及端口清理确认；真机/正式签名/完整版本门禁独立。
- Web真实联调发现并修复已挂载虚拟列表的键盘焦点问题，77271e0已合入，新有效回归与相关QuickLook/多选4/4通过；不同浏览器用例现66。真实16项图片/错误检查与独立interaction continuation完成：JPEG/PNG/WebP、方向、alpha、中文同内容改名、损坏/超限、Space/Escape/回焦/历史、404/401、对象URL释放和真实CSP。两份原receipt和索引SHA已重算一致，保留原failed顶层记录，不声称一次全跑。见V03-008/real-core/acceptance-index.json。
- Windows a21a145生命周期源、a497权限测试与25a7bed共享StartAsync已组装。root复现普通可写目录一次设置Owner+DACL失败后，53bc455先写严格私有DACL再核Owner，原失败目录独立7/7通过、0skip、零告警。历史5失败保留；未修改workspace ACL或放松保护。

- Android f143292的图片404兼容补丁与fb562bc候选证据已合入：只将图片不可用与条目删除区分，11状态测试及1相关API36界面用例通过，列表/会话与文件信息保留。新APK为30,425,593B，SHA256 `6d959b5ac000b6a7a932c9d50298b51a25c9992d92ed748c9371e7b710444ddb`；旧1838b96真实图片验收不冒充新候选全量重跑。

## 真实共同服务与清理

临时开发环境使用干净Core874fb6a、Worker40d2d69、PG16.15与148个固定合成文件。实际Linux图片接口2/2通过；Android和Web共用此生命周期。Web焦点修复后，仅替换经源码/强hash核对的静态产物并保留旧副本，Core/数据库/资产未变。

两个消费者结束后，root核对所属容器ID并显式stop，实际NATIVE_CLIENT_CLEANUP verified：148源hash/mtime不变、6个LOGIN移除、Host/PG退出、HTTPS关闭、runtime删除、容器不存在。Windows/远端connection导出副本已删除；Web按PID/创建时间/程序路径关闭所属SSH，root独立确认44147监听为0。公开原始结果real-core-cleanup.json，SHA751cc658fef1eb608d247f4842be68b359cdf9c10f344b00693ae0b8e2aaa04b；完整原始记录在.runtime/linux-real-core-evidence。没有改NAS生产或用户资产。

## 当前接续与分工

1. Windows最高优先级。自有入口、包外注册清理与真实Core只读接入已完成受控功能验收，G1已关闭。V03-012故障工具已合入88e6c21；下一步为G2真实故障恢复，不能重复回到旧入口排查或把工具正控当实机通过。当前Session 2已确认WTSDisconnected，截图与输入不可用；本轮未注册/启动管道Host，Core已清理，但新SDK测试窗口关闭未确认。恢复条件和已准备场景见[验收计划](explorer-gate-review/g2-acceptance-plan.md)。
2. NAS包a12b0d1已实际构建、逐文件验hash后暂存/载入NAS，未替换线上服务。相同实际包在Linux6.14的20项corpus/资源检查及父退出验证通过，但NAS5.10.55+缺少可用seccomp，Ready前安全失败、未发图；原件/容器清理通过。简单AppArmor替换不能保留现有隔离保证。nas-platform-decision.md建议仅在现有dev-230部署认证TLS图片计算入口，正在等待用户/ADR确认；回答前不扩展该服务或更改NAS策略。
3. 收齐Windows新入口与平台证据、实际NAS包/目标测试后再统一交付；继续更新result/tests/注册表。当前已通过的客户端源码套件不重复运行，仅对后续修复和缺证据补测。完整Alpha/V0.3、生产资产写入及既有Provider/平台门禁保持独立。

## 验证口径

最新带Host/Python/原生Worker的整solution格式和Release零告警，360通过/0失败/25明确NotExecuted。随后删除profile恢复1/1、负/非规范身份14/14通过。NAS平台Ready失败1项单列，后续19项未执行；开发Linux实际包20/20与父退出1/1不能抵消NAS失败。Windows c6649c5稳定native/身份修复已集成，原窗口12/12与LAN6对照明确范围；root新增活SID身份回归1/1及affected构建/仓库验证通过。详细原始结果见tests.md及.runtime/v03-005-final-tests。

线程/路径见windows.json与注册表，模块按任务包单写；共享wire、中央依赖/CI、版本、迁移和最终集成由root维护。
