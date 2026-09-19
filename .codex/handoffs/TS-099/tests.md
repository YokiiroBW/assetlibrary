# TS-099 测试与验证记录

本文件严格区分三类证据：**已实际执行并通过**、**已实际执行但被环境阻断**、**未执行**。
不得把编译通过读作测试通过，不得把被环境阻断读作断言失败。

## 1. 已实际执行并通过（沙箱内，本工作树）

| 命令 | 结果 | 日志 |
| --- | --- | --- |
| `dotnet restore AssetLibrary.slnx --locked-mode -p:NuGetAudit=false -m:1` | 退出 0，14 个项目还原 | `.runtime/dsh-delivery/logs/restore-locked2.txt` |
| 锁定包完整性核对（50 个在解内包） | 双向一致 50/50 | `.runtime/dsh-delivery/logs/lock-hashes.txt` |
| `dotnet build AssetLibrary.slnx -c Release --no-restore -m:1 -p:BuildProjectReferences=false` | 退出 0，**0 警告 0 错误**，15 个项目 | `.runtime/dsh-delivery/logs/build-slnx2.txt` |
| `dotnet build services/core-server/AssetLibrary.CoreServer.csproj -c Release` | 退出 0，0 警告 0 错误 | `.runtime/dsh-delivery/logs/build-core-*.txt` |
| `dotnet build tests/.../AssetLibrary.TransferOperation.Tests.csproj -c Release -m:1 -p:BuildProjectReferences=false` | 退出 0，0 警告 0 错误 | `.runtime/dsh-delivery/logs/build-tests-m4.txt` |
| `python -B tests/architecture/check_release_gates.py --target v0.1-start` | `RELEASE_GATE_ALLOWED: v0.1-start` | `.runtime/dsh-delivery/logs/release-gates.txt` |
| 仓库校验 12 步（见下） | 全部通过 | `.runtime/dsh-delivery/logs/verify-steps.txt` |

### 锁定依赖证据（回应协调方"复制缓存不作为可信性证明"）

复制来的包缓存本身不是证明。本卡对**每个**在解内锁定包做了两次独立核对：

1. `packages.lock.json` 的 `contentHash` == 缓存 `.nupkg.metadata` 的 `contentHash` → **50/50 一致**
2. `<id>.<version>.nupkg.sha512` == base64(SHA-512(`.nupkg` 字节)) → **50/50 一致**

脚本：`.runtime/dsh-delivery/logs/verify-lock-hashes.py`，输出 `.runtime/dsh-delivery/logs/lock-hashes.txt`。
说明：NuGet 的 `contentHash` 与 `.nupkg` 文件摘要**本来就是两个不同摘要**，早期误把二者直接比较得到的
"185 处不一致"是核对方法错误，不是包损坏；诊断过程记录在
`.runtime/dsh-delivery/logs/lock-hash-diagnosis.txt`。7 个 `apps/windows-client` 与
`tests/windows-{client,setup}` 的锁文件不属于本解决方案，未在本卡还原范围。

### 仓库校验通过的 12 步

`handoff`、`architecture-baseline`、`v0_1_alpha`、`dotnet-source`、`migration-tool-validate`、
`assetlink-sdk-generate-check`、`assetlink-sdk-source`、`assetlink-sdk-deps`、`web-deps`、`web-source`、
`native-theme-export`、`android-deps`。

其中与本次改动直接相关的两项：

- `validate_dotnet_source.py` → `.NET source policy passed (666 C# files scanned)`，
  即跨文件重复令牌块（≥60 token）与敏感日志模式检查通过。
- `validate_architecture_baseline.py` → `Architecture quality gate passed`，
  即层级依赖方向、跨模块公开层、禁止令牌（含 Domain 的 `system.io`）与循环依赖检查通过。

## 2. 已实际执行但被环境阻断（不是通过，也不是断言失败）

| 命令 | 现象 | 判定 |
| --- | --- | --- |
| `dotnet test ... -c Release --no-build --no-restore` | `vstest.console process failed to connect to testhost process after 90 seconds`，测试运行中止 | **测试从未运行**；VSTest 与其 testhost 之间的命名管道被沙箱拒绝 |
| `dotnet format AssetLibrary.slnx --verify-no-changes --no-restore` | `UnauthorizedAccessException` at `NamedPipeClientStream.TryConnect` | `MSBuildWorkspace` 的 build host 需要命名管道，被沙箱拒绝 |
| `dotnet format <test project>.csproj ...` | 同上 | 同上 |
| `python -m unittest discover -s tests/architecture` | 14 个用例 28 个 **error**，全部在 `setUp` 写临时目录时 `PermissionError` | 环境错误，非断言失败 |
| `python -m unittest discover -s tests/database` | 同上，`PermissionError` 于 `tempfile` 清理 | 环境错误，非断言失败 |

已用探针定位根因（`.runtime/dsh-delivery/logs/` 内记录）：

- 沙箱允许在工作树 `.runtime` 下写文件（`WRITE OK`）。
- `tempfile.mkdtemp()` 能创建目录，但在其中**写文件**被拒绝
  （`PermissionError: ...\AppData\Local\Temp\dsh-926CZK\tmph36ygrvs\a.txt`）。
  把 `TMP`/`TEMP`/`TMPDIR` 指向工作树后，`unittest` 仍在清理阶段被拒。

按协调指令：**不**申请提权、**不**改动 ACL 或全局配置、**不**改断言/skip/替换测试引擎。
`dotnet test` 原始失败日志保留在 `.runtime/dsh-delivery/logs/test-1.txt`。

## 3. 未执行

- 上述 `dotnet test`：43 个已编写测试方法（`MediaPackageBoundaryTests` 8、
  `MediaPackageContractTests` 9、`MediaPackageFailureTests` 19、`MediaPackageInspectionTests` 7）
  **一个都没有运行过**。
- `dotnet format --verify-no-changes` 的真实结论。
- 真实 NAS、真实数据库、真实媒体解码、真实发布/索引链路。
- 恶意并发目录替换（TOCTOU）、断电持久性。

## 4. 已编写测试覆盖（仅表示"已编写"，不代表已通过）

| 文件 | 用例数 | 覆盖 |
| --- | --- | --- |
| `MediaPackageContractTests.cs` | 9 | 夹具 LF 摘要、两个正例、17 个反例、原始字节摘要、键序无关、冻结词表 |
| `MediaPackageInspectionTests.cs` | 7 | 单 P/多 P 正常路径、报告恒不授权 + `unverified` 完整、未授权零 I/O、`target_exists`、重复预检确定性、不创建目标目录 |
| `MediaPackageBoundaryTests.cs` | 8 | 原始路径拒绝/接受清单、大小写折叠碰撞、重复声明路径、`Contains`/`Overlaps` 前缀安全、逃逸拒绝、根重叠、链接根/链接文件拒绝 |
| `MediaPackageFailureTests.cs` | 19 | 空间不足、恰好余量、空间未知、源缺失、多余文件/目录、大小/哈希不符、运行中撤销 → `scope_changed`、离线/过期 scope、取消传播、超时、文件数/读取/清单预算、并发 `busy` 与释放、问题上限与截断、报告不含绝对路径与内容 |

链接（符号链接/重解析点）用例在机器无法创建链接时使用 `Assert.Inconclusive`，
不伪装成通过。

## 5. 结论

交付状态 **`needs_validation`**：编译、静态、依赖与架构门禁已实际通过；
**测试未执行**，必须由协调方在固定提交后用真实命令复验，本卡不宣称任何测试通过。
