# 06. AssetLink、WebDAV、Developer API 与 MCP

## 6.1 AssetLink 官方客户端协议

官方 Windows、Android、Web 与浏览器扩展使用 AssetLink，不直接依赖 WebDAV 或公开 API。

协议建立在 HTTPS 之上，分为：

- 控制：认证、导航、搜索、元数据、计划与状态；
- 事件：任务、文件变化、权限、通知和连接游标；
- 数据：缩略图、Range、媒体流、分块上传、断点续传和哈希。

协议必须支持：版本协商、capability negotiation、request ID、幂等键、游标、超时、取消、结构化错误和长任务 task ID。

Web 可使用 AssetLink 的浏览器适配编码；Windows/Android 可使用更紧凑的编码，但字段语义与 SDK 必须同源。

## 6.2 Windows 本机 IPC

`AssetShell.dll` 只通过本机受限 IPC 与 AssetHost 交互。AssetHost 承担登录、网络、缓存、传输、外部软件和协议连接。Shell DLL 不持有密码，不直接访问 PostgreSQL、WebDAV或远端文件。

## 6.3 WebDAV

- 新建资源库默认关闭；
- 显式启用后默认只读；
- 读写需再次开启；
- 只映射具体资源库和真实物理目录；
- 不映射聚合视图、收藏、人物、时间轴、角色主页等；
- PUT 临时接收 + 校验；
- COPY/MOVE 真实操作；
- DELETE 进入统一垃圾桶；
- `.assetmeta` 默认隐藏；
- 使用独立应用密码或受限令牌。

WebDAV 只提供文件兼容层，不承载完整资产标签、AI、查重和专业视图。

## 6.4 Developer API

默认关闭，仅开发者模式启用。提供版本化 HTTP API、OpenAPI 文档和独立访问令牌。

基础 scope：

```text
libraries.read
folders.read
assets.search
assets.metadata.read
previews.read
originals.read
inbox.upload
metadata.write
favorites.write
annotations.write
tasks.read
status.read
plans.create
plans.execute
```

移动、复制、改名采用“创建计划 → 返回预检 → 显式执行”。普通 API 不开放永久删除、任意 SQL、系统命令、静默覆盖和资源库根修改。

## 6.5 MCP 与插件

MCP 作为 Agent 友好适配层，建立在业务服务或 Developer API 上，不直连数据库和 NAS。

预留工具：

```text
list_libraries
browse_folder
search_assets
get_asset_metadata
get_asset_preview
get_system_status
get_library_status
get_tasks
upload_to_inbox
add_asset_tags
add_to_favorite
create_annotation
create_diary_draft
create_move_plan
create_copy_plan
create_rename_plan
execute_plan
get_plan_status
```

AstrBot、Hermes、Codex 可以选择官方插件或 MCP：

- AstrBot：读取发布人设、角色头像、表情、提交日记草稿；
- Hermes：查询健康、任务、存储和创建受控计划；
- Codex：浏览工程资料、读取文档、上传待整理和创建计划。

Agent 不得直接永久删除、绕过保护或静默覆盖。

## 6.6 Codex 开发深度链接

Codex 深度链接用于创建/打开工作线程和工作区；关键结果仍必须落盘到标准交接文件、commit 和测试。`scripts/codex-start.*` 和 `scripts/codex-new-task.py` 负责生成链接。
