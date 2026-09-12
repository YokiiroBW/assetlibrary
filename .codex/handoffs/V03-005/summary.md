# V03-005 — 并行浏览与预览协调

2026-09-13最新：**Windows原生Explorer图库浏览preview.6已交付，本轮浏览体验工作完成**。V03-019/020/021已集成，保比例瀑布流/列表/密度、101可见候选分批、共享16MiB像素预算、原生UIA退出回收及自有计数摘要通过；实际Core/Explorer图片、中文目录、分页、多选、宽窄布局和退出清空有实机证据。148合成原件不变、6角色及临时服务清理；测试连接移除，正式安装保留。见[图库交付记录](windows-gallery-delivery/README.md)。NAS预览引擎、完整V0.3/Android真机/签名独立；G4仍用户豁免未测。下方preview.2及更早记录为历史。

状态 **partial / Windows可安装只读浏览版preview.2已交付，完整V0.3未完成**。原生侧栏、WinUI连接设置、后台Host、分页与中文目录、根/子目录会话清理已在实机验收；测试窗口和服务已结束，138原件未变、6角色回收。正式安装保留，测试配置移除。包、人工验收缺口、标题显示与旧版本占用限制见[交付验收](windows-product-delivery/README.md)和[使用说明](../../../docs/releases/WINDOWS_EXPLORER_PREVIEW.md)。

G1/G2/G3沿用实测证据，G4依用户要求豁免未执行；不再安排20轮/8小时。[G3量测验收](explorer-g3-live/README.md)保留原数值与边界。内容预览、传输、签名与Android指定真机仍独立待办。

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

1. 用户要求的Windows可安装浏览版已交付preview.2；下一步为人工认证/系统卸载入口检查、友好标题与导航体验，再按独立决策推进内容预览。当前无活跃GUI/测试服务，安装器注册保留供使用。G3实测/G4用户豁免边界不变，不重跑旧注册、克隆信号或G3调查。
2. NAS包a12b0d1已实际构建、逐文件验hash后暂存/载入NAS，未替换线上服务。相同实际包在Linux6.14的20项corpus/资源检查及父退出验证通过，但NAS5.10.55+缺少可用seccomp，Ready前安全失败、未发图；原件/容器清理通过。简单AppArmor替换不能保留现有隔离保证。nas-platform-decision.md建议仅在现有dev-230部署认证TLS图片计算入口，正在等待用户/ADR确认；回答前不扩展该服务或更改NAS策略。
3. 收齐Windows新入口与平台证据、实际NAS包/目标测试后再统一交付；继续更新result/tests/注册表。当前已通过的客户端源码套件不重复运行，仅对后续修复和缺证据补测。完整Alpha/V0.3、生产资产写入及既有Provider/平台门禁保持独立。

## 验证口径

最新带Host/Python/原生Worker的整solution格式和Release零告警，360通过/0失败/25明确NotExecuted。随后删除profile恢复1/1、负/非规范身份14/14通过。NAS平台Ready失败1项单列，后续19项未执行；开发Linux实际包20/20与父退出1/1不能抵消NAS失败。Windows c6649c5稳定native/身份修复已集成，原窗口12/12与LAN6对照明确范围；root新增活SID身份回归1/1及affected构建/仓库验证通过。详细原始结果见tests.md及.runtime/v03-005-final-tests。

线程/路径见windows.json与注册表，模块按任务包单写；共享wire、中央依赖/CI、版本、迁移和最终集成由root维护。
