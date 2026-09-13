# V03-031 交接摘要

状态：ready_for_review。实现提交 `31d81c7d4eb8837865ead1fe47088f2a03b0a3cf`，分支 `codex/v03-031-explorer-preview-geometry`。本任务只新增PreviewViewport.cpp及其数学测试，冻结头未改；root共享CMake提交1ba6314按授权cherry-pick为40a76b8。

## 行为

PreviewViewport以图片归一化中心、缩放及Fit模式保存常量大小状态；只进行O(1)数值计算，无位图/句柄/窗口分配、I/O、IPC、网络、解码或持久化。Reset保留viewport，清图或接受1..1600尺寸后回默认Fit；Resize保留图片，Fit重算，显式模式保留归一化中心并按可见区域夹取。

Fit不自动放大小图；显式比例10%..400%，ActualSize保持当前中心，Fit回中央。ZoomAt保持给定设备像素锚点，边界优先夹取；Pan正delta令图片随手移动，较小轴始终居中。无Ready时Scale/FitScale均0、Placement四0、动作false。临时无效RECT不会抹掉显式中心，负原点但正宽高有效。

Root确认的细节：Fit低于10%时ZoomOut无操作，避免缩小反而跳大；ZoomIn首步至少10%。multiplier1不改变模式，NaN/Inf/非正倍率及非有限pan原子无操作，有限极值安全饱和。ActualSize/Fit即使几何相同但模式改变也返回true，因为状态摘要会变化。RECT先转double再求差，避免LONG极值减法溢出；pan先限制delta再除，避免大有限值溢出。

## 验证

真实MSVC19.44.35228.0、Windows SDK10.0.26100.0、x64 Release完成GalleryPreviewViewportTests构建；生成项目中核实/W4 /WX /permissive- /analyze /utf-8 /MT及C++17。CTest gallery_preview_viewport为1/1通过，包含7个定向场景、256个图片/视口组合及2000次重复饱和操作。未启动GUI或生产pipe，没有用替身实现链接。

测试独立检查已知目标矩形、锚点对应源像素、四方向边界、100%、显式中心与临时空态恢复、DPI设备像素、低于10%的Fit、LONG/UINT极值、NaN/Inf及无图能力。属性矩阵检查居中或无空白覆盖，未用实现公式复制作为唯一预期。源码最终diff已审查，仓库verify_repository通过。已将31d81c7提供给Surface owner进行真实控件集成。

## 边界与交接

无新依赖/语言/框架、版本/协议/权限/资产变更；复用ADR0024和root冻结头，数值类不接管Surface生命周期。缩放只是绘制目标矩形，不创造放大位图，不重新请求1600图片。50万资产不增加单图计算或存储成本。

基线4d1a9c5比较的changed_files包含root提供的3个CMake接线文件，注明它们来自1ba6314，不是本owner另改。合并顺序：root共享接线 → 31d81c7几何 → Surface集成 → root实际HWND/Core/Explorer验收。完整交互/捕获/可访问性/绘制资源由Surface和root验证，本纯数值测试不替代GUI证据；G4豁免未安排。没有新增技术债或本任务残留运行资源。
