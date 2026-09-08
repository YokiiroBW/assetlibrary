# V03-007 — 独立图片 Worker 打包接线只读检查

2026-09-09，按 V03-005 协调请求检查。本报告记录当前代码及建议，不修改根级发行脚本、配置、门禁或 NAS。核查基线为本任务 25a7bed 与协调已批准的 shared wire；Windows 原生故障/COM 矩阵仍独立待验。

## 当前缺口

| 入口 | 代码证据 | 用户可见后果 |
| --- | --- | --- |
| NAS Docker | `infra/docker/nas/Dockerfile` 的 server stage 仅 restore/publish CoreServer.Host；core stage 只复制 `/out`，没有独立 Worker 的 NativeAOT 发布或复制。构建层也未安装 NativeAOT 所需 clang/zlib 开发包。 | 新 Core 可有图片路由，但镜像没有 decoder。 |
| 通用 Docker | `infra/docker/Dockerfile` 同样只发布 Host。当前入口为诊断 Host，未启动只读 trial。 | 既不能以容器健康为图片成功证据，也未交付图片 Worker。 |
| Windows/Linux 原生 | `scripts/build_server_release.py:build_cold_run` 只发布传入的 Host project；`eng/server-release-policy.json` 的 `host_project` 只有 Host。Host csproj 只引用 Core，Core 不应加载 Skia。 | solution 编译成功不会把独立进程及 native Skia/notices 放进归档。 |
| Windows 只读试用 | `scripts/build_read_only_trial.py` 复用上述 Host 发布，复制到 `host/`，另复制 Web/迁移/启动脚本。 | ZIP 缺 Worker，原试用启动过程未配置 Worker。 |
| 启用 | `TrialImagePreviewConfiguration` 只读环境变量 `ASSETLIBRARY_IMAGE_PREVIEW_WORKER`；`ImagePreviewRuntime` 对缺省、相对、缺失或重解析的可执行路径保守返回 unavailable。全仓库 `infra`/发行脚本无该变量设置。 | 现有包的真实图片请求返回 503 `preview_unavailable`；手工测试显式设置变量的成功不能推断已随包启用。 |
| 来源绑定 | release policy 的 `issuance_inputs` 包括 Core，但未包括 `services/worker-supervisor/ImagePreview`。 | Worker 源/锁/独立 native 输入必须纳入发行来源摘要，不能只随意复制已构建文件。 |

Docker 忽略规则排除了 bin/obj/.runtime，不能依赖开发机的已发布文件意外进入 context。这一点应保留。NAS Compose 已有 nonroot、cap_drop ALL、no-new-privileges、init 与只读根；无需放松。当前 `/tmp` 64MiB 承担 source broker 的稳定副本：两个最大 32MiB 输入同时处理已接近上限，应在实际容器验证空间不足的有界失败，是否调整大小由 root 据实测决定。

## 建议的最小受控变更（交 root 裁决）

1. 在发行构建中独立发布同提交、同平台的 Worker 到 `workers/image-preview/`，保留 Host→Core 的现有依赖方向，不增加 Core→Skia/Worker ProjectReference。Worker 的普通 lock 与两个 RID/AOT locks 继续分开；把 Worker 目录纳入发行输入摘要及严格归档文件清单。
2. NAS 在现有 exact SDK 的构建层添加 clang/zlib 开发包，独立 linux-x64 NativeAOT restore/publish，再复制完整发布目录到不可写 `/app/workers/image-preview/`。运行层仅保留实际 native 运行依赖、可执行文件、Skia 动态库、MIT/THIRD-PARTY-NOTICES；符号仍作为构建诊断产物分离。通用诊断镜像是否加入 trial 另行裁决，不能因本功能擅自改变其模式。
3. 仅在平台证据允许的受控 NAS trial 配置设置绝对路径 `/app/workers/image-preview/AssetLibrary.ImagePreview.Worker`。Windows trial 启动器由实际包根算出同目录 `.exe` 的绝对路径，并仅传给所属 Host 子进程，不修改机器全局环境变量。缺省/旧包保持现有 503 语义。
4. 原生冷发布流程当前循环两个 RID；Worker 的 NativeAOT 应在对应 OS/toolchain 上构建，不能把跨 RID Host publish 的成功当 NativeAOT 跨 OS 发布能力。使用现有平台矩阵组织，并把实际发行目录的来源/内容摘要绑定到同一提交。缺平台工具不标通过。
5. 归档清单必须包含 Worker native 库和许可证声明；校验 exact locks、文件 hash、可执行权限和无符号/原件/秘密。升级及回滚保留 Host/Worker 同版本组合，不能复用未绑定来源的外部 worker 路径作为正式包证据。

## 真实验证入口

独立构建命令已经由本任务 Windows 与 root Linux 使用。以下占位路径须由调用方替换为自有目录：

```text
dotnet restore services/worker-supervisor/ImagePreview/AssetLibrary.ImagePreview.Worker.csproj -p:RuntimeIdentifier=<win-x64|linux-x64> -p:PublishAot=true -p:SelfContained=true -p:AssetLibraryReleaseLockRoot=<absolute-worker-lock-directory> --locked-mode
dotnet publish services/worker-supervisor/ImagePreview/AssetLibrary.ImagePreview.Worker.csproj -c Release -p:RuntimeIdentifier=<same-RID> --self-contained true -p:PublishAot=true -p:AssetLibraryReleaseLockRoot=<same-lock-directory> -p:DebugType=None -p:DebugSymbols=false --no-restore -o <owned-worker-output>
```

发行改动后复用实际入口，不创造空跑层级：

- `python -I -B scripts/verify_repository.py`
- `python -I -B scripts/validate_server_release.py`
- `python -I -B -m unittest discover -s tests/release -p test_*.py -v`
- `python -I -B scripts/build_server_release.py`，随后 `python -I -B scripts/validate_server_release.py --require-artifacts`；只有 root 修改并审查其 Worker 集成后才能作为新包证据。
- NAS 使用现有 `python -I -B scripts/build_nas_deployment.py --output-root <new-owned-V01-022-directory>`，构建结果仍只是 built_not_deployed；实际部署/回滚与 NAS 目标内核验证归 root。
- Windows 试用使用现有 `scripts/build_read_only_trial.py` 的实际必需参数 `--dotnet`、`--node`、`--pnpm`、`--output-root`，不得把普通 Host 包或 Explorer 之外的入口冒充 Windows 内嵌完成。
- 从最终包里的实际 Worker 路径启用同一 `tests/integration/native-clients/serve.py`，配合 `LiveImageEndpointTests` 的 `ASSETLIBRARY_PREVIEW_EXPECT_AVAILABLE=1`；缩略图/预览真实 200，格式/损坏/超限、鉴权与 source-change 负例按已有组覆盖。缺 Worker 的 503 另测，不能替代成功路径。显式停止后验证原件 hash/mtime 与全部所属服务/临时状态回收。
- Linux 还须在实际打包后、实际 nonroot/cap0/内核上执行 Worker README 现有 isolation/native-memory/thread/CPU 同码探针；Windows 执行 V03-006 的实际隔离与故障矩阵。开发 Linux 6.14 证据不替代 NAS 5.10。

本次为只读打包检查，未执行发行构建、修改运行环境或开启新平台门禁。

## 后续受控实现（d18b224，非部署完成）

root以f673a12授权NAS Dockerfile/Compose/README、build_nas_deployment.py及专属测试。d18b224已补NAS同源码/RID锁AOT发布、固定6文件、镜像内源/hash记录和默认空env透传；builder创建不启动的临时检查容器，用实际core image ID提取并验证内容，清单绑定commit/image/锁/产物。故本报告前述NAS缺引擎的源码接线已修正，但实际Docker构建与目标NAS隔离/启用尚待root完成。通用Docker、原生/Windows trial以及它们的issuance policy保持未修改。

当前Windows机器没有Docker CLI。新测试11项中9通过、2POSIX权限缺本机平台；真实root Linux runner需补11/11。整个既有tests/release suite52项，47通过/0失败/5未执行：新增2POSIX及既有3原生试用包/PG环境。未把合成ELF夹具当成实际NativeAOT或镜像构建成功。

root后续平台补证：开发Linux干净检查点0f4d0c68cf693a854b4dde1932cbe1c623b1b6c2执行本新增包测试11/11、0skip（38ms），已覆盖POSIX权限；实际同提交builder已启动，结果仍待root验收。
