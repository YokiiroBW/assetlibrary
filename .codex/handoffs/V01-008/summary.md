# V01-008 交接摘要

## 完成状态

`partial`

- 分支：`codex/v01-008-packaging-release-evidence`
- 实现与安全修复 commit：`81eb838136b9826c9ed6e0ac9e1ae9fdb42cfd49`
- Worktree：`C:\YOKI\Codex\worktrees\V01-008`
- `partial` 原因：同源 native artifact、最终 Windows 原生进程、静态门禁与安全修复已完成；真实 Windows SCM、Docker daemon 和 Linux systemd 周期仍缺批准环境，保持 `blocked_missing_environment`。
- 交接 commit：本交接三件套提交后的分支 tip；避免在 commit 内容中写入无法自引用的哈希。

## 已完成内容

- 新增唯一 `AssetLibrary.CoreServer.Host`，复用既有 CoreServer 核心程序集，统一支持控制台、Windows Service、systemd 和容器生命周期；未复制业务核心。
- 实现失败关闭的宿主配置、稳定退出码、低敏生命周期日志、`/healthz`、host-only `/readyz`、`--health-probe` 与 `--build-info`。
- 从指定 Git commit 的只读源码快照执行两次 win-x64/linux-x64 self-contained cold publish，生成 RID 隔离 lock、完整路径/长度/SHA-256 清单、确定性 archive 和来源绑定。
- 新增 Windows Service、Docker/Compose、Linux/systemd 发行候选与证据脚本；系统级写动作要求精确目标、显式批准、任务 marker、权限校验和 owner-guarded cleanup。
- 新增 release CI、独立 validator、19 个 release 安全/发行测试、18 个 .NET packaging 测试及仓库负向门禁。
- 未接入数据库 runtime、认证、业务 API、生产文件写、Provider、Explorer 或安装器。

## 安全审查修复

安全扫描 `b1a5d75d-0290-4df5-b8b9-542f30658ac4` 的 7 个报告项均已在 `81eb838...` 修复并回归：

| 项 | 修复后的安全不变量 |
| --- | --- |
| Windows health polling 越过 LocalService | 提权编排器只用有界 HTTP 客户端读取精确 JSON，不再运行 staged binary 探针。 |
| Linux health polling 越过 systemd identity | root 编排器只使用有界 `curl`，且拒绝 UID/GID 0 或 root 补充组的 evidence user。 |
| Windows 提权执行未认证 staged binary | 完整 artifact tree 经独立摘要验证后复制到原子创建、管理员拥有且冻结 ACL 的固定 secure root；SCM 只运行 secure copy，提权脚本不执行其 `--build-info`。 |
| MSBuild 自动导入未纳入来源 | 精确 issuance input 包含 `Directory.Build.targets`；拒绝外部 override/祖先 targets，并在选定 commit 的 Git snapshot 中固定 props/targets 后构建。 |
| Linux root 执行未认证 staged binary | root-owned `0700` secure root 中复制并复核完整 tree，随后只由非 root systemd identity 执行。 |
| archive 名称在规范检查前触发 I/O | 先验证唯一允许的 archive 相对名，再通过安全句柄读取。 |
| Windows manifest 路径别名/逃逸 | 拒绝反斜杠、冒号、控制字符、尾随点/空格、设备别名；单次 no-follow 安全句柄读取，并先复制到匿名临时快照再解析 archive。 |

额外收紧：所有发行 Python 入口强制 `-I -B`，完整 `scripts/**` 纳入 issuance provenance；runtime evidence binding 同时绑定 source revision、RID 和 tree digest；安全副本在周期前后均复核完整摘要。

## 发行产物与来源证据

- 发行合同：`v01-008/1`
- 源码提交：`81eb838136b9826c9ed6e0ac9e1ae9fdb42cfd49`
- 两次 cold publish：完整清单一致，共 679 个文件。
- issuance input tree SHA-256：`e53b1d0c33e593e87b075c39439190d06df05bd68d4b2b8c663ec4d8e1bf7bcd`
- artifact aggregate SHA-256：`c5ed8786479e07a81803f5ab57bbe238365bcc3e2a41c9350f7bb846801c9e3f`
- release-lock aggregate SHA-256：`73a5aa88f3be7d06c7704ebb7cf2bfdb4116973f0b59dd8a270e84de1100e442`
- Linux archive：`assetlibrary-core-server-linux-x64-81eb838136b9.tar.gz`，48,219,608 bytes，SHA-256 `0e88179a4e5ac107e6719c6d9e255f13f64482a1f123e31842c96ac243c4b414`。
- Windows archive：`assetlibrary-core-server-win-x64-81eb838136b9.zip`，49,127,994 bytes，SHA-256 `d117bf500ec869686049e4936fa585597ba3b7a0f0ce50f7e1875a24e388c5da`。
- runtime evidence binding：Linux `d0eab2932902aa454bb03e3de6779a4a15e0f54d74105fe3185f999d796d2340b`；Windows `d2b216383051f7e706aa1cdd7295dbaec0f5665d5d84a460613531989bb6937f4`。
- 所有二进制、缓存和运行日志仅位于 `.runtime/sandbox-storage/V01-008/release-build/**`，未提交到 Git。

## 平台证据状态

| 目标 | 状态 | 已验证事实 | 门禁结论 |
| --- | --- | --- | --- |
| Native artifacts | `passed` | 从 commit snapshot 完成两次 cold publish、独立 `--require-artifacts` 校验、archive 快照复核和 provenance/binding 校验 | 可作为同源 artifact 证据 |
| Native Windows process | `passed` | 最终 win-x64 EXE 在非提权 loopback 启动；health/ready/独立 probe 正确；停止后 listener 与 state 残留均为 0 | 仅证明原生进程合同，不等同于 SCM |
| Windows Service | `blocked_missing_environment` | 当前主机只执行只读 preflight；`elevated=false`，未创建服务、HKLM、secure staging、state、进程或监听 | `windows-server-release` 保持阻断 |
| Docker / Compose | `blocked_missing_environment` | 当前无可用 Docker CLI/daemon；未 build/up，未创建容器、网络、volume 或镜像 | `docker-release` 保持阻断 |
| Linux systemd | `blocked_missing_environment` | GNU Bash 语法通过，但没有批准的 Linux root/systemd runner；未安装 unit、账户、目录、进程或监听 | `linux-server-release` 保持阻断 |

## 模块边界与架构审查

- 依赖保持 `Host/Infra -> AssetLibrary.CoreServer public assembly -> Application -> Domain`；Host 不访问模块 Infrastructure、数据库表或内部实体。
- 三种发行共享同一 host project、source snapshot、lock 与 manifest；差异只在 host lifecycle 和 `infra/**`。
- `Directory.Build.targets` 当前在仓库中不存在；其“缺失状态”同样被精确 provenance 约束：若根目录新增未跟踪文件会阻断，祖先文件被拒绝，选定 commit snapshot 固定为不导入外部 targets。
- 无共享 wire contract、AssetLink、迁移、统一错误码或生成 SDK 变化。
- 新依赖仅 `Microsoft.Extensions.Hosting.WindowsServices 10.0.11`（MIT），用于 SCM 生命周期适配；相关 abstractions 统一锁定到 `10.0.11`。

## 测试与门禁

- 累计唯一自动测试：360 passed，0 failed，0 skipped（.NET 185、database 53、repository 48、architecture 14、SDK Python 14、AssetLink 21、Chromium 6、release Python 19）。
- 安全补丁后重跑：release 19/19、repository 48/48、architecture 14/14、`verify_repository.py`、Python/PowerShell AST、GNU Bash `-n`、双冷构建、独立 artifact validator 与最终 EXE 进程冒烟，全部通过。
- 未受安全补丁影响的 .NET/database/SDK/Web 代码与依赖输入沿用同一分支先前完整绿测，不做无输入变化的重复执行。
- `v0.1-start` 返回 0；`windows-server-release`、`docker-release`、`linux-server-release`、`production-file-writes`、`v0.1-release` 均按设计返回 3。

## 文件安全、权限与性能影响

- 当前宿主测试仅在 `.runtime/sandbox-storage/V01-008/**` 与 loopback 内运行；未访问真实资产、NAS 或用户数据，未注册服务、写 HKLM、启用系统功能、修改 UAC/账户/PATH。
- Windows/Linux secure staging 采用固定系统临时路径、强 owner marker、精确资源身份和完整 tree digest；不确定状态一律拒绝创建、执行或清理。
- 日志/build-info 不写 data path、用户、主机、凭据、连接串或异常全文；probe/进程/cleanup 均有有界超时。
- manifest/archive 采用流式哈希；archive 验证只复制单个归档到匿名临时文件，不把大文件整体载入内存。本任务没有 50 万资产业务查询路径。

## 仍需完成与风险

- 安全代码修复已经本地验证，但 Windows secure ACL + LocalService、Linux root-owned staging + systemd identity、Docker hardening 仍必须在各自真实隔离环境执行完整 cycle 才能形成平台运行时证据。
- 当前 host 仍只有 host-level health/readiness/build-info；认证、PostgreSQL runtime、业务 API、TLS/secret、迁移/升级/回滚和正式安装器属于 V01-009 或后续任务。
- 生产文件写与 M0-006-G1/G2/G3 仍阻断；这些 artifact 不得表述为可发布 Alpha。

## 建议合并与下一步

- V0.1 建议合并顺序 `7`；本任务实现与交接可合入 `main`，但不得因此关闭平台 release gate。
- 将基于 `81eb838...` 的 Windows 测试包放入 `\\Yokiceshi\HANA` 的新目录，避免覆盖之前 M0-002 文件；在快照可回滚的 `YOKICESHI` VM 内由 `AssetLibraryTest`/管理员按 README 分别执行 preflight 与批准的 SCM cycle。
- 之后在真实 Docker daemon 和批准 Linux systemd runner 分别执行 evidence cycle，由协调线程独立复核零残留结果。
