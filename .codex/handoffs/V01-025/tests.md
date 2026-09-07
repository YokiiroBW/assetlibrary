# V01-025 验证记录

## 环境与命令

Windows x64；固定 Node v24.20.0、pnpm 11.19.0、Playwright 1.62.1 / 已有 Chromium 缓存。使用原 pnpm store，不增加依赖或下载。

从仓库根执行：

```text
pnpm --dir packages/sdk/assetlink/typescript install --frozen-lockfile --offline
pnpm --dir apps/web install --frozen-lockfile --offline
pnpm --dir apps/web run format
pnpm --dir apps/web run format:check
pnpm --dir apps/web run lint
pnpm --dir apps/web run typecheck
pnpm --dir apps/web run build
pnpm --dir apps/web run test:browser
```

两项 install、最终 format:check、lint/typecheck、build 通过。初次 lint 的 History key 模板字符串类型推断错误已修正为 useRef<string>，未改变运行时语义。小修批次之后再通过 format:check 和含相同 TS 门禁的 build；之后只有 README/交接更新，没有重复未变构建。

## 浏览器结果

完整首轮运行 43 项，37 通过、6 失败，耗时 45.7s。失败的具体原因与集中修正：

- 下拉框包裹 label 的文本包含 option 文本：增加明确 aria-label，保持对外中文标签稳定。
- 共享 Modal 的 cleanup 读取暂时清空的 ref，StrictMode 探针未关闭原 dialog，导致关闭后焦点不能回到触发按钮：捕获 element 后 close，与既有 Register 的生命周期一致。
- 多选断言误将筛选下拉的 selected option 算作资产：限定到资产 listbox。
- headers 超时 fixture 在深链初次 StrictMode 挂载时记录额外的快速取消连接：改为首页挂载稳定后通过真实库导航发起单次用户请求，保留 4–8 秒断开、5 秒 advertised deadline、可重试断言。

集中修正后只运行受影响 7 项（6 个失败，加上同 fixture 的 body 超时例）：

```text
pnpm --dir apps/web run test:browser --grep "stalled (headers|body)|narrow workspace|home, category|multi-selection|scope, server|administrator registers" --output ../../.runtime/playwright-results/V01-025-recheck
```

7/7 通过，9.6s。最终 43 个不同用例全部有通过证据、失败 0、跳过 0；没有再次运行其他 36 项。

保留原 40 个取消/总期限/响应体停滞、401/403/404/畸形响应、换身份/跨标签页/绝对过期、首次扫描显式启动/取消/失败重试/离线用例。仅新增 3 个关键交互例：分类与 CAS 冲突；键盘多选/复制/网格/手机抽屉；搜索来源恢复/全范围过滤/非法 URL。BFCache 拒权并入原会话用例，冒号路径并入已有深链用例。

## 截图与可见行为

均为本工作树下相对路径，已用 view_image 查看以下代表图；无截图快照基线或自动批准基线。

- `.runtime/playwright-results/V01-018/read-only-workspace-deskto-6f93c-zed-and-restores-navigation-chromium/workspace-desktop.png`
- `.runtime/playwright-results/V01-018/trial-library-scan-offline-02336-through-an-explicit-refresh-chromium/offline-narrow-dark.png`
- `.runtime/playwright-results/V01-025-recheck/read-only-workspace-home-c-03e74-se-explicit-server-metadata-chromium/workspace-home.png`
- `.runtime/playwright-results/V01-025-recheck/read-only-workspace-multi--7268a-hare-real-entry-information-chromium/workspace-grid-details.png`
- `.runtime/playwright-results/V01-025-recheck/read-only-workspace-multi--7268a-hare-real-entry-information-chromium/workspace-mobile-details-dark.png`

同一复验输出还包含 workspace-narrow.png、register-desktop.png、scan-desktop.png。手机 390×844 暗色/降低动画无横向溢出，详情为可关闭抽屉；桌面 1280/1440 视口显示紧凑导航/列表或网格/右侧文件信息。虚拟列表 100 项时渲染行数少于 40。

## 边界

生产输出 JS 290.45 kB/gzip89.55 kB；CSS25.94 kB/gzip5.93 kB。分页与虚拟化并非 50 万条数据库性能测试，真实规模 SQL 与 HTTPS/PG 由协调器统一验收。浏览器自动化的剪贴板成功写入使用受控 transport；拒绝后的手动复制弹窗当前只有静态生命周期审查。没有真实资产写操作、自动初扫、通用重扫或新的生产测试开关。
## 移动端选择位移回归（1336afe）

由真实 HTTPS/PG E2E 在 390px 单条搜索结果触发的位移，采用固定窄屏工具栏两行布局修正。原有多选/手机用例增加选择前、选择后和取消选择后的 row boundingBox.y 完全相等断言，并执行真正的 dblclick，确认资产详情 dialog 可见。没有新增测试数量或产品功能。

```text
pnpm --dir apps/web exec prettier --check src/styles.css ../../tests/web/read-only-workspace.spec.mjs
git diff --check
pnpm --dir apps/web run build
pnpm --dir apps/web run test:browser --grep "multi-selection, keyboard, grid and mobile details" --output ../../.runtime/playwright-results/V01-025-mobile-toolbar
```

格式、diff 和含类型检查的 build 全部通过；只复验该例，1/1 通过、0 failed、0 skipped，3.8s。本轮仅复验定位到的手机选择行为，其余 42 项未重复运行。新 dist 的 CSS 为 index-DedOXqR9.css（25.94kB/gzip5.93kB），JS 为 index-cVzg_3bQ.js（290.45kB/gzip89.55kB）；真实环境由协调器继续复验。