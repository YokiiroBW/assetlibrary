# 同机图片容器 IPC v1

ADR-0023 / V03-026批准。仅受信Core与同部署图片监督器使用；不是公开网络API。

- 固定pathname Unix stream socket `/run/assetlibrary-image/decoder.sock`，专用卷，Core只读挂载；目录root:1654模式0710（或同等明确更受限的遍历权限），socket UID1654模式0600。监督器SO_PEERCRED只接受UID1654；Core验证服务peer UID0。decoder UID/GID1655且清补充组，无权进入该目录。
- 容器监督器为PID1、cap_drop ALL后仅SETUID/SETGID/KILL、NNP、network none、私有PID/IPC/mount、readonly root、memory512MiB。不挂资产/Core私密卷/PG/Docker socket。部署工具核验镜像/配置/标签与实际限额，不以Ready帧当容器配置证明。
- 每连接一张图。复用ImageWorkerProtocol24字节LE：magic0x31495041，status、profile、length、width、height各u32。未知字段/超长/额外字节均拒绝。
- 服务确认有容量、启动新decoder且其隔离Ready通过后，发送status1/profile0/length0/width0/height0；忙/熔断/不可用发送status7且无body。
- Core发送status2/profile0缩略图或1大图、length1..32MiB、width/height0，接着精确length字节；发送完保持连接开启，不半关闭。监督器向decoder转送相同帧与源字节后关闭其stdin以满足旧EOF语义，并另读Core连接1字节监控断开或多余输入；任何额外字节、EOF均取消在途工作。
- 成功status3/相同profile、宽高与length匹配PNG，512/2MiB或1600/12MiB，父端继续验证PNG签名/尺寸/像素上限与子进程实际成功退出。失败status4/5/6/7无body，沿用invalid/unsupported/limit/unavailable。禁止输出路径/凭据/源metadata。
- 监督器单decoder，不无限排队；Core适配器串行等待包含在原8秒decoder总预算与15秒应用预算内。服务端8秒总期限，stderr最多4096B，固定缓冲流式收发；断开取消和实际wait/reap，未清理完成不归还容量。
- 只有明确受控配置选择该后端；本地进程worker与socket互斥。旧seccomp入口不变，未知平台/缺失隔离/peer错误失败关闭。无TLS网络入口、无远端地址、无SCM_RIGHTS。
- 连续基础设施/清理异常最多3次自动恢复，持久计数不含媒体；熔断保持不可用直到受控重启/维修，正常成功恢复可清连续失败计数。明确区分不支持图片与基础设施故障，正常坏文件不触发重启风暴。
