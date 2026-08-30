# M0-007 交接摘要

## 完成状态

`ready_for_review`

## 完成内容

交付确定性、流式、可取消、路径安全的元数据清单生成器；支持 500k/100k 热目录和
逻辑 100GiB 资产表示；交付版本化故障计划及自动测试。未物化真实资产文件。

## 关键决策

仅使用 Python 3.12 标准库；稳定 ID 由 seed+序号哈希生成，物理相对路径显式记录；
JSONL 通过 partial + fsync + 原子替换输出。

## 修改文件

`packages/test-support/performance/**`、`tests/spikes/performance/**`、
`docs/spikes/M0-007/README.md`、本 handoff 与任务包。

## 模块边界、依赖方向与复用

模块为 `test-performance-foundation`，仅测试支持层；无生产模块、契约、数据库或
跨模块访问。复用标准库 JSON、哈希、临时目录和资源计时能力；未复制产品业务逻辑。

## 新语言、框架或重大依赖

无；Python 3.12 标准库符合工具预算。

## 共享契约或数据库变化

无。manifest/fault-plan 是本测试夹具的显式版本格式，不改变产品契约。

## 测试结果

5 个 unittest 全部通过；500k profile 通过并精确生成 500,000 条。

## 架构测试与质量门禁

通过 `validate_handoff.py`、`validate_architecture_baseline.py`、`git diff --check`。

## 文件安全、权限与性能影响

输出限制在 sandbox/temp，拒绝逃逸和 symlink；无权限或真实系统修改。500k 基线：
9.256015s（外部 9.33s）、峰值 RSS 19,608KiB（外部 19,736KiB）、194,334,025 bytes、54,018.93 records/s；profile 直接断言 100,000 热目录和逻辑 100GiB 资产。

## 技术债、已知问题与风险

峰值 RSS 受 Python 分配器/运行环境影响，后续应在 CI 固定环境建立回归阈值；当前
工具只生成 fixture hash，不验证真实文件哈希。

## 建议合并顺序

在协调器审查 handoff 后直接合并本 commit；不依赖其他生产模块。

## 下一步

M0-009 汇总时复用 profile 命令和摘要字段定义性能回归门禁。

## Codex 线程链接（可选）

仅作为导航，不是唯一交接依据。
