# dev-230 只读构建条件

按root追加授权，SSH只读查询本机Unix Docker socket（不沿DOCKER_HOST指向其他服务）、工具路径、四个基础镜像和已有AOT工具镜像元数据。没有pull/build/create/run/exec容器，没有NAS操作。

- Python3.12.3；Docker Server26.1.4，需要sudo -n访问本机daemon。
- 宿主PATH有docker/git/cc，没有dotnet或clang；构建器本来也不依赖宿主这两个工具。
- 已缓存assetlibrary/v03-005-aot-tools:izfgezzr，ID sha256:6637af43221d16bcad07c04ddb47421b42cbdbcba642f4d8c52a53fa61e03ad5；镜像历史recipe包含10.0.111与clang，未启动镜像核二进制。

四个linux/amd64基础镜像均在本机：

| 镜像 | 当时RepoDigest |
| --- | --- |
| debian:bookworm-slim | debian@sha256:88200866dfff7ea7f5cbcb6ec7c8a701889efe6fe859fe64d6990e4b07ea4171 |
| node:24.20.0-bookworm-slim | node@sha256:ba849c60be29959425b8734d57b8b4b7d56f98edd9504c9af091d5281095a71e |
| python:3.13-slim-bookworm | python@sha256:ed86c82274b3c69b52fb5820f358f0bd7df0b603332063cb5c6e32bd220c3e6e |
| postgres:16.15-bookworm | postgres@sha256:bb3e1a57e5407e0a5280b4211980a5e537f4abd234a87014ac979849a78dd825 |

这些是只读快照，正式builder仍执行pull并记录该次解析的immutable digest。Dockerfile在自己的构建层下载固定SHA512的SDK10.0.111并安装clang/zlib开发依赖，不直接FROM上面的外部tool-image；SDK下载层可命中Docker缓存，clang位于server之后可能随源码重建。还需registry、Debian apt、Microsoft SDK、NuGet、npm/pnpm通路，不能因为宿主有工具镜像就承诺完全离线构建。

## 正式执行（由root，尚未由本任务运行）

先进入合入Supervisor、Core socket适配、Root的Linux AOT锁及本打包改动的最终干净Linux Git checkout。以下wrapper只使用dev-230本机Docker socket，放忽略的.runtime内，不污染Git源码：

```sh
mkdir -p .runtime/sandbox-storage/V01-022/tools
printf '#!/bin/sh\nexec sudo -n /usr/bin/docker --host unix:///var/run/docker.sock "$@"\n' > .runtime/sandbox-storage/V01-022/tools/docker-local
chmod 700 .runtime/sandbox-storage/V01-022/tools/docker-local
python3 -I -B scripts/build_nas_deployment.py \
  --docker "$(pwd)/.runtime/sandbox-storage/V01-022/tools/docker-local" \
  --output-root ".runtime/sandbox-storage/V01-022/same-nas-final-$(git rev-parse --short=12 HEAD)"
```

输出目录必须新且不存在；如同commit重复运行，显式选择新的后缀，不覆盖旧证据。也可给--docker已有等效wrapper的绝对路径，不能传一个带空格的sudo命令字符串。脚本顺序构建core/setup/image并tag同次PG镜像、校验全部native payload和同源码两份decoder、保存四镜像与完整交付清单；成功仍仅built_not_deployed。NAS只需要加载交付包，不依赖dev-230在线运行。
