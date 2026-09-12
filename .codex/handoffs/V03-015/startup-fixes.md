# V03-015 实机启动阻断修复

修复代码：`bbe480efe9b5c388860c52d1ee1c8c829b556f60`。由root统一实际安装/Explorer验收发现并交回；本任务没有替换已安装文件、注册或操作实机UI。

## 设置窗口资源缺失

root原生上下文启动旧Settings得到exit `0xC000027B`（stowed exception），没有窗口/标准输出。本任务在自己的组件输出目录通过非GUI命令启动旧组件复现相同退出码。检查发现obj含App.xbf/MainWindow.xbf，但发布目录没有应用PRI；默认EnableMsixTooling=false使NuGet Base.targets不导入SingleProject资源工具。

只向对照发布传EnableMsixTooling=true，即生成AssetLibrary.Settings.pri。随后将该属性写入项目，并增加AfterPublish缺PRI即失败的检查。makepri详细dump验证其包含App.xbf和MainWindow.xbf。最终修复后UI由root启动；本任务不把对照构建当作GUI成功。

验证：Settings locked restore、Release publish通过；六个normal/release锁SHA256不变；makepri dump含两项应用资源；指向缺PRI输出的目标执行按预期硬失败。Setup owner已收到新增必需产物AssetLibrary.Settings.pri的建议。

依据：[微软自包含部署](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps)、[项目属性与初始化](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/project-properties)，以及实际Microsoft.WindowsAppSDK.Base 2.0.4的SingleProject import条件。

## 通知辅助进程COM模式冲突

root原生通知辅助进程原代码退出2。隔离.NET10 probe只执行CoInitializeEx而不触发Shell，复现默认main为MTA时请求STA返回0x80010106；显式STA线程返回S_FALSE(0x1)。这是官方定义的RPC_E_CHANGED_MODE，不能忽略继续执行。

通知现在在显式STA线程上初始化、解析固定root并发送通知；内部等待2秒，原父进程3秒隔离保留。添加安全stage/hresult（initialize_sta、parse_root、dispatched、timeout/退出）诊断，不含账号、地址或原始异常。辅助进程非零退出不再静默。

新增测试从MTA调用实际通知线程路径，只执行初始化probe，避免触发注册或真实Shell。该测试1/1通过，相关Host/test构建0 warning/error；源码重复/敏感日志检查通过。root负责native SHParse阶段与视图刷新验收。

依据：[CoInitializeEx官方返回值与线程规则](https://learn.microsoft.com/en-us/windows/win32/api/combaseapi/nf-combaseapi-coinitializeex)。
