# V03-022 验证

| 范围 | 结果 | 证据 |
|---|---|---|
| Windows适配/Host离线 | 120/120 | V03-024；delivery/windows-offline-module.trx |
| 原生Shell | 13/13 | delivery/shell-view-tests.txt + shell-remaining-tests.txt；View1.10s，其余69.81s |
| 原生Gallery | 12/12 | delivery/gallery-tests.txt；1.16s；新快捷键用例在V03-023额外通过 |
| Setup生产逻辑 | 23/23 | delivery/setup-tests.txt |
| 包构建与锁 | 6+16通过 | package-tests.txt、release-lock-tests.txt；RID锁只改SDK project版本范围 |
| 最终自包含Setup CLI | 8/8 | delivery/setup-cli.json；合成payload、不写真实HKCU |
| NativeLive | 最终3个方法有通过证据 | 两个未变方法在initial TRX通过；图片最终1/1顺序双profile四张图，native-image-final.trx |
| 仓库回归 | 79/79（含上述锁16） | delivery/repository-tests.txt |
| 仓库/架构/契约 | verify_repository通过 | delivery/verify-repository.txt；最终文档后再校验 |
| Explorer gate | allowed | delivery/explorer-gate.txt；G4豁免，不声称重新认证G1..G3 |
| 实机包 | 542文件匹配、全部指定行为通过 | delivery/README.md、evidence.json、截图与receipt |
| 原件/清理 | 148原件未变；6角色等全清理 | delivery/core-cleanup.json、connection-cleanup.json、windows-cleanup.json |

真实命令来自对应README：Windows solution locked restore/format/build/test；Shell/Gallery CMake Release和CTest；Setup独立solution；verify_repository；check_release_gates --target explorer-v0.5；tests/windows-setup/test_cli.py。精确SDK10.0.111，MSVC /W4 /WX /permissive- /analyze /utf-8 /MT。Root集成Windows build和Setup format0错误；未重跑输入相同的120离线用例。

先失败后修正：原生新增View测试缺少直接null guard被/analyze拒绝，补测试边界后严格编译通过；NativeLive初次三类同时登录竞争Core的2并发位，图片512行429，另外3行通过。改为共享登录并DoNotParallelize后仅重跑变动图片方法通过，原失败记录保留。生产限流未调整。错误UIA摘要可能与截图相差一帧，独立MTA读取与用户可见截图分开判断。

无Server/NAS/Android功能修改，不将未执行全项目跨平台CI、签名、G4或Android真机记为通过。最后仅复验变动文档/交接与发行引用，不重复未变运行时套件。
