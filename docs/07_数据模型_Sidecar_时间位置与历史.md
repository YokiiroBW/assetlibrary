# 07. 数据模型、Sidecar、时间、位置与历史

## 7.1 核心实体

建议领域实体至少包括：

```text
User / Device / Credential
StorageSource
LibraryCategory / LibraryTemplate / LibraryInstance
Folder / Asset / AssetContentRevision
Sidecar / AssetRelation / CompositeAsset
Tag / Rating / Color / CustomField
Collection / SavedView
SourceRecord / LicenseRecord
TimeFact / LocationFact / PersonCluster
Annotation
TransferSession / SyncTask / SyncBaseline / Conflict
OperationPlan / OperationItem / TrashEntry
Task / Notification / AuditEvent
Provider / PreviewArtifact / AnalysisResult
```

## 7.2 稳定身份与路径

资产定位采用：

```text
storage_source_id
library_id
relative_path
stable_asset_id
content_hash
```

绝对路径是部署映射，不是全局身份。路径变化保留历史。物理复制创建新资产 ID；已确认移动保留原资产 ID。

## 7.3 内容修订

同一路径被外部软件修改时保留资产 ID，但产生新的 content revision：旧哈希进入历史，预览/OCR/查重派生结果失效。疑似完全替换时进入待确认，不盲目继承全部关系。

## 7.4 Sidecar

`.assetmeta/` 只在用户确实产生可携带人工数据时创建，不在首次扫描时铺满目录。

可携带：

- 标题、说明、标签、评分、颜色；
- 来源与授权摘要；
- 人工时间、位置、保护；
- 轻量标注；
- 文件夹封面和说明；
- 已确认关系摘要。

个人播放进度、搜索历史、同步基线、任务和权限不写 Sidecar。

标准 XMP、LRC、CUE、MTL 等按格式 Provider 读取和关联。Sidecar 冲突必须显式处理。

## 7.5 时间模型

同一资产可以拥有拍摄、录制、发行、出版、日记、剪藏、创建、修改、入库和最近使用等多种时间。每条时间记录类型、来源、时区、精度、可信度、原值、校正值和锁定状态。

时间推断可来自目录或文件名，但必须标明推断。支持仅年份、年月或日期。文件修改时间不能覆盖拍摄、发行、日记等主时间。

## 7.6 位置模型

分层保存：原始 GPS、标准坐标、Provider 显示坐标、逆地理结果和用户确认值。拍摄位置与画面地点分开；现实地点与角色虚构地点分开。

默认本地读取 GPS，逆地理按需，自动识别默认到城市/区县。支持 GPX 时间匹配和分享时移除/模糊位置。

## 7.7 人物与角色

照片人物识别在本地按资源库隔离，先聚类后由用户命名。跨库人物合并必须显式确认。人物特征严格继承权限。

真实人物与角色库中的虚构角色严格分开。动漫角色只通过明确 `character_id`、系统见证、元数据或用户确认关联。

## 7.8 轻量标注

统一支持整资产备注、图片区域、PDF页码/区域/文字、视频时间点/片段、3D视角和文本锚点。标注不改原文件，记录创建时哈希；内容变化后可能进入待复核。

## 7.9 历史和审计

分为：

- 操作批次；
- 单资产历史；
- 系统审计；
- 调试日志。

外部变化标记为“文件系统观察”，不猜具体程序或用户。关键文件操作和权限默认保留 365 天，普通任务 90 天，通知 60 天，调试日志 14 天。
