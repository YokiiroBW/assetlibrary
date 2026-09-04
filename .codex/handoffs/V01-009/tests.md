# V01-009 测试记录

## 环境与边界

- Worktree：`C:\YOKI\Codex\worktrees\V01-009`
- 实现 commit：`4300c2d4eceb6104d551264c3f249c95c9f9d68e`
- Windows 非提权会话；没有运行 SCM、Docker daemon、Linux systemd/root 或生产文件写操作。
- Python 3.13.11；.NET SDK 10.0.111；Node 24.20.0；pnpm 11.19.0；JDK 21.0.12+8；Gradle 9.3.1。
- 所有可变输出位于 Git 忽略的 build 目录、`node_modules` 或 `.runtime/sandbox-storage/V01-009/**`。

## V01-009 定向验证

```text
python -I -B scripts/validate_v0_1_alpha.py
  exit 0; ALPHA_INTEGRATION_AUDIT_OK decision=blocked

python -I -B scripts/validate_v0_1_alpha.py --require-ready
  exit 3（由包装断言确认）

python -I -B scripts/validate_v0_1_alpha.py \
  --output .runtime/sandbox-storage/V01-009/reports/current.json
  exit 0；确定性脱敏 JSON，owner marker 正确

python -I -B -m unittest discover \
  -s tests/release -p test_v0_1_alpha_readiness.py -v
  13 passed

python -I -B -m unittest discover \
  -s tests/repository -p test_v0_1_alpha_foundation.py -v
  5 passed
```

负向覆盖：缺必需能力、伪造 `passed`、未知/`skipped` 状态、V01-008 policy/registry 提升、result/evidence 替换和 traversal、缺 owner/follow-up/blocker、声明假 ready、host policy 假业务/写入就绪、重复 JSON key、无效 gate ledger、timeout、异常输出、输出越界/UNC/盘符/控制字符/设备别名/ADS/尾随点空格、reparse parent 和默认无写。

## 完整成功套件

| 套件 | 结果 | 命令或说明 |
| --- | ---: | --- |
| Repository | 54 passed | `python -I -B -m unittest discover -s tests/repository -p test_*.py -v` |
| Release Python | 32 passed | 先构建 Release host，再执行 `tests/release/test_*.py` |
| Database | 39 passed、1 skipped | `tests/database/test_*.py`；PostgreSQL integration 缺专用环境 |
| Architecture | 14 passed | 由最终 `scripts/verify_repository.py` 执行 |
| SDK Python | 14 passed | `tests/sdk/test_*.py` |
| AssetLink | 21 passed | `tests/spikes/assetlink/test_*.py` |
| .NET solution | 185 passed | locked restore、format、Release build、test；0 warning / 0 error |
| Chromium Web | 6 passed | `pnpm --dir apps/web run test:browser` |

唯一成功计数为 365 passed、0 failed、1 skipped。Repository verifier 内重复执行的 21 个 migration manifest 测试和 14 个 architecture 测试没有再次计数。

## 构建、依赖与策略门禁

```text
dotnet restore AssetLibrary.slnx --locked-mode
dotnet format AssetLibrary.slnx --verify-no-changes --no-restore
dotnet build AssetLibrary.slnx --configuration Release --no-restore
dotnet test AssetLibrary.slnx --configuration Release --no-build --no-restore
  185 passed；0 warning；0 error

python -I -B scripts/validate_dotnet_dependencies.py ...
  11 projects / 46 locked packages passed
python -I -B scripts/validate_dotnet_source.py
  158 C# files passed

pnpm@11.19.0 --dir packages/sdk/assetlink/typescript run lint
pnpm@11.19.0 --dir packages/sdk/assetlink/typescript run typecheck
pnpm@11.19.0 --dir packages/sdk/assetlink/typescript run test
  5 passed
packages/sdk/assetlink/kotlin/gradlew.bat ... --dependency-verification strict build
  BUILD SUCCESSFUL；sdkTest passed
python -I -B scripts/validate_assetlink_sdk_dependencies.py --require-build-artifacts
  integrity / license / size passed

pnpm@11.19.0 --dir apps/web run format:check
pnpm@11.19.0 --dir apps/web run lint
pnpm@11.19.0 --dir apps/web run typecheck
pnpm@11.19.0 --dir apps/web run build
python -I -B scripts/validate_web_dependencies.py --require-build-artifacts
python -I -B scripts/validate_web_source.py
  全部通过；无 Node engine 漂移警告

pnpm audit（SDK、Web）
dotnet package list --include-transitive --vulnerable + policy validator
  最终均通过，无已知漏洞报告
```

最终 `python -I -B scripts/verify_repository.py` 通过：handoff、architecture、Alpha audit、.NET source、migration manifest、SDK/Web policy 与 architecture tests 全部绿色。

## Gate 结果

```text
python -I -B tests/architecture/check_release_gates.py --target v0.1-start
  exit 0

python -I -B tests/architecture/check_release_gates.py --target v0.1-release
  exit 3（M0-004-G2、M0-006-G1、M0-006-G2）

python -I -B scripts/validate_v0_1_alpha.py --require-ready
  exit 3（15 个能力 blocker、6 个 target blocker、V01-008 partial input）
```

退出 3 是经断言的预期发布阻断，不计为测试失败。

## 环境限制与开发期异常

- PostgreSQL integration class：因 `ASSETLIBRARY_TEST_POSTGRES_BIN` 未配置，正式 `skipped=1`；没有用静态测试替代其环境证据。
- `tests/spikes/performance/profile_500k.py`：Windows 缺少 Unix-only `resource` 模块，未执行；scheduled CI 的权威 runner 为 Ubuntu。
- `tests/spikes/performance/test_generator.py`：5 项通过，symlink 用例因当前 token 缺创建 symlink 权限报 WinError 1314；该不完整 suite 不计入成功总数。
- Release suite 首次在未构建 host 时按设计失败；按 workflow 顺序 locked restore/build 后完整 32/32 通过。
- 首次 NuGet vulnerability query 遇到 TLS 连接被远端关闭；一次 TypeScript SDK audit 遇到 registry timeout。两者在未改变输入后重试成功，最终政策检查通过。
- 误调用 Corepack 默认版本时被仓库策略拒绝；之后显式使用 Node 24.20.0 + pnpm 11.19.0，嵌套脚本无 engine 漂移警告。

## 未运行

- Windows SCM install/start/LocalService health/stop/uninstall/zero-residue。
- Docker real-daemon build/up/health/restart/hardening/down-with-volumes。
- Linux root/systemd lifecycle 与 namespace containment。
- 1/20/100 GiB release-like 真实字节、生产耐久写入和真实 NAS/资产路径。

这些缺口全部保留为 readiness blocker，没有被 `skipped` 或静态文件存在折算成通过。
