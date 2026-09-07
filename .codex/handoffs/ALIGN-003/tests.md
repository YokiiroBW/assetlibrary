# ALIGN-003 测试记录

## 执行环境

- Windows x64；独占 worktree `C:\YOKI\Codex\AssetLibrary-worktrees\ALIGN-003`，实现 commit `7a1ed4203f8fd2de695e270ef543652839abe551`。
- Node.js `24.20.0` 复用 `C:\YOKI\Codex\worktrees\V01-006\.runtime\toolchains\node-v24.20.0-win-x64`；pnpm `11.19.0` 使用当前 worktree `.runtime/tool-bin/pnpm.cmd`。本进程 PATH 将这两个目录前置，`pnpm_config_pm_on_fail=ignore`，防止系统包管理器重选旧 Node；未修改永久 PATH 或工具锁。
- Python 使用 `C:\Users\Administrator\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe`，命令带 `-B`。
- Playwright `1.62.1` Chromium，4 workers、0 retries，locale `zh-CN`、timezone `Asia/Shanghai`、reduced motion；Vite 与故障 fixture 只监听 loopback。开始恢复工作前确认无遗留相关进程，测试服务由 Playwright 正常启动/回收。
- 所有新增证据在 ignored `.runtime/ALIGN-003/` 与既有 `.runtime/playwright-results/V01-006/`；没有真实资产或数据库访问。

## 最终执行命令与结果

以下命令均从任务 worktree 执行，`python` 指代上述固定 Python。最终源码稳定并完成完整 diff review 后执行；成功后没有重复未变输入的同项检查。

```text
pnpm --dir apps/web run format:check
pnpm --dir apps/web run lint
pnpm --dir apps/web run typecheck
pnpm --dir apps/web run build
python -B scripts/validate_web_source.py
python -B -m unittest discover -s tests/architecture -p test_*.py -v
python -B -m unittest discover -s tests/repository -p test_web_gateway_foundation.py -v
pnpm --dir apps/web run test:browser
pnpm --dir packages/sdk/assetlink/typescript audit --audit-level low
python -B scripts/validate_web_dependencies.py --require-build-artifacts
pnpm --dir packages/sdk/assetlink/typescript run test
git diff --check
```

| 范围 | 最终结果 | 本地证据 |
| --- | --- | --- |
| Web format/lint/typecheck/build | 全部成功 | `.runtime/ALIGN-003/web-{format-check,lint,typecheck,build}.log` |
| Chromium 完整套件 | 22 passed，0 failed，0 skipped；12.7 秒 | `.runtime/ALIGN-003/browser-final.log` |
| 架构单元回归 | 14 passed | `.runtime/ALIGN-003/architecture-tests.log` |
| Web repository 定向回归 | 6 passed | `.runtime/ALIGN-003/repository-tests.log` |
| TypeScript SDK native | 5 passed，0 failed，0 skipped | `.runtime/ALIGN-003/typescript-sdk-tests.log` |
| SDK audit | 无已知漏洞 | `.runtime/ALIGN-003/typescript-sdk-audit.log` |
| Web 依赖/完整性/许可证/体积 | 通过 | `.runtime/ALIGN-003/web-dependencies.log` |
| Web 源码边界、最终 diff | 通过 | 当前任务终端输出及实现 commit |

唯一自动测试计数为 **47 passed、0 failed、0 skipped**，未把构建或策略命令计作测试，也未重复计算中间浏览器执行。

交接阶段只核对现有日志与文档：复用仓库 `SchemaStore` 校验 `result.json`，确认实现 commit、相对基线的完整 15 文件清单、去重计数和 Markdown 空白格式一致；新增任务及交接文件的 `git diff --cached --check` 通过。没有源码变更，也没有重跑上述产品测试。

本任务先前阶段已通过且输入未改变的命令沿用现有结果：

```text
pnpm --dir apps/web install --frozen-lockfile
pnpm --dir apps/web audit --audit-level low
python -B scripts/validate_architecture_baseline.py
python -B scripts/generate_assetlink_sdks.py --check
```

## 复现与故障回归

- 恢复工作时已有最终选择范围的 `.last-run.json` 显示 passed，但没有完整执行计数。为恢复覆盖范围证据实际执行完整套件，得到 18/18，日志 `.runtime/ALIGN-003/browser.log`。
- 完整审查进一步发现，SDK 的开放 envelope 解码不会校验 `error.code/message`，客户端后续字段异常会丢失 401/403/404；已收 401 响应头、正文挂起的请求又会被截止处理覆盖为 504。
- 添加 4 条回归后、补全状态保护前执行：`pnpm --dir apps/web run test:browser --grep "malformed error|keeps its status"`。结果 **4 failed**，证据 `.runtime/ALIGN-003/rejection-reproduction.log`；失败截图、上下文和 trace 已保存在 `.runtime/ALIGN-003/rejection-reproduction-artifacts/`。
- 401 畸形错误未显示登录失效；403/404 畸形错误仍显示条目行与详情中的 4 处旧文件名；401 正文挂起未显示登录失效。修复保留已收到的失败 HTTP 状态后，最终完整 22 场景一次通过。
- 无响应头和成功响应正文挂起的请求在 5 秒截止后关闭真实连接，断言范围 4–8 秒，并能重试成功；wire `timeout_ms` 始终为 5,000。
- 搜索变化取消旧请求、重新连接取消上个工作界面的后续分页，连接关闭均小于 2 秒；等待原截止时刻后仍无误报超时。已取消的调用保留同一个 DOMException，网络请求计数不增长。
- 401 重认证清空旧选择与搜索词；403/404 浏览分页、资源库分页拒绝、搜索分页拒绝都清除相关旧条目和详情；非协议 401 同样隐藏旧数据。503 保留上一可读页并显示失败，不显示空目录成功。
- 原 6 个桌面/窄屏、分页/窗口化、目录/跨库导航、请求关联/页大小拒绝与陈旧搜索回归继续通过。

## 桌面、窄屏与性能

- 已目视检查 1440×900 桌面截图 `.runtime/playwright-results/V01-006/read-only-workspace-deskto-d19ec-tered-paged-and-virtualized-chromium/workspace-desktop.png`：三栏、虚拟列表、目录返回和操作按钮正常。
- 已目视检查 390×844 窄屏完整页截图 `.runtime/playwright-results/V01-006/read-only-workspace-narrow-3bfac-s-the-empty-directory-state-chromium/workspace-narrow.png`：搜索置顶、单列重排、资源库和空状态可见，无横向裁切。
- Vite 产物 JavaScript 231,523 bytes（gzip 72.47 kB）、CSS 8,922 bytes（gzip 2.76 kB），联合 240,445 bytes，低于 286,720-byte 预算。
- 浏览器 100 条页实际 DOM 仍少于 40 行。每个请求仅新增一个计时器和取消监听器，额外成本 `O(1)`，不改变分页、搜索或索引协议。

## 失败、跳过与未覆盖

- 最终必要检查无失败、无跳过。上述 4 次失败属于保留的预修复复现；早期定位器重复匹配已修正，最终断言未放宽。
- SDK audit 首次遇到 `ECONNRESET`，pnpm 自动重试后成功，无已知漏洞；未更换 registry 或关闭审计。
- 未重复未改动的 .NET/真实 PostgreSQL 全套；本任务不实现宿主认证、权限管理、真实文件写操作或解除发布门禁。
- 未覆盖生产身份切换联调、权限推送通知、50 万真实 NAS/P95、Firefox/其他平台和正式发布演练。它们不是本任务 47 个通过项的一部分。
