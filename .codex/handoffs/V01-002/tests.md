# V01-002 测试记录

## 执行环境

- Windows Codex worktree：`C:\YOKI\Codex\AssetLibrary-worktrees\V01-002`。
- Python：Codex bundled Python 3.12，`PYTHONDONTWRITEBYTECODE=1`，所有仓库命令使用 `-B`。
- .NET：任务沙箱内 SDK `10.0.111`；`DOTNET_CLI_HOME`、`NUGET_PACKAGES` 均指向本 worktree `.runtime/`。
- TypeScript：任务 `.runtime/toolchains` 内 Node.js `24.20.0`，pnpm `11.19.0`，TypeScript `6.0.3`。
- Kotlin：任务 `.runtime/toolchains` 内 Eclipse Temurin JDK `21.0.12+8`、Gradle wrapper `9.3.1`、Kotlin plugin `2.3.20`。
- 未安装系统级工具，未运行管理员命令，未操作注册表、Explorer、服务、Provider 或真实资产。

## 执行命令

```text
python -B scripts/generate_assetlink_sdks.py --check
python -B -m unittest discover -s tests/sdk -p "test_*.py" -v
python -B -m unittest discover -s tests/spikes/assetlink -p "test_*.py" -v
python -B scripts/verify_repository.py
python -B -m unittest discover -s tests/repository -p "test_*.py" -v
python -B tests/architecture/check_release_gates.py --target v0.1-start

dotnet restore AssetLibrary.slnx --locked-mode
dotnet format AssetLibrary.slnx --verify-no-changes --no-restore
dotnet build AssetLibrary.slnx --configuration Release --no-restore
dotnet test AssetLibrary.slnx --configuration Release --no-build --no-restore
dotnet package list --project AssetLibrary.slnx --include-transitive --vulnerable --no-restore --format json --output-version 1
python -B scripts/validate_dotnet_dependencies.py --solution AssetLibrary.slnx --packages-dir .runtime/nuget --vulnerability-report .runtime/dotnet-vulnerabilities.json
python -B scripts/validate_dotnet_source.py

pnpm --dir packages/sdk/assetlink/typescript install --frozen-lockfile
pnpm --dir packages/sdk/assetlink/typescript run lint
pnpm --dir packages/sdk/assetlink/typescript run typecheck
pnpm --dir packages/sdk/assetlink/typescript run build
pnpm --dir packages/sdk/assetlink/typescript run test
pnpm --dir packages/sdk/assetlink/typescript audit --audit-level low

packages/sdk/assetlink/kotlin/gradlew.bat -p packages/sdk/assetlink/kotlin --no-daemon --dependency-verification strict build sdkTest
python -B scripts/validate_assetlink_sdk_dependencies.py --require-build-artifacts
python -B scripts/validate_assetlink_sdk_source.py
git diff --check
```

另以空的任务级 `GRADLE_USER_HOME` 运行官方 wrapper，真实下载 Gradle 9.3.1 distribution 并验证 `distributionSha256Sum` 后完成 strict build。

## 架构与契约测试

- `scripts/verify_repository.py`：通过；handoff、M0-009 架构基线、.NET source、SDK generation/source/dependency 全部通过。
- `tests/architecture/test_architecture_rules.py`：14/14 passed，包括新增 LF/CRLF 合同摘要一致性反例。
- `tests/repository/**`：24/24 passed，其中 V01-002 SDK 基础合同 5/5。
- `tests/sdk/**`：14/14 passed；生成确定性、完整 schema shape、三端消息/字段覆盖和失败关闭策略均通过。
- `tests/spikes/assetlink/**`：21/21 passed；冻结合同、演进、断连/重放、范围、哈希和 uint64 语义保持不变。
- `check_release_gates.py --target v0.1-start`：`RELEASE_GATE_ALLOWED`；没有修改 release ledger。

## 通过

- 生成器：7 个输出连续生成字节一致；清单覆盖 20 个消息和 6 个 target 输出摘要，三个合同 stamp 相同。
- .NET：5 个 solution projects locked restore/build 成功，0 warnings、0 errors；AssetLink 13/13、foundation 2/2，共 15/15 MSTest passed。
- TypeScript：frozen install、lint、strict typecheck、build 成功；5/5 Node tests passed；audit 无已知漏洞。
- Kotlin：strict dependency verification build 成功；4 个逻辑检查覆盖往返、unknown、uint64 与畸形 envelope。
- 依赖/许可证/完整性：pnpm lock、Gradle lock、verification metadata、wrapper JAR 和 distribution SHA-256 均通过；.NET runtime SDK 保持 BCL-only。
- 体积：TypeScript `dist` 16,938 bytes、.NET DLL 20,992 bytes、Kotlin JAR 55,362 bytes，均低于预算。
- 最终计数：97 passed，0 failed，0 skipped；`git diff --check` 通过，仓库无 Python cache residue。

## 失败 / 跳过

- 最终失败：0；最终跳过：0。
- 开发中新增的两个生成器负向测试最初因夹具字段和文件名选择错误而失败；修正为真实 `server_id` alias 和 `handshake.schema.json` 后 14/14 通过，未掩盖产品错误。
- Windows/Ubuntu GitHub Actions 尚未远程执行；这是待合并检查产生的外部证据，不计作本地测试跳过或通过。

## 故障注入与恢复验证

- 临时副本分别删除、手改或增加 generated 文件，`--check` 均拒绝。
- 临时合同副本删除一个 envelope、改变 common wire kind、注入无法解析的外部 `$ref`，生成器均失败关闭。
- 临时依赖副本注入 TypeScript 版本漂移、删除 Gradle artifact SHA-256、缺少三个构建产物，依赖门禁均拒绝。
- 临时源码副本增加网络源码、敏感凭证日志和业务状态机 token，源码门禁均拒绝。
- LF 与 CRLF 合同副本产生相同摘要，跨 runner 不再发生伪漂移。
- 所有故障夹具仅位于系统临时目录或被忽略的 `.runtime/`，未操作真实资产。

## 性能数据

- 本地生成器、合同和策略测试均为亚秒级；最终并行原生验证约 11 秒内完成最慢的 Kotlin build。
- 三端发行级核心产物合计小于 100 KiB；任务只解析单条 JSON 消息，复杂度 O(n)，无 50 万资产扫描路径。
- CI 预算从 10 分钟调整为 20 分钟，以容纳 Windows/Ubuntu 首次 Gradle/JDK/Node 依赖缓存填充；只缓存依赖，不缓存构建产物。

## 尚未覆盖

- 尚无远程 Ubuntu/Windows Actions 日志；跨平台最终证据由合并检查产生。
- 纯 SDK 层没有浏览器、Android instrumentation、RDP、Explorer、Provider、网络传输、认证、权限或文件系统测试对象。
- Kotlin 逻辑 runner 不输出 JUnit XML；首个 Android consumer 必须引入平台级测试与报告。
- SDK 有意不执行业务语义验证；M0-003 semantic suite 和未来 Application/Core 消费端继续拥有这些规则。
