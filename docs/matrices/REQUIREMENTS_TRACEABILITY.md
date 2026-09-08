# 需求追踪矩阵

| ID | 需求 | 阶段 | 验证方式 | 状态 |
|---|---|---|---|---|
| CORE-001 | 物理目录权威，一库一物理根 | V0.1 | 路径重叠与真实文件操作测试 | confirmed |
| CORE-002 | 稳定资产ID与哈希身份 | V0.1 | 改名/移动/复制/替换测试 | confirmed |
| SCAN-001 | 首次扫描只读 | V0.1 | 扫描前后全目录哈希与mtime对比 | confirmed |
| SCAN-002 | SMB外部移动重指向 | V0.1 | 单文件与20k目录移动故障注入 | confirmed |
| TRANSFER-001 | 临时接收+分块+强校验 | V0.1 | 100GB断点传输 | confirmed |
| TRASH-001 | 统一垃圾桶与恢复 | V0.1 | 冲突恢复/保留期 | confirmed |
| SEARCH-001 | 名称/路径/元数据/全文搜索 | V0.1 | 50万数据P95 | confirmed |
| CLIENT-001 | Windows原生Explorer资产库入口 | V0.3预备 | V03-002真实Shell视图/隔离与ADR0018 | in_progress |
| CLIENT-002 | Android与平板原生只读浏览首版 | V0.3预备 | V03-003构建/协议/手机平板UI与ADR0017 | in_progress |
| SYNC-001 | Windows单向备份/移动归档 | V0.3 | 断网/崩溃/改名 | confirmed |
| SYNC-002 | Android相册与指定文件夹同步 | V0.3 | MediaStore/SAF/HyperOS实机 | confirmed |
| SHELL-001 | Windows Explorer原生外壳与右侧视图 | V0.5 | 崩溃隔离/8小时稳定 | confirmed |
| PRO-001 | 照片时间轴/地图/人物 | V0.7 | 权限/聚合/性能 | confirmed |
| PRO-002 | 音乐专辑与元数据 | V0.7 | 专辑候选/写回计划 | confirmed |
| PRO-003 | 角色库 | V0.7 | Markdown/相册/日记/AstrBot发布 | confirmed |
| PRO-004 | 表情包与未来输入法接口 | V0.7 | QQ/微信/TG分享与APIscope | confirmed |
| PRO-005 | 文本簿与网页剪藏 | V0.7 | Markdown/MHTML/截图/安全预览 | confirmed |
| SYNC-003 | 双向同步基线与删除待确认 | V0.8 | 双方修改/删除冲突/基线重建 | confirmed |
| EDIT-001 | 图片轻编辑保存新文件 | V0.8 | 原图哈希不变/衍生关系 | confirmed |
| EXT-001 | WebDAV默认关闭/只读 | V0.8 | 权限/路径/DELETE垃圾桶 | confirmed |
| PERF-001 | 50万资产与100GB文件 | V0.9 | 性能与故障门禁 | confirmed |
| RELEASE-001 | 数据丢失/权限泄露/静默覆盖为零 | V1.0 | 发布阻断门禁 | confirmed |
