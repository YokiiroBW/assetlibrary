# Linux 原 seccomp 无参数路径：6项图片回归

2026-09-13协调器授权的独立有界回归，仅在dev-230运行既有镜像，没有构建/pull镜像、改产品源码、操作NAS/Windows安装/Explorer或执行其它套件。

**JPEG、PNG、WebP各profile0/1，共6/6通过。** 每项先读精确Ready24B，再发送旧请求24B+合成原图并关闭stdin提供EOF；验证Success3、匹配profile/length、PNG全部chunk CRC、IHDR位深/颜色、顺序、IEND和尺寸。profile0均512×341，profile1均1600×1066。Worker/container与Docker CLI均exit0，无OOM、无父超时、stderr0字节。六份实际派生PNG随证据保存，下载后SHA再次与原报告比对通过。

## 固定来源及容器边界

- 已有镜像标签 `assetlibrary/nas-core:c775b7d2b221479510f958f7e302a676b024e778`，实际按不可变ID `sha256:4b5eff7925e192f99c3ebdd269dc86060c6542afc7b32b3c61cd88ecc975feac` 创建，OCI revision已核实；dev-230内核6.14.0-28-generic。
- 对协调器final `7f092904dbadcf1276bb9daf5b565946ecccbdad` 比较ImagePreview下全部14个.cs/.csproj Git blob，与c775完全相同；见 `source-identity.json`。不把未执行的final整包二进制冒充本次测试镜像。
- 六个容器均使用label `io.assetlibrary.task=V03-028` 和本轮唯一worker-test/case标签，user1654:1654、cap-dropALL、NNP、read-only、networknone、memory536870912B、init；无挂载、tmpfs、额外设备或权限。保留Docker默认seccomp，没有unconfined覆盖。
- 固定entrypoint `/app/workers/image-preview/AssetLibrary.ImagePreview.Worker`，inspect检查Cmd与Args均空；没有 `--container-decoder` 或任何probe参数。原 `ImageWorkerIsolation` 因而选择 `LinuxImageIsolation.Enter`，它成功后才发Ready。
- 从第一个自有容器启动前只读复制Worker和libSkiaSharp做哈希：Worker2302600B，SHA256 `640b236ba3b0c2fde1192fb5dd0f94a760ad8ed60c402138b3994b741bb01737`；libSkiaSharp.so11756440B，SHA256 `dc4dfcc90d319e9fd9edf42106bf1a9c5d9f438d5008c53ddcb4daf8e2340ad1`。没有增加第七个容器或图片用例。

## 复用与执行

自有 `/tmp/assetlibrary-v03-028-linux-seccomp-c775b7d` 保存runner、锁定源码的 `tests/integration/native-clients/verify_nas_worker.py` 和原合成corpus。只重写该工具Runner的容器创建策略及六项清单，复用原corpus身份/SHA/mtime校验、24B/EOF收发、全PNG CRC/尺寸校验、有界输出/超时、实际exit检查、按归属清理与存在性核查。未复制一套PNG或wire实现到产品。

实际运行 `python3 -B run.py`；其Docker入口是本轮小脚本 `#!/bin/sh` 加 `exec sudo -n docker "$@"`。完整runner保留 `runner.py.txt`，六次create参数在 `evidence.json`。复用helper来自c775归档，Windows导出CRLF使文件字节hash与Git LF blob不同；仅换行正规化后内容一致，两个实际hash均记录，没有改helper代码。corpus使用原10项manifest验证固定身份，实际只发送landscape.jpg/png/webp各两次。

运行窗口11:30:25.776Z至11:30:28.637Z；每case含Docker创建/检查/清理0.494至0.751秒，此数字不是单独decoder CPU耗时。结果详情包括Ready字段、响应状态、尺寸、字节数、PNG SHA、容器ID与开始/结束时间，见原报告 `evidence.json`（由原runner的result.json原样复制）。本工具产生JSON证据，不伪造TRX。

## 清理与适用结论

六个容器均先inspect确认固定image/唯一标签、Running=false、ExitCode0/OOMKilled=false，再精确删除并逐ID查询不存在。最后再次独立查询 `docker container ls --all --quiet --no-trunc --filter label=io.assetlibrary.task=V03-028`，exit0且0项，见 `cleanup-readback.json`。原corpus所有内容SHA与mtime前后相同；只保留自有临时证据和读取的二进制供复核。

这些结果证明同码旧无参数seccomp图片路径仍能正常出图。没有运行syscall拒绝探针，没有把Docker已有io_uring拒绝归因于新filter，也不新增CPU/内存最大边界或NAS交付完成结论。本补充提交只有交接和实际证据。
