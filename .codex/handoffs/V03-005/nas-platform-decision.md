# NAS 图片引擎历史平台评估（由 ADR-0023 接续）

2026-09-13：用户要求所有运行时处理留在同一台 NAS、同一部署包内；下文“外置计算主机或暂缓”是当时建议，已不再是当前实施方向。[ADR-0023](../../../docs/adr/ADR-0023_同NAS图片处理容器.md) 的同机图片容器方案已由 [V03-026](../V03-026/summary.md) 完成实测与原NAS冷备上线：同一Compose、不同解码身份和本地socket。历史seccomp失败证据仍有效；新路径的验收独立记录，没有将旧失败改写为通过。

## 已核实的结果

同提交a12b0d1的NAS离线包已经实际构建、传输、逐文件验hash并载入NAS，但没有替换线上服务。图片Worker在开发Linux6.14的同一镜像内能进入Ready；目标NAS5.10.55+在读取图片前返回Unavailable，第一项用例失败，后续19项未执行，原样例和自有容器清理均验证通过。

NAS宿主机的只读能力查询：PR_GET_SECCOMP返回-1/EINVAL22，seccomp GET_ACTION_AVAIL返回-1/ENOSYS38，/proc/self/status没有Seccomp项，Docker只报告AppArmor安全模块。独立临时容器中cap0、NNP和各rlimit能设置，但追加TSYNC过滤器仍ENOSYS。没有修改宿主机策略。原始结果见.runtime/nas-image-evidence-a12b0d1/kernel-capability.json、worker-first-failure.json。

这证明当前NAS没有提供本实现所需的seccomp接口；不能把开发机通过或只读容器配置当作NAS图片安全验收通过。当前图片功能保持关闭/受控503。

## 为什么不直接替换为AppArmor

AppArmor已启用，但“AppArmor+NPROC=1”在现有同UID、同PID命名空间的Core/Worker结构下并非已验证的等价替代。按上游5.10.55代码分析，调度优先级、部分跨进程资源限制和撤销父死亡信号仍有未封闭路径；厂商补丁需另核实。这是对上游实现的推断，不是声称已在NAS重现了每项攻击。

- [AppArmor钩子](https://raw.githubusercontent.com/gregkh/linux/v5.10.55/security/apparmor/lsm.c)、[资源限制](https://raw.githubusercontent.com/torvalds/linux/v5.10/security/apparmor/resource.c)。
- [父死亡信号接口](https://man7.org/linux/man-pages/man2/PR_SET_PDEATHSIG.2const.html)允许进程清除自己的设置；现有seccomp规则只允许读取相关策略，不允许清除。
- [NNP下的profile转换](https://raw.githubusercontent.com/torvalds/linux/v5.10/security/apparmor/domain.c)还需实际验证，不能用unconfined或继承原profile回退来消除失败。

本轮没有安装AppArmor策略、改NAS内核或降低Worker的隔离检查。重新设计本地身份/命名空间/独立监督也需要新的ADR和平台验证，不能作为一个未经验证的开关修复。

## 推荐的可实施方向

仅把图片解码部署到现有、已验证seccomp的dev-230（192.168.31.230）；NAS仍保留Core、数据库、目录、权限与一致性判断，客户端入口不变。NAS经认证加密连接发送待预览的有界图片字节，Linux主机用现有独立Worker处理并返回派生PNG；原文件不迁移，不发送原始路径、用户会话或数据库凭证。图片内容会到达这台受信主机，设计目标为不持久保存输入。

此方向需要批准一个新的内部TLS计算入口及ADR，复用现有隔离Worker、核心端口和PNG验证。必须补服务身份认证、15秒总预算、限并发、取消/超时后的实际回收、断网降级、输出验证与临时数据清理；未通过前不启用。Linux主机不可用时保留基本目录/文件信息浏览，图片显示明确不可用。它不关闭Explorer、通用Provider、资产写入或完整版本门禁。

另一选择是暂缓NAS图片，继续完成Windows/Android基础客户端与Explorer入口，等具备受支持的Linux环境后再启用。这会保留目前已完成的客户端图片代码和真实联调证据，但不能宣称现有NAS已经提供图片预览。

项目AGENTS.md要求发现需求或架构冲突时停止扩展实现并提交决策问题；新增内部服务也需ADR批准，因此在确认部署方向前不自行增加服务或改变NAS安全策略。
