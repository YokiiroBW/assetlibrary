# Web

V0.1 的只读浏览工作界面。当前仅通过浏览器适配的 AssetLink `control.request`
列出已授权资源库、浏览物理目录事实并搜索文件名/相对路径；不包含登录签发、权限写入、
上传、移动、改名或删除能力。

本地命令从仓库根目录执行：

```text
pnpm --dir apps/web install --frozen-lockfile
pnpm --dir apps/web run format:check
pnpm --dir apps/web run lint
pnpm --dir apps/web run typecheck
pnpm --dir apps/web run build
pnpm --dir apps/web run test:browser
```
