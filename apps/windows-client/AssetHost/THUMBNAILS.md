# 派生缩略图适配

V03-020复用现有Core Cookie/Origin/TLS会话和`image-preview-v1.md`的thumbnail GET，
通过独立`AssetLibrary.ExplorerThumbnail.v1.<TokenUserSID>.<session>`返回固定PBGRA。
快照v1及原Proof CLI不变；缩略图服务只加入生产`--user-session`。
具体frame、错误与取消语义以`contracts/windows-shell/thumbnail-v1.md`为准。

## 归属与预算

SnapshotNodeRegistry只在现有8192节点额度内保留服务器投影的双UUID映射。
请求只有当前epoch/node；旧节点、目录和reparse不会触发图片HTTP，文件名不决定格式或身份。
没有完成图片缓存或磁盘图片落地，完成Task及时脱离Host工作集合。

HTTP总并发2，图片传输最多占1；每个会话的图片处理也最多1，另外最多3项等待。
所有图片任务通过Task.Run离开调用者session临界区，再执行20秒总限内的排队/HTTP/读取/解码。
JSON保留8秒/1MiB，PNG最多2MiB；429最多两次重试。
401/403复用身份撤销，404/409/415/422/503只返回无像素的诚实降级。
客户端断开会取消HTTP和辅助进程，初始帧500ms、成功写后最多500ms有界排空。

## 系统解码辅助模式

正常Host只做有界PNG签名、IHDR、尺寸/像素、8bit、CRC及静态/APNG检查。
`AssetLibrary.Host.exe --decode-thumbnail`只接匿名标准管道字节；没有路径/URL/profile参数。
父进程以显式handle-list和job-list原子创建子进程；仅继承3个标准管道句柄，环境重建为
DOTNET_EnableDiagnostics/DOTNET_ROOT/SystemRoot/WINDIR，不继承凭据环境。
Job限制1进程、128MiB进程及job内存、kill-on-close；解码3秒且服从总20秒。
子进程先验证实际Job限额，随后直接从System32 windowscodecs.dll取得固定PNG decoder和format converter，
不探测第三方注册codec。单帧/尺寸复验后输出top-down 32bpp PBGRA；父进程再次校验输出尺寸、严格长度、
premultiplied通道和退出码，完成前等待自有子进程及管道回收。

这提供崩溃/资源隔离，不能解释为同用户强安全主体隔离，也不替代NAS原件的Provider隔离。

## 验证

Windows solution命令仍见上层README；新增`Thumbnail*.cs`覆盖独立13条frame向量、真实WIC/Job、
HTTP早期拒权/降级/限制、导航容量、真实pipe断开/部分帧、旧epoch与晚到解码。
`ThumbnailLiveTests`仅使用显式`ASSETLIBRARY_NATIVE_TEST_PROFILE`连接隔离Core样例，
验证派生PNG→受限WIC→随机`.test.<GUID>`管道；不触碰生产端点/GUI/注册/真实NAS。
该测试要求夹具的`图片样例`目录真实可预览，不能把503当成通过。
