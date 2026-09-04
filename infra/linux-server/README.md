# Linux x86-64 / systemd 发行候选

V01-008 生成自包含 `linux-x64` artifact 与确定性 `tar.gz`。`assetlibrary-core-server.service`
是发布候选 unit：专用非 root 用户、只读安装目录、`StateDirectory=assetlibrary`、空 capability、
`NoNewPrivileges`、私有临时目录和内核/设备/主目录保护。

`systemd-evidence.sh` 默认只执行不写系统状态的 `preflight`。真实 `cycle` 只允许在快照可回滚、
明确批准的 Linux 测试机上运行，并要求 root、目标计算机名、任务 evidence marker 与
`ASSETLIBRARY_APPROVE_SYSTEM_CHANGES=1` 同时成立。脚本不得在 NAS 资产主机或承载生产服务的
系统上运行。

测试机上的 evidence root 必须是仓库 `.runtime/sandbox-storage/V01-008` 的规范真后代，路径不含
空格、链接或遍历；把同次发行的 `artifacts/linux-x64` 复制到
`linux-systemd/artifact/linux-x64`，创建 `.assetlibrary-v01-008-evidence-root` 后设置：

复制前还必须在构建机上独立保存 `release-metadata.json` 的 40 位 `source_revision`，以及
`manifest.json` 的 `runtime_aggregate_sha256.linux-x64` 和
`runtime_evidence_binding_sha256.linux-x64`；三者必须作为同一组经可信通道传递，不得从测试机上
可替换的 staging 目录重新取得。root 脚本不会执行 artifact 的 `--build-info` 或
`--health-probe`。文件树校验后，完整目录会复制到 root 所有且不可由测试账号改写的固定
`/var/tmp/assetlibrary-v01-008-evidence`，systemd 只从该目录以非 root 身份启动；健康检查使用
有界、禁代理和禁重定向的 loopback HTTP 请求，周期结束会删除该暂存区。

```bash
export ASSETLIBRARY_EVIDENCE_ROOT='<task-root>/linux-systemd'
export ASSETLIBRARY_EXPECTED_HOSTNAME="$(hostname)"
export ASSETLIBRARY_EXPECTED_SOURCE_REVISION='<40位提交>'
export ASSETLIBRARY_EXPECTED_ARTIFACT_TREE_SHA256='<64位文件树摘要>'
export ASSETLIBRARY_EXPECTED_RUNTIME_EVIDENCE_BINDING_SHA256='<64位revision/RID/文件树绑定摘要>'
bash infra/linux-server/systemd-evidence.sh preflight
```

只有 root、目标主机、工具、边界、artifact、source revision、artifact 文件树 SHA-256 与绑定摘要
预检全部满足且 residue 全零，
才显式设置 `ASSETLIBRARY_APPROVE_SYSTEM_CHANGES=1` 并以 root 执行 `cycle`。测试默认使用现有的
`nobody` 非 root 账户，也可通过 `ASSETLIBRARY_EVIDENCE_USER` 指定已批准的专用测试账户；脚本
不会创建或删除系统用户，并明确拒绝 UID 0、主组 GID 0 或继承 root 组的账号。

静态 unit 校验或 Windows 上生成 linux artifact 都不能关闭 `linux-server-release`；真实周期还
必须处理 M0-006-G3 的非协作命名空间政策。V01-008 不修改该 gate。
