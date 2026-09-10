# 固定11字段独立只读工具

已按主协调要求完成下一周期使用的只读读回工具，且只在当前未登记状态运行一次expected-missing验证。没有调用/dot-source guard，没有注册、GUI、attach、Explorer重启或系统策略变化。

工具位于 `.runtime/explorer-registry-readback/`，以已审[11字段契约](registry-read-preparation/official-field-contract.json)为唯一官方字段来源，固定SHA `63CC68C84E2E49B4AE9078E7A6EE474B795461BE68E464AA765E55E1E2A41A13`，路径和值原样使用。另读四个固定Owner和BA16在本独立reader的HKCR合并视图中五个字段，共20条记录；后者明确不是Explorer进程内读回，不推断宿主看到了同一视图。

每项分别记录key/value是否存在、实际Win32状态、类型、字节数和值。只用RegOpenKeyExW的QUERY_VALUE|WOW64_64KEY、RegQueryValueExW和RegCloseKey；先取metadata，再读取不超过4096字节。DWORD从实际四字节规范化为无符号`0xXXXXXXXX`；REG_SZ必须正确终止且无嵌入null，真实空默认字符串与missing-value分开。类型不支持时保留类型而不读取任意payload，大小/类型变化或非零返回不重试、不填入预期值。读取为逐项时点而非原子快照。

| 文件 | SHA256 |
| --- | --- |
| FixedRegistryValueReadback.cs | 5A37EAB04575B4E2DEEBF28AB762E4B435ED7BCE32827BBCEC477F6480EDB379 |
| read-fields.ps1 | F282B79AA0D121B4E0A60537E8A9603FAA25604145FF7DDF59597DD8946E3453 |
| run-readback.ps1 | BA62DE638654AFAB91247E11810200A439876DB74E547888A718C3C04ECE587C |

实际验证：C# Add-Type编译、两份PowerShell AST通过；`run-readback.ps1 -Mode expected-missing -Label unregistered-baseline`在独立隐藏reader中运行，外部10秒上限，exit0、无超时、stderr空。11官方+4Owner+5外部合并字段全部为`missing-key`，原生OpenStatus=2，RegistryWrites=0。已有键中缺失值、真实DWORD/字符串读取、错误类型/大小/竞态与超时清理分支仍只有源码审查，本轮没有造登记或测试键来补造成功证据。

下一经协调的有效注册窗口内，可用同wrapper的`-Mode expected-registered -Label registered-before`和`registered-after`分别读回；新标签保护旧记录。必须保留原F298/323CD843注册字段、600秒和cleanup，本工具不建立注册，也不根据字段结果自行扩大实机动作。wrapper超时时只能终止其新建的独立reader，并保留stdout/stderr/返回码。

[证据索引](registry-field-readback/evidence.json)含三份源码、README和一份明确标注的脱敏结果；原始输出仍在runtime，预期DLL路径的当前用户profile前缀仅在副本替换并记录原始/副本SHA。复用既有字段契约和有界独立进程模式，没有产品代码、公开契约、依赖或权限改变。此准备不计新observer正常/取消、实机或G1通过。
