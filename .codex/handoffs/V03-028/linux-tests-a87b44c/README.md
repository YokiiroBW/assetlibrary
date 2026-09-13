# V03-028 开发 Linux 完整测试证据

2026-09-13，仅dev-230，不是NAS验收。测试源码固定为协调提交a87b44c7e950b53e44f0e41f6702d1d892653eeb，通过git archive导出；本地与远端tar SHA256均为f6ff80fafe97fe17553283c0b7f1515afd6cb8a03a5f50cce3da20b4d2fa76a9。没有修改源码。

独立目录/tmp/assetlibrary-v03-028-linux-tests，不读写root的source-buffer-fixed构建目录、obj/bin或NuGet卷。使用目录内自己的.runtime/nuget与dotnet-home；没有共享命名卷或拉取镜像。该独立副本保留用于复现，两个测试容器已清理。

## 环境与实际执行

- 现有镜像sha256:6637af43221d16bcad07c04ddb47421b42cbdbcba642f4d8c52a53fa61e03ad5，linux/amd64。
- 内核Linux6.14.0-28-generic，SDK10.0.111，测试容器UID/GID0。
- 容器标签io.assetlibrary.task=V03-028与精确source标签；实际inspect内存2147483648B、NanoCpus2000000000，无OOM。
- 只绑定新源码副本到/src；内置SDK执行，未安装新工具链或改NAS。

第一容器9104d8c0fb417cbf033ac69b34e4521837f503c26c20c5884bdc95cd07471591运行：

```sh
dotnet restore tests/dotnet/AssetLibrary.Preview.Tests/AssetLibrary.Preview.Tests.csproj --locked-mode
dotnet build tests/dotnet/AssetLibrary.Preview.Tests/AssetLibrary.Preview.Tests.csproj --configuration Release --no-restore
dotnet test tests/dotnet/AssetLibrary.Preview.Tests/AssetLibrary.Preview.Tests.csproj --configuration Release --no-build --no-restore --logger 'trx;LogFileName=linux-preview-tests.trx' --results-directory /src/.runtime/linux-results
```

完整发现131项：92通过、0失败、39未执行，约8秒。Core与测试构建0warning/0error。RealLinuxPeerUsesEffectiveUserAndRejectsUnconnectedSocket实际通过：本进程UID0的真实SO_PEERCRED正控及未连接socket负控，不使用假peer代替该项。

第二容器fcd4b0dd346fe3b8cd90fbfdfbf248a3bca3328158fb2b5e0ca11c33b1acd709在第一容器退出后复用同一独立副本/私有缓存，补齐先前缺Host DLL的一个测试：

```sh
dotnet restore services/core-server/Host/AssetLibrary.CoreServer.Host.csproj --locked-mode
dotnet build services/core-server/Host/AssetLibrary.CoreServer.Host.csproj --configuration Release --no-restore
export ASSETLIBRARY_TEST_DOTNET="$(command -v dotnet)"
export ASSETLIBRARY_TEST_HOST_DLL=/src/services/core-server/Host/bin/Release/net10.0/AssetLibrary.CoreServer.Host.dll
dotnet test tests/dotnet/AssetLibrary.Preview.Tests/AssetLibrary.Preview.Tests.csproj --configuration Release --no-build --no-restore --filter FullyQualifiedName~ImageSourceBrokerTests --logger 'trx;LogFileName=linux-source-broker.trx' --results-directory /src/.runtime/linux-results
```

Host构建0warning/0error；补充1/1通过160ms，验证真实源子进程、字节/hash/mtime复验及句柄释放。未重复运行原92个通过用例。

## 汇总和未执行边界

按类名及完整用例名合并两份TRX，仍为131个唯一用例：**93通过、0失败、38未执行**。剩余34项Windows专属、1项非Linux平台负控、3项要求真实PostgreSQL/HTTPS Core夹具。完整名称与原因见test-summary.json，不将缺夹具算作通过。

这是开发Linux测试，不代表NAS5.10.55+的UID1654到UID0容器接合、MEMLOCK/内存边界、真实图片或生产部署已完成。G4未做。

## 清理

两个容器均实际ExitCode0、OOMKilled=false。删除前复验V03-028标签及Running=false，再按上述精确ID执行docker rm，随后docker container ls按精确ID查询为空；命令整体返回0。记录见container-state.json、source-container-state.json、container-cleanup.log。未删除共享镜像，未操作其他容器、NAS或root源码目录。

commands.json、source-broker-commands.json为阶段退出码；TRX、构建/测试日志、环境文件均原样保存。test-summary.json由TRX解析生成。

文本证据由Git管理换行；evidence.json的artifact哈希按CRLF规范为LF后计算，避免Windows checkout换行造成假差异。原源码tar哈希按原始字节计算。交接追加后verify_repository通过；没有再运行测试套件。
