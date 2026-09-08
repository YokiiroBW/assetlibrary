# V03-002 测试与证据

最终自动测试：22个标准.NET用例通过，1个真实Core用例通过；真实Explorer入口场景失败/未激活。初次2个Query测试因fixture复用JsonNode已有parent失败，已修复为DeepClone并回归通过；不把该失败归因于客户端运行逻辑。注册/ABI/静态与依赖检查另列，不重复计入.NET用例数。

工具：精确.NET SDK10.0.111、MSVC19.44.35228.0、CMake3.31.6-msvc6、Windows SDK10.0.26100.0、Windows x64 build26100。实际dotnet使用任务已有便携SDK；Python使用Codex bundled Python。没有全局框架安装。

## 已执行

```powershell
dotnet restore apps/windows-client/AssetLibrary.Windows.slnx --locked-mode
dotnet format apps/windows-client/AssetLibrary.Windows.slnx --verify-no-changes --no-restore
dotnet build apps/windows-client/AssetLibrary.Windows.slnx --configuration Release --no-restore
dotnet test apps/windows-client/AssetLibrary.Windows.slnx --configuration Release --no-build --no-restore --filter "TestCategory!=NativeLive"
dotnet package list --project apps/windows-client/AssetLibrary.Windows.slnx --include-transitive --vulnerable --no-restore --format json --output-version 1
python -I -B scripts/validate_dotnet_dependencies.py --solution apps/windows-client/AssetLibrary.Windows.slnx --packages-dir .runtime/nuget --vulnerability-report .runtime/windows-vulnerabilities.json
python -I -B scripts/validate_dotnet_source.py
cmake -S tests/windows-shell -B .runtime/explorer-proof -G "Visual Studio 17 2022" -A x64
cmake --build .runtime/explorer-proof --config Release --parallel 2
pwsh -NoProfile -File tests/windows-shell/verify.ps1 -BuildDirectory .runtime/explorer-proof
git diff --cached --check
```

全部通过。最终源码检查已经合并并使用协调提交3f791d6的扩展范围，实际扫描373个C#文件，包含新增Core/tests，未放宽60-token重复块或日志规则。Release构建0警告0错误。NuGet3项目/15锁定包通过许可证/完整性/漏洞政策；审计json列出三个项目且无vulnerable packages。Core仅BCL+已有生成SDK，生产依赖没有新增。最初WinUI还原遇到nuget.org TLS断开，后续按用户纠正移出该草稿，不属于最终方案依赖缺证据。

## 真实Core

在V03-004持有的HTTPS+PostgreSQL+真实已扫描合成目录上，通过 `ASSETLIBRARY_NATIVE_TEST_PROFILE` 指向私密连接文件执行NativeLive用例，实际4秒通过。验证精确叶指纹、login/session、100条及下一页无重复、授权详情、范围搜索、logout后401、不可见普通账号空库/强制404。不记录凭证/会话，不扫描或修改NAS。

随后最终revision重试时，统一fixture已被owner清理，私密profile不存在，测试未进入HTTP阶段。这是最新环境缺失，不是重新通过；此前真实成功证据保留为当时记录。缺profile的检测现明确返回环境未就绪，避免把本机路径包含在异常日志里。源码复核确认其后HTTP/TLS传输与查询协议未变；新增的旧翻页取消防护不在LiveCore调用链，CSRF控制字符拒绝不改变合法fixture token，并补充2个合法/非法会话header用例通过。故保留当时有效真实证据，不把正常清理后的环境不可用当业务回归，也不为commit变化重启服务。

## Explorer

isolated probe结果：CoCreateInstance、SHParseDisplayName、RootAssociationArray、RootOpenAssociation、RootAttributes、Desktop枚举、Initialize、CreateDefView、FirstChild、BindChild均成功。自动verify验证重复注册被拒、probe10秒上限与finally卸载后无键。

真实框架入口失败：两种官方CLSID形式、正确PIDL选择形式、任务新建窗口的Navigate2均出现“无关联应用”，尚无Explorer类工厂调用证据。加入正确父Desktop UPDATEDIR通知后未改变结果。第一次Navigate2缺optional VARIANT指针的RPC错误不用于产品结论；修正后重测仍在错误模态阻塞。

UIA读状态成功，输入/截图分别0x80070005/0x80070057，停止GUI输入重试。所有自有HKCU注册最终清理；未改HKLM/UAC，不重启用户Explorer。未完成的自有窗口关闭已交给协调者待桌面恢复。G2/G3/G4未有实际视图前置，未运行，不计通过。所有入口错误与成功隔离探针在summary/evidence中分开。

## 证据边界

当前没有生产Windows安装包/Host-to-Shell IPC/真嵌入用户闭环，不能进行发行可用性或50万资产体验声明。分页/响应/客户端内存上限是结构与回归证据，非容量实测；20轮和8小时Explorer稳定性保持开放。

最终标准用例为原20项通过 + 新增SessionBoundaryTests 2项通过；没有重跑输入未变的成功用例。最终仓库verify_repository通过，包含14个架构回归与21个迁移清单回归，Alpha保持blocked，SDK生成/源码/依赖和主题一致性通过。
CMakeLists在project之前显式固定Windows SDK10.0.26100.0（7652357）；重新configure后两个vcxproj均确认WindowsTargetPlatformVersion=10.0.26100.0，与此前实际构建值一致，未重复运行输入未变的C++行为测试。
