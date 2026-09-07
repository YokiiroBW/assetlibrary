# V01-024 — Web 产品交互完善与 NAS 更新

已完成并更新到 **https://192.168.31.210:5443**。刷新现有页面即可使用。首页、分类/库管理、真实目录面包屑与浏览历史、刷新/深链、多选/键盘、列表/网格、服务器排序筛选、限定范围搜索、条目定位、宽屏详情/手机抽屉及单库扫描管理均已落地；不以旧最小链路替代产品交互验收。操作见 [Web工作区说明](../../../docs/releases/WEB_WORKSPACE.md)。

## 复用与边界

沿用原React/SDK、会话/CSRF、请求取消/期限、虚拟化和模块化Core。分类由管理员明确指定，旧库general；LibraryStorage写分类/CAS/幂等，AssetIdentity拥有条目读查询/索引，GatewayAuth复用原权限投影。分别追加0019/0020/0021，不改历史迁移、不跨schema写、不新增语言/框架/数据库。三类分页查询共享有限lookahead逻辑；源指纹由原generator再生，生成代码仅头部注释变化。

保护真实NAS状态：先停止Core/PG，冷备四个专用卷和原配置，保留numeric owner/mode/ACL/xattr/链接并逐归档compare/hash；只替换经过校验的公共包，再initialize/start。没有重新configure/bootstrap/rotate-key。原2个库、管理员/授权密钥、图片已完成scan ID及195792项计数保留；文档仍未扫描，未写个人资产。最后两容器healthy、Core1654、只读根/资产、cap_drop ALL、no-new-privileges和PG不暴露端口均读回。

## 证据与交付

[集成与NAS证据](integration-evidence.json)、[构建证据](build-evidence.json)、[测试记录](tests.md)。V01-025/026已合入，85个不同自动检查通过：Web43、后端/仓库42；综合PG/E2E内部场景不重复计数。真实E2E使用原驱动和自有沙箱，覆盖真实登记/扫描、初次及重启后的浏览、mobile双击、权限、断连/恢复、CAS/幂等、源hash/mtime不变，资源回收verified。真实NAS桌面/手机登录、库深链/刷新/历史和扫描页通过，零管理/扫描写请求。

不可变包源 **61e6c0ef054f91bae6a6f39c692c175fdf151028**，tree **5fd3db626a7b08e2d3bca8c3bed702d50c9b8580**。本轮747实际编译新C#/SQL/Web；随后最终Linux Web单独重建、与747四产物hash一致，Core/setup只更新来源标签且RootFS层完全相同。application_payload明确记747，未声称重新编译未变代码或复用旧818业务。归档755318272 bytes，SHA256 **bd286a65f09c69b8059f3bb41bba0cd8a1c8f7dc09106e2c64134e6fd5a07b12**；两端与NAS部署目录SHA256SUMS通过。

## 运维和未扩范围

运行配置仍在 `/volume2/homes/agent/assetlibrary/V01-021/live`。回退需恢复本次冷备的整套四卷/配置与旧镜像，旧Host不接受新21态数据库。备份只在NAS私密运维目录，未复制资产原文件。保留可回退包/私密冷备和ignored日志，未处理此前已被审批拒绝的历史残留。

当前详情为文件信息，内容预览、下载原文件、通用/增量重扫、文件写入、Provider及其他管理模块仍按原路线独立。完整Alpha和所有未完成发布/写入门禁保持阻断。仓库主线与NAS主线在交接后同步，所有登记Git工作区须clean，不删除历史分支或修剪旧worktree指针。
