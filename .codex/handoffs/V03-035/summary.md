# V03-035 — Explorer页内翻页按钮

ready_for_review。基线88339997ad5e503461546eee14808da53e27da12，分支codex/v03-035-explorer-page-controls。代码6e2a4eeb12e020859f39a19c7f3731f706805a7c；原生禁用焦点修正aefa1f49fa9b13334648c47c82ef3b12ddc170bf；本收尾另提交测试和交接。root已接Source供View集成。

## 结果

原Surface浏览工具栏增加原生上一页114/下一页115，保留原100..105按钮；数组8项，Tab最大canvas+8共9项。复用当前原生字体、主题、高对比色、换行布局和辅助功能。预览仍使用独立7按钮，隐藏浏览翻页按钮；没有全局快捷键。

冻结Surface.h提供SetPageNavigation(previousEnabled,nextEnabled)、pageStep(context,delta)和控件ID，本任务只实现Surface.cpp，没有编辑头/契约/View/CMake/版本/依赖。Surface仅保存View给的布尔标志并发送-1或+1，不知道页token/历史/权限，不发请求或写偏好。

初始、SetPage（含同generation）、Clear和隐藏均退休标志；隐藏期间调用SetPageNavigation也不能启用。预览保留当前View标志但按钮隐藏/禁用，结束后才恢复仍有效标志；隐藏后重新显示不能复活旧标志。直接WM_COMMAND、原生BM_CLICK都受shown/preview/相应flag守卫，禁用不能触发pageStep。

SetPageNavigation沿用OwnedState/owner保活。EnableWindow和必要SetFocus后检查Alive、pageRevision及本地pageNavigationRevision，回调中Clear/发布新flags/销毁均不被旧调用覆盖。禁用前记录原按钮焦点；Win32可能在EnableWindow返回前把焦点清空，只有仍空或仍在原按钮时恢复canvas，不覆盖回调主动选择的其他焦点。

## 验证与范围

原MSVC19.44、Windows SDK10.0.26100.0、C++17、/W4 /WX /permissive- /analyze /utf-8 /MT静态GallerySurface和全部同源test EXE编译通过。受影响CTest12/12通过、0fail/0skip，2.25秒；按root要求没有重复不变geometry专项。

新PageNavigation夹具在既有SurfaceTests中使用真实隐藏自有HWND，覆盖原生可访问名/disabled状态、±1精确回调、9项Tab/禁用跳过/边界、加载/拒绝/同代页替换/隐藏/预览生命周期、1000/320/180DIP布局、EnableWindow重入修改flags或删除Surface、pageStep清页或删除Surface、禁用焦点按钮导致focusActivated删除Surface，最后owner/provider回基线。原外部UIA/MSAA、预览/缩放、布局、绘制、预算、退役和浏览恢复同步通过。

新增测试首先暴露真实禁用焦点恢复漏判，形成aefa1f4；原wide-summary固定1000物理像素不足以容纳新8按钮与摘要，仅该wide夹具改明确1000DIP并检查末项115，保留320/180DIP窄窗和原极窄reflow回调删除，未通过普遍扩大窗口绕过窄窗问题。

没有GUI/安装/注册/Host/IPC/NAS/真实资产操作。NativeAOT或产品打包不属于本控件任务。分页授权、前后页查询和跨页预览由View owner负责；实际Explorer和整包验收由root负责。原16MiB像素/后台请求预算不变，新增空间O(1)、当前页操作仍O(101)，无新依赖或技术债。

合并顺序：6e2a4ee→aefa1f4→本测试/交接提交；无需复制任何View或共享头。G4维持用户豁免，不宣布完整V0.3。
