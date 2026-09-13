# Windows 原生资源管理器图库与大图浏览版

2026-09-13，`0.3.0-preview.7`，Windows 11 x64，已安装。日常入口仍在原生资源管理器，图片与大图都在右侧内容区，连接设置是辅助窗口。

## 安装包

[下载本机安装包](../../.runtime/releases/windows/AssetLibrary-0.3.0-preview.7-win-x64.zip)，155495031字节（约148MiB）。源码：`addcaf205aa88af19d858eeae9770bb2f5c9b1ef`。

SHA256：`4c1c328c8b37bbb9e4f0ad38658619bca267dd3a25892d7ac9340e5054ad7702`。

完整解压，保留payload和manifest.json，运行AssetLibrary.Setup.exe。连接设置填写自己的HTTPS服务地址、账号及可信证书指纹，再从Explorer左侧“资产库”进入。记住登录默认关闭；安装包尚未签名。

## 使用

- 图库保留图片比例的行对齐瀑布流，可切列表、调整图片大小。
- 选中文件后按空格、Enter或双击，在右侧打开大图；左右键或“上一张/下一张”浏览当前页文件。
- Esc或“返回”退出大图，恢复原选中项、图库位置；预览不增加导航历史。
- 大图最长边1600，适应窗口，支持JPG/PNG/WebP、照片方向修正和透明图片。坏文件或暂不支持的格式显示明确提示。
- 图库/列表与图片密度会跨目录、分页和重新打开窗口保存；已开的其他窗口维持当前显示。

## 实机验收与边界

最终包在新的Explorer进程中连接真实隔离Core/PostgreSQL/HTTPS，验证了上述图片、键盘/按钮、宽窄布局、偏好恢复与退出登录清图。542个安装文件大小/哈希全部匹配；148个合成源文件hash/mtime不变。测试服务、账号会话、连接配置及专用窗口已清理，安装和本次选择的图库大小保留；原Explorer未重启。

2026-09-13后续：原NAS已按ADR-0023启用同机图片服务，实际Core请求和页面显示通过，见[NAS图片交付](NAS_IMAGE_PREVIEW.md)。上述preview.7 Explorer实机记录仍对应当时的本机隔离Core；本次NAS上线没有冒称重新执行全部Explorer门禁。不支持图片服务的其他连接仍显示不可用。

- 当前页最多100个普通项及分页入口；切图仅遍历当前页普通文件。
- 本版只有适应窗口；缩放/平移、原件编辑、时间轴、全库连续滚动、上传下载/同步另行开发。
- Ctrl+Space保留选择切换；Ctrl+Enter专业软件打开原件尚未实现，不降级成普通预览。
- 偏好只保存模式与密度，不保存路径、选择或登录信息；升级/卸载保留此用户显示记录。
- Windows11底部可能仍显示旧DefView计数，以图库工具栏摘要为准。
- 旧preview.1占用目录仍待清理，未强删DLL；认证表单和系统卸载UI未自动操作。
- G1/G2/G3沿用实测；G4按用户要求豁免且未测试。完整V0.3、签名和Android真机门禁未因此关闭。

[完整交付证据](../../.codex/handoffs/V03-022/windows-preview7-delivery/README.md) · [验收矩阵](../matrices/WINDOWS_GALLERY_ACCEPTANCE.csv) · [上一版preview.6](WINDOWS_EXPLORER_PREVIEW_6.md)
