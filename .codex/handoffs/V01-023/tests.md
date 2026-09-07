# V01-023 Web统一验收记录

2026-09-07。此前按用户要求集中实施，未运行检查；根随后明确启动统一验收，本记录替代原待验收状态。受验HEAD为093adf313c8e580454fb7c5f11c8f720f9ee8338，已merge根858dcf3，包含Web实现bc3d151和68e7250。

## 环境

Node v24.20.0，pnpm11.19.0，Playwright1.62.1，沿用现有Chromium缓存。会话PATH前缀为ALIGN-003/.runtime/tool-bin及V01-006的固定Node24.20.0目录；未改全局PATH，没有安装新工具或更改依赖锁。

## 执行结果

| 命令 | 结果 |
| --- | --- |
| pnpm --dir packages/sdk/assetlink/typescript install --frozen-lockfile | 通过，缓存复用，锁未变 |
| pnpm --dir apps/web install --frozen-lockfile | 通过，缓存复用，锁未变 |
| pnpm --dir apps/web run format:check | 通过 |
| pnpm --dir apps/web run lint | 通过 |
| pnpm --dir apps/web run typecheck | 通过 |
| pnpm --dir apps/web run build | 通过，实际SDK与Vite产物 |
| pnpm --dir apps/web run test:browser | 一次完整40/40通过，19.2秒，0失败/重试/跳过 |

没有发现需修正的源码或格式问题。没有追加测试集、截图基线或放宽断言，也没有重跑已成功且输入未变的检查。日志在.runtime/V01-023/{format,lint,typecheck,build,browser-full}.log。

40项既有测试包含：photos源+/assets/photos原样登记及关联帮助；保留Windows路径错误重试；首次快照/刷新不重扫说明；entry_path_unsupported反斜杠名称失败提示与可重试；既有认证/CSRF/换身份/拒权清理、截止/取消、分页/虚拟化、扫描状态及键盘交互。字段与测试数量未扩大。

## 截图与产物

既有输出目录名称仍为.runtime/playwright-results/V01-018，但实际位于本V01-023 worktree内，未写其他worktree。已查看：

- trial-library-scan-adminis-97caf-browsing-real-indexed-names-chromium/register-desktop.png：1440×900，容器/Windows帮助可读，焦点可见。
- trial-library-scan-offline-02336-through-an-explicit-refresh-chromium/offline-narrow-dark.png：390×844暗色，离线原索引与首次快照提示可见。
- read-only-workspace-narrow-3bfac-s-the-empty-directory-state-chromium/workspace-narrow.png：390×844，空目录与底部详情布局正常。

其余现有产物包括workspace-desktop.png、scan-desktop.png、login-narrow.png；没有新建黄金快照。.last-run.json为passed，failedTests=[]。

| 构建文件 | SHA-256 |
| --- | --- |
| apps/web/dist/index.html | 0cd28149941a41b664a547fd6ccf1c674790277c2eaa27fb52cbfd6d8b68a0ee |
| apps/web/dist/assets/index-nKE0a30X.js | 1d1f678cb964a5a90d3d4e63e6a5d8a886e637224a1d7ee9757db7deccdd8894 |
| apps/web/dist/assets/index-yo48TXrB.css | a96272f6d7f6932742dcb9342c26766f1c4bd10ebfcdbf8238094bc33e02fe45 |

Vite报告JS251.41kB/gzip78.44kB，CSS11.50kB/gzip3.33kB。CSS与前版相同，保持原设计令牌和布局。

## 证据边界

这是现有Web组件/浏览器fixture验收，认证与控制响应由隔离fixture提供，不能替代真实NAS镜像/HTTPS/数据库/物理资产闭环。根同时负责.NET与仓库静态，以及后续NAS统一部署验收；本任务没有重复或宣称这些检查通过。

Playwright管理本地服务器/浏览器生命周期；保留ignored node_modules、dist、日志、截图供根复核。没有操作真实资产、NAS、证书或系统设置。
