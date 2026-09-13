# V03-023 原生大图 Surface 交接

状态：ready_for_review。分支 `codex/v03-023-native-large-preview-surface`，工作树 `C:/YOKI/Codex/AssetLibrary-worktrees/AssetLibrary-worktrees/V03-023`，基线c10eddf。提交顺序：d116e7b（冻结API）→f851555（实现/组件测试）→4593274（既有harness/外部UIA）→cb0f154（修饰键边界）。

## 完成行为与接口

BeginPreview(index,serial,previousEnabled,nextEnabled)进入当前授权页的普通文件预览；SetPreview(index,serial,PBGRA,statusText)匹配当前请求并替换像素/文案；EndPreview返回；Previewing只读。serial独立于页generation，必须由View每次打开/切换递增，View仍另核ticket、page generation、epoch/node。prev/next和返回只调用owner的previewStep(-1/+1)/previewClose，不在Surface重新选择授权范围或访问I/O。

预览使用同一原生canvas和GDI/AlphaBlend，保持比例fit，可留白；文件名完整可访问，绘制标题允许省略显示；原生返回/上一张/下一张按钮ID107/108/109，状态沿用原生摘要。预览期间SetStatusText只保存图库摘要，SetPreview的状态文案负责当前显示。

普通文件Space/Enter/双击走activateItem；目录/分页Space不导航，原Enter/双击仍保留。Ctrl+Space切换选择，Ctrl+Enter不降级成打开大图；Ctrl/Alt修饰Esc/左右不拦截Explorer快捷键。预览内普通Esc/左右、按钮均可用，Tab跳过禁用前后按钮并在边界交宿主。

## 状态、预算与无障碍

进入/切换立即释放全部缩略图与旧大图，VisibleFileItems为空；SetThumbnail不能在预览中重新发布。底层page/selection/focus/scroll仍保留，SelectedItems/FocusedItem返回原图库状态，切图不改变选择。关闭恢复图库/列表与浏览位置；隐藏、Clear/权限失效、SetPage和Destroy清预览，迟到index/serial或关闭后的结果拒绝。

固定接受1..1600尺寸、stride=width×4、最大10,240,000字节PBGRA；仍以capacity第二道检查16MiB持久总预算。只有一个shared preview buffer，无持久GDI位图副本；绘制临时DIB最多10,240,000B，绘完释放。该瞬时绘制缓冲不混入持久限额；Root负责跨请求完成队列和Surface共享字节租约，Shell/Host负责像素可信验证，Surface不解码/联网/哈希。

图库和预览复用同一AccessibleModel类型、现有MSAA/native UIA实现与固定512退休调度器。预览模型只含当前1项；WM_GETOBJECT只返回当前预览Pane/Image，隐藏图库的旧provider先退役；返回及切换同样使旧对象拒绝current查询。Image不暴露图库Selection/Scroll/Invoke模式，图形名称和状态可读。外部UIA实测List30→Pane+Image1且ListItem0→Next名称变化/旧provider失效→Back恢复List与原选择，并回收owner/providers/dispatcher。

## 偏好与架构边界

preferencesChanged仅实际模式/密度变化后通知，no-op和clamp后相同值不通知；读取恢复屏蔽/注册表16字节持久化由root承担。复用现有字体、主题、布局、原生按钮、GDI和只读snapshot投影，不新增语言、框架、依赖、端点或业务规则；没有改View/Requests/共享contract/版本/注册表/生产注册。全库50万下Surface仍只处理≤101当前页及1预览，所有循环有界。

## 验证和剩余范围

严格MSVC /W4 /WX /analyze构建通过；组件用合成图验证1600×1600最大值、1600×1/1×1600、保比例、GDI回收、像素/容量拒绝、取消/隐藏/权限Clear/迟到、状态恢复、回调销毁和快捷键。现有GalleryHarness --show已可用文件Space/Enter/双击检查同源合成1600预览及按钮，不伪装真实Core或目录导航。详见tests.md。

实际Core→Host/WIC→Explorer、大图授权/取消/偏好存储与发布验收由协调root/V03-022完成。本任务未操作GUI/生产注册/真实NAS；没有原图打开、缩放/平移、预取或G4耐久声明。
