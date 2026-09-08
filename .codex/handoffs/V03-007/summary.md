# V03-007 — 服务端图片、NAS打包与平台验证（partial）

当前阻断已经明确：目标NAS内核没有提供本实现需要的seccomp接口，图片保持关闭/503。现有Core、隔离Worker、NAS包与诊断交接已完成到可审查检查点；不继续增加服务、改变NAS策略或宣告目标平台通过。平台方向由主协调195afc82e9b3ab30de39f29fbe9c75eaba485596中的决策问题等待用户确认和相应ADR。

## 已交付实现

分支codex/v03-007-image-preview-server，工作区C:/Users/Administrator/.codex/worktrees/021c/AssetLibrary。Core/Worker接线0a746e2；sBIT严格PNG修正4275815；源清理名额a277178；迟到嵌套清理de83a3c；Windows StartAsync25a7bed；NAS打包d18b224及实际恢复/发布图一致性00b03b1；最终test-only诊断808eb3a。Windows专属生命周期与journal由V03-006单写，最新5bce4cd已合为bfc5fc1。

- 冻结GET使用库/entry稳定ID和variant，同源鉴权、前后授权/索引/实际源复核，返回重编码PNG。thumbnail512px/2MiB、preview1600px/12MiB，不放大、处理EXIF方向并保留alpha；格式/损坏/超限/权限/源变化都有明确失败。
- Source broker复用规范路径和只读worker边界，逐层no-follow、强hash与全精度当前身份/mtime核验。原文件不修改。Core不加载Skia，独立NativeAOT只接有界字节。
- 2并发名额保持到实际清理完成，未知清理失败保留有限名额；内容hash+renderer缓存64MiB/256项。PNG全块CRC和严格8bit/sRGB/sBIT集合，拒绝未知或原源元数据。
- NAS固定SDK/RID锁发布独立Worker，运行层只含6个明确产物；构建清单绑定实际镜像、提交、锁和每文件hash。Compose仅透传显式路径，默认关闭；没有改变非root、capdrop、NNP、只读根/资产、init或资源上限。
- Python3.8兼容的verify_nas_worker.py只测试显式已验证包、固定corpus与canary，顺序12图片变体/4拒绝/4探针，pipe/过程/全组都有界。ID记账、身份核验、清理存在性确认以及失败前就绪帧/退出/OOM/有限stderr摘要均保留；未知create结果不能冒充cleanup完成。

## 已验证与明确失败

本地真实Core/PostgreSQL/HTTPS→source broker→LPAC NativeAOT图片2/2，后续Windows完整清理源码组合的相关21/21及真实HTTPS2/2均无skip；原样例hash/mtime、Host/PG/账号/证书/临时状态由fixture确认清理。源边界、容量、授权、PNG与真实PG精度见tests.md；单项Windows叶symlink仍缺本机权限，开发Linux另有源边界证据。

root同提交a12b0d139765357b1e33437a4c7a34823dd556ad的NAS包已实际built_not_deployed；Core image sha256:2dd1f17c28be1969b6488c5afe14499c71f7f26aa5d36c179b09768056cf54f0，images.tar为783892992B，SHA256 5951b75d46b46c14124885bb71f0cdf7b1c4b3713d495d312861085836bf0626。包已由root传输、重算hash并载入镜像，未替换线上服务。原NETSDK1112是恢复/发布SelfContained不一致，00b03b1后实际完整构建已验证修复；新NAS包测试Linux12/12无skip。

同一镜像在开发Linux6.14能Ready，NAS5.10.55+首image-0-0在发图前返回status7、stderr空，随后19项未执行。宿主只读查询PR_GET_SECCOMP=-1/EINVAL22、GET_ACTION_AVAIL=-1/ENOSYS38、/proc/status无Seccomp，Docker安全模块仅AppArmor；同限制临时容器cap0/NNP/rlimits可设置，但TSYNC syscall317返回ENOSYS。原始证据由root持有，路径.runtime/nas-image-evidence-a12b0d1/kernel-capability.json、worker-first-failure.json。所有测试容器及原件不变检查均已完成。这是环境硬阻断，不是通过放宽检查修复的临时参数问题。

开发Linux额外parent-death1/1：保留stdin，仅杀中间父进程，同包Worker由SIGKILL终止并在10.385ms回收；这只属于开发Linux，不替代NAS证据。当前记录的144逻辑测试通过、1实际NAS失败、1既有本机源平台缺口保持分开；其他root/owner平台用例不重复计数。

## 边界、风险与后续

复用既有Core端口、权限/路径/worker协议与Git快照/锁/来源清单，无跨模块写表、第二解码框架或生产新服务。Skia4.151.2已审核正常/RID锁、4包签名/内容hash与MIT/native notices；声明随包且固定hash，详见decoder-dependencies.json。

AppArmor+NPROC=1未被接受为现结构的等价替代。仅把解码部署到现有dev-230的内部TLS入口仍是待用户与ADR确认的提案，没有实现或传输真实图片。当前既有浏览继续，NAS图片关闭/503；通用Docker、原生/Windows trial打包缺口与Windows剩余原生边界、Explorer、Provider、资产写入和完整版本门禁保持独立。

后续顺序：主协调确认平台方向与ADR → 精确更新任务/契约 → 获准实现与目标平台验收。现有诊断和交接完整，不启动新的架构分支来绕过阻断。
