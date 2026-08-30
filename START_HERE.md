# START HERE — 人工与 Codex 启动说明

## 当前状态

需求探索与工程原则已冻结。现在应进入：

```text
需求基线校验
→ M0 技术验证与架构决策
→ 仓库与契约骨架
→ V0.1 资产核心
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

## 初始化仓库

```bash
python scripts/bootstrap_repo.py
python scripts/validate_handoff.py
python scripts/validate_architecture_baseline.py
```

然后让主协调线程：

1. 检查需求基线与废弃清单是否互相冲突；
2. 完成 M0 技术验证，并以 M0-009 冻结编码架构门禁；重点包括 Windows Shell 自定义视图、跨平台服务端发行、AssetLink 协议和文件操作恢复；
3. 创建独立 worktree 任务；
4. 要求每个任务提交标准交接文件和测试；
5. 只在契约稳定后启动 Windows、Android、Web 等并行实现。

## 安全提醒

开发与测试不得直接指向用户真实 NAS 资产。默认使用 `.runtime/sandbox-storage/`、临时 PostgreSQL 和自动生成的模拟资产。任何会移动、覆盖或删除文件的测试都必须运行在隔离目录中。
