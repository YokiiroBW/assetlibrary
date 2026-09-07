# Web

V0.1 的只读试用工作界面。使用同源登录、会话查询和退出接口；通过生成的 AssetLink SDK
列出已授权资源库、浏览物理目录事实、搜索文件名/相对路径。系统管理员可以登记部署者
配置的存储源内的真实目录，显式启动首次扫描，并查看、取消或重试未完成的扫描。

NAS 容器部署按部署挂载表登记路径：资产以只读方式挂载到 `/assets/<source_key>`，例如
`photos` 存储源对应 `/assets/photos`，也可登记该根内的子目录。不能直接把 NAS 宿主
路径或浏览器电脑的盘符当作容器内可用路径。Windows 原生部署仍可使用服务端可访问的
本地盘符或 UNC 目录；所有登记都由核心检查存储源范围和权限，前端不推断或改写路径。

接口与字段以 `contracts/assetlink/read-only-trial-v1.md` 为准。Cookie 自动同源发送，
CSRF 只在当前会话内存中保存；认证失效、换账号及拒权会清理对应浏览状态。原文件保持
只读，当前不提供文件内容下载、上传、移动、改名、删除、预览或通用重扫。

生产构建产物位于 `apps/web/dist`，由同一 HTTPS 试用 Host 托管。Vite 入口用于开发及
隔离浏览器 fixture，不提供产品中的认证绕过或任意 API 地址设置。正式试用部署与真实
HTTPS/PostgreSQL 端到端验收见 V01-015；NAS 容器交付遵循 ADR-0015，由 V01-021 统一验收。
本目录的 fixture 测试不能替代对应部署环境的真实验收。

本地命令从仓库根目录执行：

```text
pnpm --dir apps/web install --frozen-lockfile
pnpm --dir apps/web run format:check
pnpm --dir apps/web run lint
pnpm --dir apps/web run typecheck
pnpm --dir apps/web run build
pnpm --dir apps/web run test:browser
```

浏览器测试使用 `tests/web` 内的会话、控制请求和有界断连 fixture。完整结果与代表性
截图写入 `.runtime/playwright-results/V01-018`。例如，只检查登记弹窗键盘路径可执行：

```text
pnpm --dir apps/web run test:browser --grep "dialog keeps"
```

界面仅显示实际扫描计数，不估算总量或百分比。当前库有活动任务时每 2 秒读取一次状态；
失败重连每 5 秒一次；隐藏页面停止轮询、切库/退出中断旧请求。已成功的首次快照不显示
再次初扫入口。离线仍显示上次成功的索引，恢复后可以刷新或重试未完成的扫描。
完成提示明确说明这是首次扫描的索引快照；刷新重新读取现有索引，不会发现后续文件变化
或触发重新扫描。此限制对 Windows 和 NAS 容器相同。
