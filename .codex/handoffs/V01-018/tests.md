# V01-018 验证记录

日期：2026-09-07；实现：`d5ac7f69df48f35cdf4db48ad1d2dd91da42ed50`。
最终结果：73 passed / 0 failed / 0 skipped；不重复累计修正前的运行和定向复验。

## 环境与真实命令

Windows，Node.js `v24.20.0`、pnpm `11.19.0`，仓库固定React/TypeScript/Vite/Playwright版本。会话级PATH前缀为：

```text
C:\YOKI\Codex\AssetLibrary-worktrees\ALIGN-003\.runtime\tool-bin
C:\YOKI\Codex\worktrees\V01-006\.runtime\toolchains\node-v24.20.0-win-x64
```

Python使用 `C:\Users\Administrator\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe`。没有改全局PATH或安装新工具。

| 命令 | 最终结果 |
|---|---|
| `pnpm --dir apps/web install --frozen-lockfile` | 通过，锁未改变 |
| `pnpm --dir packages/sdk/assetlink/typescript install --frozen-lockfile` | 通过，锁未改变 |
| `pnpm --dir apps/web run format:check` | 通过 |
| `pnpm --dir apps/web run lint` | 通过 |
| `pnpm --dir apps/web run typecheck` | 通过 |
| `pnpm --dir apps/web run build` | 通过，真实SDK编译和Vite产物 |
| `pnpm --dir apps/web run test:browser` | 39通过，17.2秒，无重试/跳过 |
| `pnpm --dir apps/web audit --audit-level low` | No known vulnerabilities found |
| `python -B scripts/validate_web_source.py` | 通过，网络仍只有一个fetch |
| `python -B scripts/validate_web_dependencies.py --require-build-artifacts` | 通过，原依赖/许可/体积预算 |
| `python -B scripts/generate_assetlink_sdks.py --check` | 7个生成文件一致 |
| `python -B scripts/validate_architecture_baseline.py` | 130个架构输入通过 |
| `python -B -m unittest discover -s tests/architecture -p "test_*.py" -v` | 14通过 |
| `python -B -m unittest discover -s tests/repository -p test_web_gateway_foundation.py -v` | 6通过 |
| `python -B -m unittest discover -s tests/sdk -p "test_*.py" -v` | 14通过；故意篡改生成文件的负向fixture输出ERROR后测试按预期通过 |
| `git diff --check`和完整diff复核 | 通过，所有文件属于授权范围 |

## 浏览器覆盖与修正

保留22项原浏览/搜索/分页/虚拟化/权限缓存/实际请求与正文截止/调用方取消/畸形拒绝回归。新增17项验证同源会话与CSRF、登录失败/限流/退出失败、绝对到期、多标签换身份、同主体会话代次变更、focus拒绝或503区别、管理员登记/显式初扫、重用请求身份、取消/重试、恢复已有任务、隐藏页停轮询、离线快照/恢复、扫描拒绝与畸形计数、存储源上限、键盘弹窗。

首次运行29/37通过，发现新fixture对名称/路径重复文本定位不唯一、select隐式标签定位不明确、弹窗首次焦点问题；修正标签和焦点后36/37通过，剩余Tab越出边界由弹窗首尾循环修复，单项复验通过。随后补齐session明确拒绝和扫描拒绝后的游标保护，最终39/39完整运行通过。没有跳过失败、放宽时间阈值或自动批准截图基线。

最终完整日志：`.runtime/V01-018/browser-final.log`。运行命令为标准test:browser；早期名为browser-targeted.log的文件实际跑了全37项，不将其当作定向测试或最终成功证据。

## 视觉与产物

截图均配对DOM/交互断言，人工查看1440×900桌面、390×844窄屏，locale zh-CN、Asia/Shanghai、reducedMotion reduce。最终产物目录：`.runtime/playwright-results/V01-018`。

- `read-only-workspace-deskto-d19ec-tered-paged-and-virtualized-chromium/workspace-desktop.png`
- `read-only-workspace-narrow-3bfac-s-the-empty-directory-state-chromium/workspace-narrow.png`
- `trial-library-scan-adminis-97caf-browsing-real-indexed-names-chromium/register-desktop.png`
- `trial-library-scan-adminis-97caf-browsing-real-indexed-names-chromium/scan-desktop.png`
- `trial-library-scan-offline-02336-through-an-explicit-refresh-chromium/offline-narrow-dark.png`
- `trial-session-login-uses-p-5179c--logout-hides-the-workspace-chromium/login-narrow.png`

`apps/web/dist/assets/index-DkayHSuZ.js`：250286 bytes，gzip约77.90KB，SHA256 `95ac530f4e0cc5a65ce20a50cff67fa9b172510ea672609ee38743c9bb7d2cc7`。

`apps/web/dist/assets/index-yo48TXrB.css`：11507 bytes，gzip约3.33KB，SHA256 `a96272f6d7f6932742dcb9342c26766f1c4bd10ebfcdbf8238094bc33e02fe45`。

## 证据边界与清理

这是组件/适配器fixture验证。浏览器控制和认证响应由隔离fixture提供，旧deadline测试包含本地真实挂起HTTP响应；没有真实HTTPS认证/数据库/磁盘扫描，因此这些集成证据由V01-015补齐。未运行无关.NET、SQL或完整发行测试，不代表它们通过。

Playwright管理的服务器/浏览器已退出，保留ignored的node_modules、dist、日志和截图供协调器复核。不删除其他任务或历史worktree，不修改真实资产与系统状态。交付后Git应为clean。
