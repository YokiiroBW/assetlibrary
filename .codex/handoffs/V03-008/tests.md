# V03-008 测试记录

## 准备阶段（2026-09-08）

Windows x64，产品基线 70ce45c，本窗口独立 worktree。此节仅记录实施前的环境准备。

`python -I -B scripts/verify_repository.py`：通过。使用 bundled Python 的绝对路径调用；含21项迁移、14项架构回归，及 handoff/schema、SDK生成漂移/源码/依赖、Web源码/依赖、原生主题和Android依赖检查。Alpha有效性审计通过且 decision=blocked；未宣称发布门禁解除。

`pnpm --dir packages/sdk/assetlink/typescript install --frozen-lockfile --offline` 和 `pnpm --dir apps/web install --frozen-lockfile --offline`：成功，全部来自原有内容寻址缓存，无下载或锁变化。初次调用的 bundled pnpm.cmd 内部固定 Node24.19.0，Web engine 报告版本不满足；没有将此记为固定运行时验证通过。

已核对项目既有 Node24.20.0：`C:/YOKI/Codex/AssetLibrary-worktrees/V01-002/.runtime/toolchains/node-v24.20.0-win-x64/node.exe`。以它显式运行 bundled pnpm.mjs，并将该 Node 目录加入当前命令 PATH，`pnpm --dir apps/web exec node --version` 返回 v24.20.0。后续构建/浏览器命令使用此方式；不修改共享工具或旧工作区运行态。

## 实现与最终静态验证

产品实现4e633e8，消费协调38aedca（本分支3f3cfba）。Node固定24.20.0，pnpm11.19.0。为避免bundled pnpm.cmd内部回到旧Node，使用本任务ignored `.runtime/tool-bin/pnpm.cmd` 调同一个pnpm.mjs，并给当前命令PATH优先使用固定Node；没有改旧工作区或共享工具。

集中实现后最终diff审查、`git diff --check`通过。`pnpm --dir apps/web run format:check`和`pnpm --dir apps/web run lint`通过；lint/typecheck定义为完全相同的TS命令，未再执行重复alias。后续小修批次后再次format:check与`pnpm --dir apps/web run build`通过（构建包含相同的完整TS检查及SDK构建）。最终产物JS301917 B、CSS27768 B，hash见build-evidence.json。

`python -B scripts/validate_web_source.py`通过。`python -B scripts/validate_web_dependencies.py --require-build-artifacts`失败：旧阈值JS262144/CSS16384/合计286720低于新产物，亦低于V01-025交接中的原产物。未改policy、未移开dist绕过，也未将失败计通过；已提交root预算变更提案。

最终`python -I -B scripts/verify_repository.py`在上述预算处中止；之前handoff、architecture、Alpha blocked审计、373个C#源码、21迁移manifest/21项回归、SDK生成7文件/源码/依赖已通过。最终aggregate不记通过，预算问题后由协调修正再验证。此前通过的14架构回归不重复求和。

## 浏览器验证与修正记录

全部使用既有Playwright1.62.1/Chromium、独立上下文、2 workers，同源Vite4173及合成PNG/临时有界HTTP fixture。无真实NAS内容。结果中的阶段重复用例只统计一次：**既有43 + 新图片22 = 65个不同用例，均有通过证据，0个未解决功能失败，0跳过**。

1. `pnpm --dir apps/web run test:browser image-preview.spec.mjs --workers=2 --output ../../.runtime/playwright-results/V03-008-images`：16项，13通过/3失败，31.2s。失败为两个视口的Tab焦点可移至浏览器工具栏，以及busy测试把StrictMode首次挂载取消混作同一次逻辑重试。
2. 补Modal焦点循环、busy改为直接调用一次客户端请求以检查3次请求和Retry-After间隔；运行 `--grep 'image preview preserves|busy image retries|^(?!.*image-preview.spec)'` 到V03-008-regression：46项，44通过/2失败，43.8s。既有43项全部通过；剩余焦点失败定位为收起details里的控件仍可能有client rect，必须排除非summary后代，不能仅按几何判断可聚焦。
3. 焦点过滤修正、同步pagehide/blur卸下图片并改用layout effect清理，新增排队/预算、BFCache、新源字节、重解析条目回归；`--grep 'image preview preserves|image admission|Retry-After shares|reopened images|directories and reparse|foreground revalidation|registration dialog keeps|multi-selection'` 到V03-008-boundaries：9项，8通过/1失败，6.5s。队列测试揭示deadline到期时timer callback排队，腾空slot会启动已过期条目；已在实际admission再次按performance.now核对截止。
4. 队列修复、解码总像素上限回归及最终比例/文字尺寸布局修正后，`--grep 'image preview preserves|image admission|image leases|derived thumbnails|closing a pending preview'` 到V03-008-final：6/6通过，9.9s。47个最大缩略图候选仅45个成功（11796480像素），2个超限；dispose后Object URL归零；48个排队/活动租约到20秒全部到期，实际启动仍仅2个。
5. 对最终layout清理/弹窗改动影响的拒权、重试、格式错误、身份、源变化与重解析条目执行 `--grep 'image (401|403|404)|retains L0|malformed MIME|foreground revalidation|reopened images|directories and reparse'`，V03-008-cleanup-recheck中12/12通过，7.2s。没有再次执行输入未变的20秒真实断流、旧JSON5秒期限或全部旧43项。

协调预算收敛：23ad33d5618eb825e60cd941a07962c3ba8b19e1已在本分支合为f6fecae。由协调单写policy为327680/32768/360448，确认既有不可变Web产物290457/25942 B与当前301917/27768 B，增量11460/1826 B，无新依赖。按协调要求仅重新执行原`python -B scripts/validate_web_dependencies.py --require-build-artifacts`，**通过**；上述预算失败已关闭。源码/产物没有改变，不重新跑64浏览器用例或整个聚合入口；此前中止的那次verify_repository仍如实记录中止。

真实HTTP停滞测试证明图片正文在19..24秒范围关闭、返回preview_timeout；既有JSON停滞仍4..8秒且advertised timeout为5000。401/403/404在不完整且不结束的错误正文前生效，2秒内隐藏图片/退出或安全降级。429最多两次重试间隔至少900ms，Retry-After60的总预算仍20秒，不启动新的重试预算。

取消覆盖离屏/滚动回返、关闭、旧响应、后台/失焦、会话变化与BFCache；新源再次打开得到新的像素尺寸。MIME、PNG头/尺寸、超大Content-Length、坏签名/截断及错误私密正文均不作为图片显示。目录、reparse_file和reparse_directory不发图片请求。恢复请求每次走服务端，不复用已释放缓存。

## 视觉、清理与范围

最终调用方补查：d944300复用同一图片拒权清理入口，补齐L0 entries.get返回403/404而图片端点尚未返回拒权时也撤销现存缩略图。新增该负向例，与图片401/403/404共4/4通过，3.9s，输出V03-008-detail-denial；未重复未变的64例。由于这次确有产品源码变更，随后最终format:check、build（含TS）、validate_web_dependencies --require-build-artifacts、validate_web_source与validate_architecture_baseline全部通过。新JS302081 B/SHA256 f7284e168ac0dcbe59db7e5d95a654e7be37227f7200b61799aa91045ecbd708；CSS27768 B未变，最终产物source为d944300。代码变更不改变已核查的视觉布局。

已用view_image检查最终1440×900浅色、390×844暗色、390×844且根文字32px（200%）截图，复制到本handoff/screenshots。预览图保持长宽比，关闭按钮在屏内，长标题换行，Tab/Shift+Tab循环、Escape与历史返回焦点有效。初始CSS zoom截图不是有效字体缩放证据，已改为真实文本尺寸验证，不使用被裁切的旧图。

PNG是Node标准库生成的有限RGBA合成渐变/透明数据，图片内容由fixture返回，明确不是实际服务器解码或NAS验收。HTTP fixture均finally关闭，Playwright/Vite已退出且4173无监听。未部署NAS、扫描个人资产或执行原资产写操作。

服务端原图hash/mtime、真实Core/PG/HTTPS/CSP、共享权限编排、引擎隔离和跨端验收由V03-005/V03-007负责。没有50万资产或长时浏览器进程总内存压力证据；解码像素/Blob预算不能等同整个浏览器进程峰值。未晋级任何Provider/Explorer/写入或完整发布门禁。

## 真实Core入口准备（9bf074e）

按root后续委派新增tests/web内独立CLI，消费root5edf25c共享fixture。未修改apps/web、root serve.py/测试程序集或生成corpus，没有启动/停止共享服务。说明与命令见`tests/web/real-core-image-preview.md`。

- `node --check tests/web/real-core-image-preview.mjs`、`node --check tests/web/real-core-image-support.mjs`通过。
- `node --test tests/web/real-core-image-input-check.mjs`：4/4通过、0skip，131.6ms；仅覆盖入口origin/秘密字段/寿命/148样例约束、十项manifest和PNG规格/方向/截断断言，不是实际TLS或Core图片执行。
- 新增4文件Prettier检查通过，最终diff审查及`git diff --check`通过。
- `pnpm --dir apps/web exec playwright test --list`只收集65项/6文件，证明显式真实CLI和Node输入检查未进入默认假数据套件；没有重复执行65项。
- CLI `--help`返回0；无`--execute`返回77且不执行，符合显式准入约定。

真实CLI须等root报告同一fixture READY后用私密connection文件执行。凭据不进命令/输出/trace，失败只报固定阶段。PNG与HTML/JS/CSS全部从真实HTTPS Host读取，不拦截/替换route；产物hash与审查build-evidence核对。普通无权限账号拒权不直写DB；同账号动态撤权和原件/进程/DB清理由root控制。此阶段总计新增4项工具检查，消费者65项证据保持，真实运行尚无通过或失败记录。

root审查后的发现断言修正：确认0020 search_read_entries_v2没有文件kind过滤，查询会返回物理目录；脚本现先校验所有hit的library/entry关联、UUID、kind与wire name，再仅筛kind=file及精确`图片样例/`前缀，断言10个预期文件唯一齐全，不通过Map吞重复。确认`services/core-server/Adapters/AssetLink/AssetLinkReadJson.cs:96`直接写出wire `name`，前端decodeEntry只是读取而非生成，故继续使用经校验的真实名称。增加目录/相似前缀正例、过滤前跨库拒绝、缺项/重复ID/重复路径/非文件/无名或假名负例；Node输入套件现7/7通过、0skip（125.0ms），两个runner模块语法和diff检查通过。没有修改产品搜索/后端、没有重跑65项自测或启动真实服务。总计为65个既有浏览器通过加7个工具输入通过，仍无真实Core图片成功声明。

## 真实连接首轮环境中断

root提供Linux Core源码874fb6a、worker40d2d69、localhost44147转发与私密连接后，执行已提交CLI到本任务`.runtime/real-core-preview/linux-live-874fb6a`。执行会话随后变为Unknown process，目录为空且没有最终receipt/截图；本机核查无该runner Node或指定SPKI的Chrome残留。**无证据首轮不计产品通过或明确产品失败。** root随后独立确认SSH转发会话也丢失、执行helper故障，与其他窗口运行态消失有同时性，但未断言具体根因。

runner099d579补齐固定阶段progress.json/stdout、git子进程5秒期限、整体Promise.race期限和浏览器10秒关闭确认，避免仅关闭浏览器却不能结束未知await；后续阶段也检查总截止。语法/格式通过。暂停消息到达前的诊断复验输出`linux-live-874fb6a-recheck/web-real-image.json`：读连接/公共输入和provenance完成，**verify_tls_identity失败**，cases和screenshots均空，浏览器尚未启动且未发送登录凭据。这是转发不可用时的前置失败，不是图片功能失败；未修改生产客户端、TLS断言或共享数据，未stop服务。

收到root暂停要求后停止所有真实重试，等待其恢复转发并再次READY。没有重跑65项mock或7项未变工具输入测试。connection未复制/打印，私密文件由root统一清理。

## 同一真实fixture的最终收敛

root恢复授权后，本窗口只检查ssh -V与空闲端口，在自有任务内建立BatchMode/StrictHostKeyChecking/ExitOnForwardFailure、loopback44147转发。证书叶SHA、localhost与有效期前置再次通过。转发所有权由PID20820/开始时间/可执行路径记录；没有改root或服务端文件。

先后定位两个runner收集问题：Playwright1.62.1的requestfailed不resolve Response.finished，不能等待取消请求；因此在真实UI图像已显示且缩略工作暂停后顺序同源GET（20秒/字节上限）核对wire，未替换UI响应。浏览器原生Response.headers是属性，旧headers()误用导致收集前异常，已修正并记录固定安全header/dimension诊断。完整历史receipt在本机.runtime，失败不改写、不计通过。

旧Web d944300实际完成10项PNG/方向/alpha与3图，但真实键盘定位揭示产品焦点缺陷：mounted行间方向键已改变selected，却不改变DOM焦点，因pendingFocus效果只依赖virtualItems。新增最小回归明确失败（第二行selected=true/toBeFocused inactive），77271e0补selection.focusedId依赖。新用例+QuickLook1440/390+既有多选4/4通过，7.5s；其他65未重跑。最终format、TS/build、Web源码、产物预算通过。现**66个不同浏览器用例、7个工具输入检查**，无未解决失败/skip。

root审查并将77271e0静态Web切入同一Core874fb6a/worker40d2d69/PG/148样例fixture，保留旧dist；未变Core/DB/资产。最终JS302093 B/SHA256 b248f3ac02a29f82a795bf4716ca5604f67c5e556089cf2bc08810ce192866ea，CSS27768 B/SHA256 c4b01b9d4517d1e554e5508b0764981a6fe45d30cf622f5fdf195c52a4bdf4a1。真实入口重新核TLS与实际HTTP静态哈希后登录。

- `linux-live-77271e0-acceptance/web-real-image.json`完成16项图片/错误检查，顶层在最后QuickLook pointer定位处failed，原文保留。实际返回12个搜索hits，其中校验得到10文件；12个成功图片检查覆盖JPEG/PNG/WebP、EXIF6、透明与中文.dat；4错误为SVG415、不支持非图415/422、截断422、超限422并保留L0。透明384×512/900×1200、角alpha0/中心160；JPEG与中文.dat派生SHA一致。所有用户可见步骤走真实Home/ArrowRight和双击，正常关闭回焦、响应式trigger卸载后的工作区回焦均验证。
- `--interaction-only`只继续收尾，未再次做16项wire；重新校验同TLS/Webhash/发现。最新cb4f66e的`linux-live-77271e0-interactions-final/web-real-image.json` **passed，exit0，scope=interaction_continuation，cases为空**。实际Space打开、Escape关闭、历史不变、toBeFocused原行；退出全部URL释放；普通账号已知ID404/非image，匿名401；无CSP绕过/违规、无脚本/跨源请求，浏览器关闭确认。
- [acceptance-index.json](real-core/acceptance-index.json)明确两阶段同fixture，绑定原case receipt SHA84fd9c37b4b671b085a70c755c80ed6ec5b9f498245a9985c765630fb902e72f与最终interaction SHAfe106a357eb5cc12a7699f4b2f43a22fadec0c1fa2de783850d3ff4e5afaebad。复制前后原SHA一致，未把failed顶层改成passed，也未把continuation的0个图片case写成16。真实客户端结论由两阶段共同形成，不虚报一次全跑。

已查看real-core下最终桌面、手机透明/暗色、200%文字与无权限账号截图（实际Web77271e0）。其来源与早先mock screenshots明确分开，未截登录凭据或复制私密JSON。

root显式stop后提供公开清理receipt `.codex/handoffs/V03-005/real-core-cleanup.json`，SHA751cc658fef1eb608d247f4842be68b359cdf9c10f344b00693ae0b8e2aaa04b，已只读核对。root verified覆盖148源hash/mtime不变、6LOGIN、runtime、Host/PG、HTTPS及自有容器清理；Windows/远端私密connection副本由root删除。本任务按PID/创建时间/执行路径核对关闭SSH20820，WaitForExit通过、44147无监听，公开forward-cleanup.json记录closed_verified。历史receipt的owned_and_verified_by_coordinator只是当时责任标签；最终索引以root canonical receipt确认共享清理。
