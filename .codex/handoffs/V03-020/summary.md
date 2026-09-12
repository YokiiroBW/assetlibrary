# V03-020 — 派生缩略图Host链路

状态ready_for_review。分支 `codex/v03-020-gallery-thumbnail-host`；基线fa1c8f9。
代码依次为 `09effe7`（完整链路）、`1bf317b`（移出caller临界区）、`5e5ace2`（及时释放完成Task像素）、`4bbaa41`（root批准的旧到期测试完成边界修正）。Root的独立向量7fae7a0已先同步，不需要重复合并。

## 最终行为

- 快照v1和Proof入口不变。SnapshotNodeRegistry在现有8192-node额度中保留服务器投影的普通文件双UUID；旧epoch、目录、reparse或未知节点不触发图片HTTP。没有根据文件名推断身份或格式。
- 生产UserSession新增独立thumbnail v1 pipe，4个受实际TokenUser/session约束的实例，复用未修改的LocalPipe安全。一个请求连接保持到结果，初始帧500ms、总操作20s、写后最多500ms排空；断开/多余输入取消操作。返回固定56字节前缀+最多1MiB top-down PBGRA，无压缩图/路径/账号/服务端ID。
- 同一HttpClient Cookie/Origin/pin/no-redirect处理固定派生thumbnail GET；JSON保留8s/1MiB，图片20s/2MiB，HTTP总2许可、图片最多占1。图片整条流水也只运行1项且最多4个请求，不影响目录保留容量；429最多两次重试。
- 401/403复用epoch/session撤销；图片404未部署、409/415/422/503均诚实无像素降级，不能误调用既有JSON的404整账号清除。Late decode与write publication都受epoch取消约束。
- 图像工作通过Task.Run离开coordinator临界区；完成Task立即移出工作集合，不长期保留其PBGRA结果。无Host完成图片或磁盘缓存，无视口表。
- 常驻Host只验证有限PNG容器：签名/IHDR/8bit/静态/APNG/尺寸/CRC/严格长度。固定System32 Windowscodecs PNG/WIC及format converter仅运行在同exe的--decode-thumbnail短时模式；不发现第三方codec。
- 子进程只通过匿名标准管道接收/返回字节；显式handle-list仅3句柄与job-list在CreateProcess原子生效。环境重建仅runtime/system变量；不继承凭据，不加载profile。Job kill-on-close/1进程/128MiB，3s解码并服从总20s，子进程先校验实际Job限额。父进程验证尺寸/长度/预乘alpha及退出码，失败/取消后等待自有进程和管道收回。

## 验证

最终Windows非NativeLive 101/101通过，独立真实Core缩略图测试1/1通过：隔离Core真实派生landscape.png、transparent.png → 同Cookie HTTP → WIC Job → 随机.test后缀真实pipe → 合法PBGRA。只退出自己的测试登录，未停止root服务。原件及其NAS隔离由服务端负责；这里不读取或修改原始文件。

完整Windows solution locked restore、format verify、Release build零警告通过；仓库验证及6项目/31包依赖审计通过，无新增依赖或锁漂移。生产自包含AssetLibrary.Host.exe在本worktree的 `.runtime/sandbox-storage/V03-020/publish-host`，locked publish及真实生产WinExe WIC Job smoke通过。

## 范围与后续

接口字段/预算不变；无数据库迁移、服务端修改、资产写入、GUI、安装注册或生产pipe操作。独立测试pipe均带随机后缀，未与V03-021的正式端点native测试竞争。

root审核发现async链可以同步运行，1bf317b以同步HTTP/阻塞decoder临界区测试固定该边界；该测试在旧直调实现下会阻塞状态/page调用。旧SessionExpiryTests曾只等AccessDenied就读remembered.bin；该状态刻意先清展示，凭据删除完成通过RememberLogin=false表示。4bbaa41等待这个明确边界，仍严格检查AccessDenied/无数据/无blob；删除真正失败变Unavailable仍会失败，没有改权限或吞IO。

Explorer自绘视图、可见范围、UIA与整包实际图片由root/V03-019/021集成；本任务没有宣告完整V0.3、NAS预览可用或G4实测完成。
