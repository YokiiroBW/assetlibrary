# M0-009 测试记录

## 执行环境

- Windows 11 x64，普通非提升的当前 Codex 会话
- Git 工作树：`C:\YOKI\Codex\AssetLibrary-worktrees\M0-009`
- 分支：`codex/m0-009-architecture-quality-gate`
- 基线提交：`48035e8`（M0-009 初始化）
- Python：Codex bundled Python，所有命令设置 `PYTHONDONTWRITEBYTECODE=1` 或使用 `-B`

## 必需仓库门禁

```text
python -B scripts/validate_handoff.py
python -B scripts/validate_architecture_baseline.py
python -B scripts/verify_repository.py
python -B -m unittest discover -s tests/architecture -p "test_*.py" -v
python -B -m unittest discover -s tests/repository -p "test_*.py" -v
git diff --check
```

结果：全部通过。

- architecture 正反夹具：13/13；覆盖 Domain/Application 反向依赖、平台依赖、跨模块内部层、公开契约单向引用、循环、语言/清单/锁文件、未登记 source root、客户端与 Provider 数据库直连、适配器复制核心状态机、Shell 重型依赖、无边界共享目录、生成 SDK 摘要、M0 blocker 完整覆盖/fail-closed、V0.1 启动矛盾、CI 合同和 release target。
- repository workflow：8/8。
- 架构基线：通过；`packages/test-support` 是唯一 active governed root，13 个尚无产品源码的 root 明确报告 inactive。
- handoff 和 repository verification：通过；验证过程未产生 `__pycache__` 或 `.pyc`。

## Release target 行为

```text
python -B tests/architecture/check_release_gates.py --target v0.1-start
```

返回 0 和 `RELEASE_GATE_ALLOWED: v0.1-start`。

以下目标逐一执行并断言返回预期退出码 3：

```text
v0.1-release
production-file-writes
provider-release
explorer-v0.5
windows-server-release
linux-server-release
```

阻断分别解析出 M0-004、M0-006、M0-008、M0-002 的对应开放 gate；PowerShell 包装断言整体返回 0。架构 JSON 文件另行全部解析通过。

## 受影响的安全 Spike 测试

```text
python -B -m unittest discover -s tests/spikes/assetlink -p "test_*.py" -v
python -B -m unittest discover -s tests/spikes/windows-shell -p "test_*.py" -v
python -B -m unittest discover -s tests/spikes/file-safety -p "test_*.py" -v
python -B -m unittest discover -s tests/spikes/provider-sandbox -p "test_*.py" -v
```

| 套件 | 通过 | 失败 | 平台跳过 | 说明 |
|---|---:|---:|---:|---|
| AssetLink | 21 | 0 | 0 | 只读 schema/语义测试 |
| Windows Shell contract | 16 | 0 | 0 | 仅源码/契约检查；未注册 COM、未打开 Explorer 测试扩展 |
| Windows file-safety | 6 | 0 | 1 | 临时固定 NTFS 目录；Linux adapter 模块按平台跳过 |
| Windows Provider | 12 | 0 | 18 | contract + Windows Job/Restricted Token；POSIX/Linux 项明确跳过 |

唯一测试计数：76 通过、0 失败、19 平台跳过。重复执行于 `verify_repository.py` 内的 architecture 用例不重复计数。

## 非门禁诊断与环境限制

执行 M0-007 performance 单元套件时，前 5 项通过，第 6 项在创建 Windows 符号链接前置步骤报 `OSError [WinError 1314]`；当前令牌没有创建符号链接权限。该命令整体不计入上述通过数，也没有修改既有 Spike 来掩盖环境限制。M0-007 已提交的 canonical Linux 证据仍为 6/6 和一次 500k profile；新 `scheduled-release` workflow 固定在 Ubuntu 执行相同 profile。

本机没有 `actionlint`、Ruby、`yq`、Node 或 PyYAML。尝试导入 PyYAML 返回 `ModuleNotFoundError`，因此未声称独立 YAML parser 通过；workflow 已由架构合同核对 tier、超时和每条命令，且逐条本地执行了适用于 Windows 的 Python 命令。远端 runner 首次执行仍是交接风险。

## 未执行且不得折算为通过

- Windows 管理员 SCM/HKLM、Shell COM 注册或真实 Explorer 生命周期；
- Docker daemon / Compose；
- Windows AppContainer 或等价文件/网络硬沙箱；
- Linux namespace/seccomp/delegated cgroup 与 POSIX supervisor；
- 1/20/100 GiB 真实字节、突然断电/控制器缓存、8 小时 soak；
- Web、WinUI、Android 生产构建和浏览器 E2E；
- GitHub-hosted workflow 实际运行。

这些不是 M0-009 启动 blocker，但仍由 `m0-gates.json` 或 `ci-tiers.json.future_native_gate_activation` 阻断相应功能/发布目标。

## 安全边界

本次命令未使用管理员权限，未修改 UAC、注册表、SCM、Docker、系统服务、真实 NAS 或用户资产。文件写入仅发生在 Git worktree、测试临时目录和现有测试沙箱约定内。
