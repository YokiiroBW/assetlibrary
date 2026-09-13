# V03-033 — Explorer页内与跨页浏览交付

已交付并安装preview.9，源1003157，分支codex/v03-033-explorer-page-integration，基线0e13f61。V03-034状态、V03-035控件、V03-036真实图片夹具已统一集成；并行代理达到用量限制后root接管V03-034未提交代码，完成独立审查发现的Escape与较新预览意图回归。

上一页/下一页与NextPage主激活在同IShellView切换，保留当前物理地址栏；大图跨一页选择首/末普通文件，Esc返回当前文件。固定64个opaque位置，不保存旧页名称/像素；前页重查、forward token更新、错误F5、权限/epoch/hide/destroy清理与generation/ticket边界复用现有链路。Surface只增加原生按钮/键盘/展示回调，没有HTTP、解码或业务副本。最大当前页101、16MiB图片、Host限额均不变；50万资产仍O(101)每步/O(64)位置空间。

实际Core120张图片分100/20页，101→100→101、125%跨页复位、102透明图、Esc选中、前页重查、宽窄和注销清空通过。540安装文件hash匹配，新Explorer进程35396加载.9；258源hash/mtime不变，临时Core/PG/6角色/runtime、测试Host/connection与两个自有窗全清，原Explorer6212/creation及Gallery/200保持。

安装包155529819B，SHA256 574f9c2d307886225f0602c117aa1156253a2850716b21ef9844c054eaaf3d8d；未签名，旧占用.1不强删。代码已通过严格构建、13Shell/12图库/23Setup/16锁/6包/8CLI和13Python/9C#夹具验证。详见tests.md和windows-preview9-delivery。

新增契约仅ADR-0025及内部Surface头；snapshot/thumbnail/preview/PIDL/AssetLink字节不变，根锁只变项目版本，未增加第三方依赖或修改NAS/原件。建议合并顺序V03-036、035、034、033。保留无限滚动、超宽缩略图预算优化、原件/同步/签名及Android实机；G4豁免，未宣布完整V0.3。
