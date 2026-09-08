# V03-008 测试记录

## 准备阶段（2026-09-08）

Windows x64，产品基线 70ce45c，本窗口独立 worktree。尚未实现预览，因此没有预览功能通过证据。

`python -I -B scripts/verify_repository.py`：通过。使用 bundled Python 的绝对路径调用；含21项迁移、14项架构回归，及 handoff/schema、SDK生成漂移/源码/依赖、Web源码/依赖、原生主题和Android依赖检查。Alpha有效性审计通过且 decision=blocked；未宣称发布门禁解除。

`pnpm --dir packages/sdk/assetlink/typescript install --frozen-lockfile --offline` 和 `pnpm --dir apps/web install --frozen-lockfile --offline`：成功，全部来自原有内容寻址缓存，无下载或锁变化。初次调用的 bundled pnpm.cmd 内部固定 Node24.19.0，Web engine 报告版本不满足；没有将此记为固定运行时验证通过。

已核对项目既有 Node24.20.0：`C:/YOKI/Codex/AssetLibrary-worktrees/V01-002/.runtime/toolchains/node-v24.20.0-win-x64/node.exe`。以它显式运行 bundled pnpm.mjs，并将该 Node 目录加入当前命令 PATH，`pnpm --dir apps/web exec node --version` 返回 v24.20.0。后续构建/浏览器命令使用此方式；不修改共享工具或旧工作区运行态。

## 待实施后的验证

实际入口和验收清单见 [implementation-plan.md](implementation-plan.md)。没有运行未改产品的全 Web 套件，也没有伪造缩略图、真实服务端联调、50万资产压力、移动/桌面视觉或拒权清理通过证据。先等待已提交冻结契约，再集中实现后验证。
