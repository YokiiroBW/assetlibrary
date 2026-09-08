# Windows 原生适配器测试

普通测试使用 HTTP handler 边界，不触碰真实资产。覆盖 HTTPS origin/精确叶指纹/主机名/证书有效期、分页范围/响应关联/未知消息/uint64溢出、HTTP拒绝正文卡住、取消与超时、陈旧响应、权限变化清空和有界页面替换。

```powershell
dotnet test apps/windows-client/AssetLibrary.Windows.slnx --configuration Release --no-build --no-restore --filter "TestCategory!=NativeLive"
```

`NativeLive` 是独立明确的真实服务端验证类别，不把环境缺失记为通过。环境变量 `ASSETLIBRARY_NATIVE_TEST_PROFILE` 指向隔离支架生成的私密JSON文件，含 origin/certificate_sha256/account_name/password/invisible_account_name/invisible_account_password/library_id。不要把内容、凭证或Cookie写进命令参数、版本库或日志。设置路径后执行：

```powershell
dotnet test apps/windows-client/AssetLibrary.Windows.slnx --configuration Release --no-build --no-restore --filter "TestCategory=NativeLive"
```

真实测试使用 Core+PostgreSQL 支架中的合成目录，验证登录、session、100条跨页、不重复条目、目录搜索/详情、退出撤销，以及无权限账号的不可见库与强制404；不启动扫描或改变原文件。支架由统一owner启动和关闭，测试本身不持有服务生命周期。
