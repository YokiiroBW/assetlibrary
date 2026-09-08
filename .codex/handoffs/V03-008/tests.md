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
