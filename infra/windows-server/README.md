# Windows x64 / Windows Service 发行候选

`service-evidence.ps1` 只用于隔离 Windows VM 的 V01-008 SCM 证据周期。默认动作是
只读 `preflight`；真实周期必须同时提供：

- `-Action cycle`
- `-ApproveSystemChanges`
- 与目标 VM 完全相同的 `-ExpectedComputerName`
- 位于任务 evidence root 内的自包含 win-x64 artifact
- 在构建机上、复制前独立记录的 `runtime_aggregate_sha256.win-x64`
- 同一 manifest 中绑定 revision、RID 与文件树摘要的 `runtime_evidence_binding_sha256.win-x64`
- evidence root marker `.assetlibrary-v01-008-evidence-root`

周期创建任务唯一服务 `AssetLibrary-V01-008-Evidence`，以 `LocalService` 运行。脚本只有在
service owner marker 与精确 ImagePath 同时匹配时才会停止或删除服务。通过摘要校验的完整运行
目录会先复制到以受保护 ACL 原子创建的 `C:\Windows\Temp\AssetLibrary-V01-008-Evidence`，禁用继承并改为
Administrators 所有；服务和 state 只使用这个不可由测试账号改写的暂存区。任何失败都会优先
执行 owner-guarded cleanup，最终检查 service、HKLM key、secure staging、state、进程和监听均无残留。
管理员脚本不会执行 artifact 的 `--build-info` 或 `--health-probe`；它先按规范化文件树校验
独立提供的 SHA-256，再仅由 SCM 以 `LocalService` 启动 artifact，健康检查由有界、禁代理和
禁重定向的 loopback HTTP 客户端完成。

不要在承载 Codex 的当前主机执行 `cycle`。推荐在快照可回滚的 `YOKICESHI` 测试 VM 中，
由管理员窗口运行；普通 `AssetLibraryTest` 账号只用于验证服务不依赖交互管理员登录。

在隔离 VM 内，先从同一次 `release-build` 把 `artifacts/win-x64` 复制到一个全新的
`windows-service/artifact/win-x64`，并在 `windows-service` 根创建
`.assetlibrary-v01-008-evidence-root`。在复制前，从构建机上的 `release-metadata.json` 读取 40 位
`source_revision`，并从 `manifest.json` 读取 `runtime_aggregate_sha256.win-x64` 与
`runtime_evidence_binding_sha256.win-x64`。三个值必须作为同一组通过独立可信通道带到 VM，
不能从 VM 上可替换的 staging 目录重新取值。然后运行：

```powershell
pwsh -NoProfile -File .\infra\windows-server\service-evidence.ps1 `
  -Action preflight `
  -EvidenceRoot <task-root>\windows-service `
  -ExpectedComputerName $env:COMPUTERNAME `
  -ExpectedSourceRevision <40位提交> `
  -ExpectedArtifactTreeSha256 <64位文件树摘要> `
  -ExpectedRuntimeEvidenceBindingSha256 <64位revision/RID/文件树绑定摘要>
```

只有预检同时显示 `elevated/computer_matches/boundary_valid/artifact_present/source_revision_supplied`
以及 `artifact_tree_sha256_supplied/artifact_tree_sha256_matches`、
`runtime_evidence_binding_supplied/runtime_evidence_binding_matches` 均为 `true` 且 residue 全零，
才在同一管理员窗口把动作改为 `cycle` 并增加
`-ApproveSystemChanges`。最终 JSON 只有在 SCM 生命周期及零残留都通过时才返回 `passed`。

该脚本不是产品安装器。正式服务名称、升级、凭据/证书、数据库连接、备份与回滚由 V01-009
及后续安装器任务冻结。
