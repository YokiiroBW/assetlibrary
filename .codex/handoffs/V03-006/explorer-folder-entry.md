# 官方文件夹CLSID入口对照

状态partial，真实Explorer扩展激活未成功。主协调批准的单项对照依据微软[Specifying a Namespace Extension's Location](https://learn.microsoft.com/en-us/windows/win32/shell/nse-junction)：普通文件夹名附加点号和扩展CLSID可作为Shell命名空间入口。本文只测试该命名方案，不使用另一desktop.ini方案、系统/只读属性或NTFS重解析点。

在本worktree的`.runtime/sandbox-storage/`创建不存在的空`AssetLibraryProof.{4FF8301D-2E73-4D49-9FE5-868D5F1EA302}`目录。Attributes=16/Directory、LinkType=null，内容为空。DLL仍为不可变0174DB9B…，GUID和原HKCU注册脚本未改，未重建或修改生产代码。

按前后窗口集合和新鲜窗口状态确认2033652为本任务Ctrl+N新建窗口。02:06:55.8364355Z完成注册，600秒guard开始；02:07:32.557Z通过实际Ctrl+L/输入普通绝对路径/Return打开目录。02:07:54.551Z真实视图显示“此文件夹为空。”、0个项目；标题和面包屑隐藏CLSID后缀，但刷新和搜索的UI名称仍含完整目录名。只有名称格式化不能证明COM绑定。

02:07:55.0388617Z注册仍两键true。五个Explorer PID6284/17944/24644/24772/26128的模块读取均成功，均无任何路径的proof DLL；当前副本旁无trace。没有观察到Explorer的Factory/Initialize/CreateDefView或示例项，因此结果属于“普通空目录/未观察到激活”，不是G1通过，也不主张已解释所有可能的内部Shell路径。

[视图截图](explorer-folder-entry/empty-view.png)仅裁剪原始截图左侧导航和窗口外框，保留本任务地址栏、完整内容区和空视图提示；原图留在自有.runtime。未改写图像内容。时间线、只读模块结果、注册与清理、600秒guard源文本和hash见[证据索引](explorer-folder-entry/evidence.json)。

stop文件触发guard的finally，02:08:40.6786039Z两注册键false、guard正常exit0。新鲜确认后关闭2033652，最终窗口列表仅用户原1247028与Codex。空目录在独立核验完整路径、无reparse和零子项后以LiteralPath非递归删除。最初组合动态路径删除命令被自动策略拒绝，改为先独立只读核验、再精确字面路径删除成功。没有关闭/重启用户Explorer、触碰用户Desktop/资产、修改HKLM/UAC/策略或部署安装。

本对照没有源码改动，不重跑已通过的构建、root-bind或图片隔离矩阵。复用不可变proof、owner保护注册脚本和computer-use受支持UI；没有新框架、依赖、业务逻辑或共享契约。目录与注册仅存在于有期限测试期间，复杂度为固定五个Explorer进程的只读模块枚举，不提供50万资产性能或长期稳定性结论。

目前三类入口（Desktop发现、shell URI、文件夹CLSID）均未见Explorer加载，而独立Desktop根绑定成功。下一步需要现有工具对精确CLSID的COM/注册读取/加载阶段提供新观测；不得把更多无观测注册、更换GUID、修改DLL接口或放宽系统策略当作诊断。
