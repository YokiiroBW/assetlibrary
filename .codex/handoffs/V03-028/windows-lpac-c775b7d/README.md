# Windows 旧 LPAC 图片路径定向回归

2026-09-13，协调器明确要求对不可变源码 `c775b7d2b221479510f958f7e302a676b024e778` 验证旧 Windows 路径。没有产品源码、项目、锁、安装、Explorer 注册、用户配置或 NAS 变更，没有 GUI/G4 操作。

## 结果与边界

真实 `NativeAotDecoderRunsInsideLpacAndReturnsARealThumbnail` **1 项通过、0 失败、0 未执行**，runner 汇总耗时140 ms（TRX duration字段127.18 ms）。仅运行该精确过滤项。测试使用原 `WindowsImageProcess` 创建临时 LPAC 和 Job，Worker 完成原生隔离检查后 Ready；合成 1024×600 PNG 得到真实 512×300、8-bit PNG，Worker 退出 0。现有测试验证 PNG 签名、IHDR 尺寸、输出长度及清理。

这个结果证明当前 SetImmutable、128MiB GC 地址预留和 PNG chunk guard 集成后，旧 Windows LPAC 路径仍正常出图。GC commit 上限仍为64MiB。它不增加最大图片、CPU/内存故障或完整 Windows 隔离门禁证据，也不替代 NAS 验收。

## 来源和构建

- `git archive --format=zip` 从指定 commit 导出到本工作区 `.runtime/windows-lpac-c775b7d/source.zip`，独立解压至 `source`，不复用先前 Worker 或测试二进制。
- 源码 ZIP SHA256：`8da747d1c6f6ad871db7e2f87e1b8b178af125f318ab2d9f49113cd9179cc4b7`。
- 执行后对 ZIP 内3564个文件逐项比较解压文件 SHA256，0项变化；见 `source-integrity.json`。构建新增的 obj/bin 位于独立副本。
- SDK10.0.111 / runtime10.0.11，Windows10.0.26100 win-x64；MSVC安装14.44.35207，cl19.44.35228.0，link14.44.35228.0；详见工具信息。
- 使用原 `locks/AssetLibrary.ImagePreview.Worker.win-x64.lock.json`，显式 RuntimeIdentifier、PublishAot、SelfContained 和绝对 AssetLibraryReleaseLockRoot，`--locked-mode` restore 后 `--no-restore` publish。锁 SHA256 前后均 `bd2342dac988629533228d720d61c2eb3de95515d34cd185cb1ba18d6c67a76a`。
- NativeAOT 实际生成原生代码，未禁用原项目 strict/AOT/trim 诊断。测试项目及其 Core/AssetLink 依赖 Release 构建0警告0错误。完整实际脚本保留为 `commands.txt`。

产物保留在自有 `.runtime/windows-lpac-c775b7d/publish` 供复核，未安装。EXE为1479168B，SHA256 `28d2ffb41ecc13c89578ae4da065b7667f189223d5a30d338a8f56a38113f7f2`；libSkiaSharp.dll为12274488B，SHA256 `09fb4b1afee9810f127672baff0e050ed076b6d889b90adec7271914c3894125`。许可证和完整第三方 notices 随产物保留，全部清单见 `evidence.json`。

## 实际测试与清理

脚本仅对自己的子进程设置 `ASSETLIBRARY_IMAGE_WORKER_TEST_EXECUTABLE`，指向本次产物。真实命令为：

```text
dotnet test tests/dotnet/AssetLibrary.Preview.Tests/AssetLibrary.Preview.Tests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName=AssetLibrary.Preview.Tests.WindowsImageProcessTests.NativeAotDecoderRunsInsideLpacAndReturnsARealThumbnail --logger trx;LogFileName=windows-lpac-c775b7d.trx --results-directory <owned-stage>/results
```

实际 PowerShell 引号及所有前置命令以 `commands.txt` 为准。测试开始/结束为11:02:52.114Z/11:02:53.172Z；TRX确认唯一用例确实执行并通过，没有 Inconclusive。

原测试在 `await using` 退出后检查 profiles 无文件、无 `.owner*` 和 `.lease`；原 Dispose 在进程确认退出后调用 `DeleteAppContainerProfile`，失败会使测试失败，随后删除自己的目录/日志。测试外再读回独立 `source/.runtime/sandbox-storage/V01-004/tests`，执行前0项、执行后递归0项；查询 Worker 进程并只匹配本 stage 可执行路径，剩余0个；临时测试环境项已移除。上述数值写入 `evidence.json`。没有执行额外权限设置、清理其它 profile、安装或卸载；未声称做过独立全局 AppContainer 清单比对。

本补充只有交接文档和原始证据，建议单独合入，不重复合入此前代码或 Linux 测试提交。
