# V03-029 测试记录

## 本机命令

```powershell
python -I -B -m unittest discover -s tests/repository -p 'test_nas*.py' -v
python -I -B -m unittest discover -s tests/release -p test_nas_image_preview_package.py -v
python -B -m unittest discover -s tests/repository -p 'test_nas*.py' -v
sh -n infra/docker/nas/nasctl.sh
python -I -B scripts/verify_repository.py
python -B -m unittest discover -s tests/repository -p 'test_*.py' -v
```

Python使用Codex primary runtime；POSIX shell为bundled Git usr/bin/sh.exe。本机Windows的精简Git缺少sha256sum，CLI测试仅在临时fake-bin内用同一Python执行真实SHA256校验；启动前断言docker解析到该fake-bin，不能落到真实Docker。Linux直接使用系统sha256sum。所有包/控制文件都是系统临时目录夹具。

## 已通过

- 新NAS suite：配置/健康分开、镜像/源码/部署身份、PID1/no init、精确4cap、NNP、readonly、namespace/端口/设备、512MiB/CPU shares、唯一IPC/无敏感挂载、环境限制的正反控制。
- 四镜像单source构建编排、Supervisor AOT restore/publish同锁属性、无SDK进入image、Core/image两份decoder文件一致。
- Supervisor ELF/许可证/SourceRevision/锁/hash完整性，错误平台/缺失/额外/篡改拒绝；检查容器从不启动，错误仍清理自身。
- 真实sh+假Docker：image健康失败在Core启动后明确报错/停止image、默认不启用、持久socket及显式空覆盖、重复空键/任意路径/同时WORKER/控制文件hash漂移/外来卷阻断；完整容器ID防止partial-ID误删。
- 新suite原15/15过16.34秒；完整ID追加定向1/1过1.93秒。非-I CI入口wrapper过17.9秒，其真正-I子suite执行16项。
- 旧release suite保留12项，10通过，2个POSIX executable/readability用例本Windows跳过，不能当Linux权限证据。
- NASCLI shell语法通过。

最终verify_repository.py通过（530个架构输入、21个迁移测试、14个架构规则测试，源/依赖/主题检查通过，Alpha审计仍blocked）。

最初按仓库记录的非-I完整repository discovery运行75项：本NAS wrapper通过，但未修改的test_server_packaging_foundation导入validate_server_release.py触发其既有-I保护，74通过/1导入错误；不删除或伪造保护。改用真正 `python -I -B -m unittest discover -s tests/repository -p 'test_*.py' -v` 后95/95通过（24.67秒），包括新增16项。Root已接管workflow/ci-tiers统一改-I的共享入口，子任务未改这些文件。

## 未执行与跨任务证据

未执行本任务实际Docker四镜像build、decoder、NAS连接/容器更改、真实Core/PG/原件或发布。LinuxAOT和实际NAS平台/旧宿主PID回收由root执行，不复制成此任务的本机通过。

只读dev-230检查是root追加授权：Python3.12.3、Docker26.1.4需sudo、四基础镜像amd64缓存、AOT工具镜像recipe含SDK10.0.111/clang；未运行任何pull/build/创建容器。详见build-host.md。
