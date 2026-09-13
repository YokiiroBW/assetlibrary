# Windows Explorer preview-v1

状态：V03-022 / ADR-0022 批准。固定 preview1600 本机投影；thumbnail-v1 的规格不变。

- Endpoint：`AssetLibrary.ExplorerPreview.v1.<actual TokenUserSID>.<SessionId>`。
- 与 thumbnail-v1 相同的同 SID DACL、first-instance、拒绝远程、同会话、Identification SQOS、持有并验证服务端进程身份、取消与过期校验。
- 帧头 16 字节小端：magic:u32=`0x31504C41`（ALP1），version:u16=1，type:u16（请求1/响应2），payloadLength:u32，requestId:u32 非零并原样回显。
- 请求 payload 固定32字节：epoch GUID16 + node GUID16，均非零。无路径、URL、尺寸、编码器或凭证。
- 响应 payload 前缀固定56字节：status:u32；epoch GUID@4；node GUID@20；width:u32@36；height:u32@40；stride:u32@44；format:u32@48；pixelBytes:u32@52；pixels@56。
- status：Ready0、Loading1、Unavailable2、AccessDenied3、Expired4、InvalidResponse5、Busy6、Unsupported7。
- Ready：原样 epoch/node；width/height 各1..1600，乘积≤2,560,000；stride=width*4；format=1（top-down sRGB premultiplied BGRA）；pixelBytes=stride*height≤10,240,000；每个 B/G/R≤A；payload 长度精确为56+pixelBytes，最大10,240,056。
- 非Ready：仅56字节，epoch/node仍回显，其余尺寸/格式/长度全零，禁止残留像素。
- 每连接一问一答，不等待 EOF。关闭客户端即取消；首帧500ms、总预算20s（排队/网络/重试/解码均计入），最终取消排空最多500ms。Host 缩略图与大图合计4个图片客户槽，不为大图新增无限队列；共享现有网络图片许可。
- Core 查询仍为现有 `variant=preview`，encoded PNG≤12,582,912B；封闭规格 helper `--decode-preview`；原 `--decode-thumbnail` 始终≤512、PNG≤2MiB。
- 当前页授权节点映射和每次 HTTP 权限/新鲜度校验不可省略；认证失效撤销页，大图失败不回退读取原件。旧 epoch/ticket 的完成值必须丢弃。

兼容：旧 DLL/Host 不提供此端点时有限时返回 Unavailable，不放宽 ALG1。既有 thumbnail-v1 测试必须保持通过。
