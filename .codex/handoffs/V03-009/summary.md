# V03-009 — Android 缩略图与基础图片预览

状态 **ready_for_review**：原1838b96实现及真实Core联调已完成；当前f143292仅修正图片404提示，相关复测与新试用候选已完成，交由主协调统一验收。分支 `codex/v03-009-android-image-preview`，worktree `C:/Users/Administrator/.codex/worktrees/85ca/AssetLibrary`。已合入唯一契约提交 `38aedca57c0214349dc058d4c62deff805e953b3`；实现提交见 result.json。

## 已实现与交付

直接在既有 Kotlin/Compose Android 11+ 应用中接入同源 GET 派生 PNG。手机/平板同 APK：网格/列表按实际可见项加载缩略图；点击文件打开全屏图片预览，保留独立文件信息入口；支持双指/双击缩放、按钮放大/缩小/复位、键盘方向键与无障碍平移、关闭与系统返回。图片失败仍可查看 L0 文件信息，明确区分不支持、损坏、超限、离线、源变化、繁忙和超时。

当前试用候选（图片404降级修订）：`.runtime/releases/android/AssetLibrary-Android-0.3.0-preview.1-404-fallback.apk`，30,425,593 bytes，SHA256 `6d959b5ac000b6a7a932c9d50298b51a25c9992d92ed748c9371e7b710444ddb`。应用内 versionCode=2，versionName=0.3.0-preview.1；只更新 Android 自身版本，没有修改根级版本。沿用前序 Android 调试试用签名，APK v2 验证通过；不是商店/正式生产签名。本次Release unsigned为23,486,407 bytes。原1838b96候选文件及SHA256 `8461631cdd24fa077abe35794a92ab599cdff1036cfff7aa4cd743595fea158b`单独保留。

## 文件安全、权限、内存与兼容

- 复用原有 AssetLink 生成 SDK sourceSet、Cookie/CSRF、精确同源与 TLS 验证、服务器授权查询及共享主题；未手改共享契约/SDK，也未新增网络、UI、图片库或数据库访问。
- 图片使用固定 UUID 路由与冻结 variant，不接收任意 URL/路径，不读取原图。JSON 仍保持 5 秒，图片排队/传输/重试/解码受 20 秒总体预算约束；仅 429 最多重试两次并遵守 Retry-After，401/403/404 优先于正文处理。
- 验证 PNG 签名、长度、CRC、尺寸/像素、8 位、非动画和无源私密元数据；thumbnail 512px/2MiB、preview 1600px/12MiB。平台解码先检查尺寸，缩略图采样至最长256px。
- 最多32个实际可见项、2个并发，解码缓存最多24MiB，缓存仅本工作区会话内存。重新可见仍发真实网络请求，收到经服务端重新鉴权/源核验的相同派生字节后才复用解码缓存。导航/关闭/刷新/后台/账号/权限变化取消或释放相关内容；取消请求的迟到错误不能清除替代请求。
- 后台立即遮蔽工作区、关闭预览和清缓存，前台先复核会话与库权限。Android33+禁止最近任务快照；Android30..32保留安全窗口保护。释放缓存采用移除拥有引用，避免 RenderThread 仍绘图时 Bitmap.recycle 引发崩溃。
- 50万资产下仍只保留当前100条页，图片请求/状态/缓存均有常数上限；未把结构性上限声称为50万资产压测通过。

## 验证与证据边界

Debug/Release、strict lock/verification、Lint、37 JVM用例通过；5个不同仪器用例在手机默认、平板1280×800dp深色、手机200%字体分别通过。6张截图已逐张视觉检查并保存在 `screenshots/`，显示的是显式合成几何图片与真实 Compose 控件，**不是服务端图片验收证据**。仓库最终校验含35个迁移/架构回归通过，Alpha仍合法blocked，v0.1-start允许。

475组件完整性与101实际runtime坐标许可/OSV/APK体积审计通过；复用V03-001当日精确inventory查询回执，现有脚本重新验证时效、完整查询集合和哈希，未跳过网络安全或依赖检查。公开报告为 `android-dependency-audit.json`。

真实 Linux Core/PostgreSQL/受限Worker 已由主协调提供，手机 `RealCoreUiTest` 与 `RealCoreImageUiTest` 2/2通过，平板深色图片用例1/1通过，全部零失败/跳过。实际GET验证JPEG/PNG/WebP、EXIF6/透明、中文异名同内容、4类无效源、不可见账号404，以及原生浏览/预览/缩放/关闭/Activity重建。手机当时实际font_scale=2.0，平板1280×800dp/字体1.0；4张 `screenshots/real-core-*` 是真实服务端派生图片截图，已逐张检查。证据摘要与原始JUnit哈希见 `real-core-integration.json`。总计79个不同已通过用例（37 JVM+5原生样例仪器+2真实Core仪器+35仓库回归），平板重复不增计数。

## 运行态与后续

JDK21.0.12+8及SDK工具只读复用旧任务，Gradle依赖缓存/调试试用签名复制到本任务.runtime；自建AVD `v03-009-api36`，独占 `emulator-5584`。真实联调仅将协调临时连接送入本设备。平板测试通过后模拟器意外退出，原因未确定；重启自有AVD后取回截图，删除并验证设备私密连接不存在、reverse为空，再显式关闭。2026-09-08T15:57:29Z确认adb设备为空、5584/5585监听为0。未部署NAS、读取真实账号或扫描个人资产；共享服务器/SSH/148文件由主协调最后清理。

初版实现与真实联调证据已由root合入。2026-09-09追加f143292最小修复，仅将图片404提示改为“图片预览不可用，可查看文件信息”，保留全部认证/JSON404语义、版本号及依赖。新状态回归与相关UI返回L0点击已验证（11项状态+1项UI）；Debug/Release、Lint、strict、签名/候选审计通过。当前累计80个不同用例，但本次只执行相关12项，旧1838b96/874真实图片证据保留，不能宣称新候选又做了Linux或NAS实测。详情见image-404-fallback.md及image-404-candidate.json；旧NAS未安全启用图片接口前，仅承诺L0浏览兼容，不宣称该NAS图片已上线。建议root合入本修复/候选交接。HyperOS/Android11真机、50万资产压力、完整跨格式预览/同步、正式签名与全版本发布门禁继续保留。
