# Windows 用户会话控制适配

Settings 只依赖此小型本机适配库；没有 Core/HTTP/数据库引用。协议位于
`contracts/windows-shell/user-session-v1.md`。控制帧最多16KiB，客户端验证服务端TokenUser与session，
使用Identification SQOS。服务器复用同一实际TokenUser DACL、首实例预占与拒绝远程边界。

默认不记住登录。只有明确勾选后，Host才把登录输入交给CurrentUser DPAPI；
`connection.json`只存来源/证书指纹/账号/偏好，`remembered.bin`是用户绑定的加密blob。
目录及文件owner/DACL只允许当前TokenUser；发现重解析、其他owner/读权限时拒绝使用。
不记录口令、Cookie、CSRF或服务端错误正文，不向设置窗口暴露Core会话令牌。

生产Host仅在同Windows登录会话内共享内存Cookie和快照；退出删除记住凭据。
有记住凭据时Host启动尝试登录一次，拒绝后删除，不循环重试。
