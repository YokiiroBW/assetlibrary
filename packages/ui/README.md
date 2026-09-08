# UI Semantics

保存跨端统一交互、状态、图标、快捷键与视觉token；不要求各端像素完全相同。

## 原生首版主题

`workspace-theme.json` 是生成的只读资源，包含 light/dark 的同名语义颜色。
权威值仍位于现有 Web `apps/web/src/styles/base.css` 与 `responsive.css`，
以 `python -I -B scripts/export_native_theme.py` 导出，`--check` 检测漂移。
Android 将此文件链接到应用资源并读取，不手工拷贝颜色常量。
Windows 首版按 ADR-0018 使用 Explorer 原生 DefView，其控件颜色由系统主题管理；
未来自定义原生视图再消费同一语义资源，当前不复制 Web 外壳到 Explorer。
用户系统的高对比度和辅助功能设置优先于产品颜色；平台控件与布局仍保持原生。

视觉信息层级以 `docs/12_UI交互与视觉规范.md`、`docs/20_视觉稿索引与说明.md`
和 `assets/visuals` 为准。稿件中的图片预览、统计和写入动作只有在对应服务端能力
交付后才呈现；当前首版边界见 ADR-0017。
