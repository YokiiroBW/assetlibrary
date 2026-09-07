# START HERE — 人工与 Codex 启动说明

## 当前状态

需求与架构已冻结，当前已进入 V0.1 受控实现。M0-009 和 V01-001 至 V01-014 的组件交接已进入主线；V01-008 保持 partial，Alpha 发布仍 blocked。先读 `.codex/project-state.json`、`docs/releases/V0.1_ALPHA_READINESS.md` 和 `docs/audits/2026-09-05-alignment.md`，从未完成项继续。

路线及当前所处位置：

```text
需求基线校验
→ M0 技术验证与架构决策
→ 仓库与契约骨架
→ V0.1 资产核心（当前：组件、沙箱与集成准备）
→ 跨端客户端
→ Explorer 深度集成
→ 专业资源库与增强能力
```

不要继续无边界追加大型模块。确需改变需求时，先更新 `docs/02_已确认需求基线.md`、`docs/18_已砍除与延期清单.md` 和对应 ADR，再调整任务图。

## 推荐阅读顺序

1. `docs/00_交接总览.md`
2. `docs/01_核心原则与范围边界.md`
3. `docs/02_已确认需求基线.md`
4. `docs/03_资源库与物理目录模型.md`
5. `docs/04_客户端平台与连接模式.md`
6. `docs/05_总体架构与技术框架.md`
7. `docs/06_AssetLink_WebDAV_API_MCP.md`
8. `docs/09_同步传输与冲突处理.md`
9. `docs/14_格式与Provider能力矩阵.md`
10. `docs/16_版本路线与验收门禁.md`
11. `docs/17_Codex并行开发工作流.md`
12. `docs/18_已砍除与延期清单.md`
13. `docs/22_编码与架构开发原则.md`

## 启动 Codex 主协调线程

Windows PowerShell：

```powershell
./scripts/codex-start.ps1
```

Linux / Git Bash：

```bash
./scripts/codex-start.sh
```

脚本会生成并尝试打开一个 `codex://new?...` 深度链接，工作区指向当前仓库，提示词要求读取 `.codex/START_HERE.md`。链接只会预填提示词，不应被当作自动执行或唯一交接机制。

## 检查现有仓库

```bash
python -I -B scripts/verify_repository.py
python -I -B scripts/validate_v0_1_alpha.py
python -B tests/architecture/check_release_gates.py --target v0.1-start
```

然后让主协调线程：

1. 检查需求基线与废弃清单是否互相冲突；
2. 读取 M0-009 冻结结果及残余门禁，核对已有组件与发布宿主的组合缺口；
3. 创建独立 worktree 任务；
4. 要求每个任务提交标准交接文件和测试；
5. 按现有门禁推进已授权能力，先稳定独立客户端，再开展生产 Explorer 集成。

## 安全提醒

开发与测试不得直接指向用户真实 NAS 资产。默认使用 `.runtime/sandbox-storage/`、临时 PostgreSQL 和自动生成的模拟资产。任何会移动、覆盖或删除文件的测试都必须运行在隔离目录中。
