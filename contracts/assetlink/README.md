# AssetLink Contract Draft

首版需定义：

- 握手、版本与能力协商；
- 统一错误码；
- 资源库/目录分页；
- 搜索游标；
- 事件游标与断线补齐；
- 预览Range与授权；
- 分块上传、transfer ID、幂等和断点；
- 长任务 task ID；
- 主备端点同一server ID验证。

最终协议可能使用Protobuf/JSON双编码或其他浏览器兼容方案，需M0 Spike决定。
