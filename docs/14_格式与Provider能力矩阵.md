# 14. 格式与 Provider 能力矩阵

完整表格见：

- `docs/matrices/FORMAT_SUPPORT_MATRIX.md`
- `docs/matrices/FORMAT_SUPPORT_MATRIX.csv`

## 14.1 能力等级

- L0：基础管理；
- L1：缩略图/封面；
- L2：内置预览；
- L3：轻量操作；
- L4：正式导出；
- L5：外部专业软件。

所有真实文件至少 L0。Provider 未安装或失败时降级，不得让文件消失。

## 14.2 核心内置

- 通用文件识别、签名、哈希；
- 常见静态图片和动图；
- PDF；
- Markdown、文本、代码和配置；
- 常见音视频基础信息和播放；
- ZIP等归档目录；
- 未知格式 L0。

## 14.3 官方内置增强 Provider

- RAW；
- PSD/PSB；
- Office只读预览；
- EXR/HDR；
- AI/EPS条件预览；
- 贴图格式；
- 电子书与漫画；
- 通用3D；
- 字体预览；
- OCR；
- 图片同源与质量；
- 音乐元数据；
- 电子书和网络小说元数据；
- 地图与逆地理；
- 安全扫描；
- AI中转站；
- Xiaomi动态照片增强。

## 14.4 外部打开优先

DWG/DXF、MAX/C4D/SKP和复杂封闭格式首期只提供基础索引、可用伴随预览和专业软件打开。
