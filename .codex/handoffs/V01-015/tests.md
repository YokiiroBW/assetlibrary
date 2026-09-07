# V01-015 测试与验收记录

日期：2026-09-07。Windows x64，本任务独立worktree。产品基线从51e8a2f集成；最终运行包的精确source commit/tree、SHA-256及验收路径见 [delivery.json](delivery.json)。日志位于该worktree的 `.runtime/V01-015/`；子任务专有原生证据保留在各自handoff和runtime。未变源码的成功检查不反复执行。

## 环境与计数

.NET SDK10.0.111、PostgreSQL16.15、Node24.20.0、pnpm11.19.0、JDK21.0.12+8、锁定Gradle/Kotlin，Chromium151.0.7922.34 / Playwright1.62.1。NuGet使用共有的受锁定缓存；所有数据库和资产fixture均为本任务临时/沙箱数据，未使用个人资产。

| 验证 | 实际结果 | 原始日志/证据 |
| --- | --- | --- |
| .NET locked restore、全量format、Release build | 通过，0 warning / 0 error | final-format.log、final-build.log |
| .NET默认完整套件 | 335例；runner为311 passed、24 skipped | final-dotnet-tests.log |
| 必需真实PG套件 | Python69/69；内部ReadCore77/77和2个真实GatewayAuth数据库case均执行 | final-database-tests.log |
| 必需真实HTTPS/PG/Worker/Chromium E2E | 1/1，19.9767s；external模式，6个本次角色清理读回通过 | final-real-e2e.log；final-real-e2e/20260907T104019Z-aa6a5467/trial-e2e.trx |
| Windows父硬死回收 | 1/1，1.134s；修复前子进程存活回归确实失败 | final-worker-parent-death.log、V01-017/tests.md |
| Web浏览器 | 40/40 | web-browser-final.log |
| Web静态与构建 | 项目声明的format/lint/typecheck/build通过；lint当前复用TypeScript检查 | web-format-final.log、web-lint-final.log、web-typecheck-final.log、web-build-final.log |
| SDK生成/策略回归 | 14/14 | sdk-regression-final.log |
| AssetLink契约回归 | 21/21 | contract-regression-final.log |
| TypeScript SDK | lint/typecheck/build和5/5测试通过；audit无已知漏洞 | sdk-typescript-final.log |
| Kotlin SDK | strict依赖校验build和sdkTest通过 | sdk-kotlin-final.log |
| NuGet依赖门禁 | 11项目、46锁定包通过 | dotnet-dependencies-final.log、dotnet-vulnerabilities.json |
| Web/SDK许可证、锁/完整性、包体预算 | require-build-artifacts通过；Web audit无已知漏洞 | web-audit-final.log；原生检查输出 |
| Windows自包含包 | 独立SQL、initialize/operator/start/health/stop/restart/recover和完整性/身份拒绝 | V01-019/tests.md，delivery.json |
| 实际SMB读与路径恢复 | 6阶段，在线/恢复扫描各7项；hash/mtime不变 | V01-017/smb-verification.json |

默认.NET套件的24 skipped为23个要求真实DB/worker的ReadCore case和1个E2E；另外2个旧条件PG测试虽然runner显示passed，在缺少连接时未执行其body。它们均在上述必需真实PG/E2E环境中实际执行并通过。跨上下文共335个唯一.NET case获得执行证据，不能把311+77+69+1等重复相加。Python数据库驱动69包含下游测试调用；E2E内部多个操作阶段只计1个综合case。

配置/TLS16项、HIBP25项、ReadCore/Worker定向测试与完整套件重叠，不额外累加。TLS覆盖RSA要求、历史证书解密与缺失历史证书拒绝；HIBP覆盖2509/5000行和末行命中，仍拒绝损坏/重复/溢出/超时响应。实际互联网只作非机密固定prefix兼容性观察，CI使用受控transport，不跳过产品风险检查。

## 可复现命令

完整命令以 `tests/architecture/ci-tiers.json` 和 `.github/workflows/handoff-quality.yml` 为准。先配置项目固定版本工具，执行：

```text
python -I -B scripts/verify_repository.py
python -I -B -m unittest discover -s tests/repository -p test_*.py -v
dotnet restore AssetLibrary.slnx --locked-mode
dotnet format AssetLibrary.slnx --verify-no-changes --no-restore
dotnet build AssetLibrary.slnx --configuration Release --no-restore
dotnet test AssetLibrary.slnx --configuration Release --no-build --no-restore --logger "console;verbosity=normal"
python -B -m unittest discover -s tests/database -p test_*.py -v
python -B -m unittest discover -s tests/dotnet/AssetLibrary.ReadCore.Tests -p test_worker_lifetime.py -v
pnpm --dir apps/web run format:check
pnpm --dir apps/web run lint
pnpm --dir apps/web run typecheck
pnpm --dir apps/web run build
pnpm --dir apps/web run test:browser
pnpm --dir apps/web audit --audit-level low
python -B scripts/validate_web_dependencies.py --require-build-artifacts
python -B -m unittest discover -s tests/sdk -p test_*.py -v
python -B -m unittest discover -s tests/spikes/assetlink -p test_*.py -v
pnpm --dir packages/sdk/assetlink/typescript run lint
pnpm --dir packages/sdk/assetlink/typescript run typecheck
pnpm --dir packages/sdk/assetlink/typescript run test
pnpm --dir packages/sdk/assetlink/typescript audit --audit-level low
packages/sdk/assetlink/kotlin/gradlew.bat -p packages/sdk/assetlink/kotlin --no-daemon --dependency-verification strict build
python -B scripts/validate_assetlink_sdk_dependencies.py --require-build-artifacts
```

真实数据库必须设置 `ASSETLIBRARY_TEST_POSTGRES_REQUIRED=1`、正确的PG16工具目录、临时runtime、dotnet和Host DLL；ReadCore由现有数据库驱动提供连接。Windows父硬死用例必须指定 `ASSETLIBRARY_TEST_DOTNET`。不能用环境缺失的skip满足必需门禁。

真实E2E入口：

```text
python -I -B tests/integration/read-only-trial/run_e2e.py --execute --postgres-external --dotnet <dotnet> --postgres-bin <PG16-bin> --node <node> --web-root apps/web/dist --playwright-module apps/web/node_modules/@playwright/test/index.mjs --evidence .runtime/real-trial-evidence
```

CI使用其专有PG service；本地最终验证用自身临时PG包裹external模式，核对结束后本次随机角色全部不存在。私密连接或口令不记录在命令/日志中。原生包构建与实际生命周期参数见 V01-019/tests.md。

## 故障、权限与用户可见结果

E2E通过真实浏览器登录、登记、扫描、查看进度、浏览与全路径搜索，核对普通用户管理403、隐藏库404、空权限列表、跨源/CSRF失败。重启后原会话仍可用；恢复/凭据版本变化后旧Cookie401，新口令可登录。已有账号离线仍可登录读取；风险服务失败时创建/恢复无数据改变。

扫描失败不提交半个索引；取消/租约失效/响应丢失按状态机恢复，暂停路径不可达时保留原快照，恢复后可读。真实Worker父进程被TerminateProcess后，Windows Job内阻塞子进程及时回收。正常退出与启动失败rollback只操作确认自有的PID/image/start实例。

实际SMB仅对新建合成目录改名并恢复；probe/scan可达→Missing/exit20无complete→可达全部在73–87ms，文件SHA-256和mtime逐次一致。它不等于断开NAS主机、共享或网络，也不证明断电/大规模长时运行。证书容器正常stop零增长，强制杀进程旧实验观察到1个自有容器残留，不能写成异常退出零残留。

## 最终元数据和发布门禁

政策和交接完成后，仓库入口通过（169 architecture inputs、336 C#文件、内含14架构和21迁移清单测试）；仓库工作流54/54、Alpha政策14/14、原发行定义与默认Host进程19/19通过。日志分别为metadata-repository-final.log、metadata-repository-tests-final.log、alpha-policy-final.log、server-release-final.log。它们与此前相同套件不重复累加。19项Host/发行检查是Windows执行，不宣称Linux/SCM/Docker已验收。

6份handoff schema通过，根changed_files与相对51e8a2f的244文件diff一致；相对链接和指南UNC配置JSON有效。V01-016的历史TLS测试文件已由V01-020合并到Shared/TrialTestTls.cs，原交接路径可在原commit读回，不当成丢失实现。只读试用证据从missing implementation变为component_only；新增回归禁止把4项直接提升passed或删去E2E输入。实际`v0.1-start`返回0，完整release/write返回3；Alpha默认审计0/blocked，require-ready返回3。

独立最终复核覆盖指南、Alpha政策、TLS/RSA/旧证书解密、Gateway SET ROLE和HIBP边界，无未解决阻断；补齐RSA≥2048续期前提后重新构建最终说明包，未重跑输入未变的外网整轮。

交付源864da3b2bfeca614b2654eb380ef6074d826b6c4与根所有列出的产品构建输入无差异。独立核验371个manifest payload条目加manifest本身，共372个实际文件及ZIP精确集合/流式SHA。49,716,196-byte ZIP已复制两侧`.runtime/releases/V01-015/`并读回同一SHA-256：13caada27dec64f165da1b5bd615422e1378c580ca7faf1dce06def6f372e360。根完整性脚本最初把372总文件误当payload数，按manifest和实际目录核对为371+1后通过，未改动产物。

## 工作区与清理

两侧main仅快进同步，保留所有分支和旧提交；所有工作区逐一读回包含untracked的Git status。NAS旧Linux worktree通过原管理目录检查，不修复指针、不prune。精确HEAD/tree与读回结果记录在最终delivery.json和Git历史中。

自动批准审查拒绝本轮两处合成目录及历史ALIGN-002九个空目录的清理；未执行被拒删除，也没有绕道重试。详情在对应交接。已恢复ACL拒绝规则和SMB assets路径；忽略的产物、缓存和上述残留不影响Git clean。
