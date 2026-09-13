# V03-030 缩放集成交接（进行中）

状态partial。V03-031几何与V03-032原生Surface已集成；原生Explorer右侧新增滚轮/按钮缩放、平移、适应/100%、键盘与清理。只复用原1600 PBGRA，未改Host/服务端/协议或持久偏好。

严格Shell build与13个CTest通过，root View测试证实zoom/reset没有新增预览/缩略图/目录请求，也不写偏好；子任务13个Gallery/Geometry CTest有实际证据，Setup23、发行锁16和包6测试通过。原NAS同机图片服务保持不变。

版本准备0.3.0-preview.8，发行锁只更新Core到AssetLink的project版本范围，没有新增依赖。打包、真实Core/Explorer界面及临时资源清理尚待本轮执行，不宣称已安装/实机通过；G4不做。
