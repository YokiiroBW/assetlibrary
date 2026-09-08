# V03-002 交接 — partial

实现提交：`31803b1ada5c1d9becec4308024387c2b28f0812`；测试收敛提交：`4f46153b0327b570c21db66dd769d5ff1e9cad74`、`5464f101cc7db11a3e4915b77a5252fe36df8e7a`。分支 `codex/v03-002-windows-native-read-client`，独立worktree `C:/YOKI/Codex/AssetLibrary-worktrees/V03-002`。

**本轮没有交付可用的 Windows/Explorer 客户端或安装包。** 用户明确纠正为原生资源管理器入口后，已停止独立 WinUI App 交付，采用协调批准的 DefView+进程外AssetHost方向。所有生产Shell与 M0-002-G1..G4 保持未通过，不宣告V0.3完成。

## 可审查成果

- C#14/.NET10只读传输组件：生成SDK envelope/uint64、单HTTPS源、精确Origin、进程内Cookie/CSRF、默认系统TLS或显式叶证书SHA256（同时验主机名/有效期）、拒绝重定向、取消/8秒超时/1MiB响应边界。
- 核心授权查询适配：资源库、服务端完整范围排序/筛选、物理目录分页、范围搜索、关联一致的文件信息。客户端每页100条、历史100项/游标200项；快照和旧请求代际有边界，拒权优先清空。没有复制核心权限、路径、身份、文件操作逻辑。
- test-only C++17 DefView/IShellFolder2/IPersistFolder2 两层样例、独立 COM/PIDL/属性/Folder关联/DefView 探针、当前用户单一CLSID注册与碰撞拒绝/有界通知/卸载验证。无网络、Provider、资产扫描或修改。
- 22个标准.NET用例及1个真实HTTPS Core/PostgreSQL用例通过；C++ `/W4 /WX /analyze` 零警告构建、隔离探针和注册清理通过。依赖审计无报告漏洞，15个锁定包均沿用既有根级测试依赖并通过完整性/许可证政策。

## 真实 Explorer 入口证据

同用户、同会话、非提升进程；HKCU DWORD Folder属性0xA8040000，Desktop读取为0xA8000000，含Folder/Browsable/HasSubfolder。独立CoCreate、SHParse、Desktop枚举、Folder open association、DefView创建和子目录bind均成功。

真实Explorer的两种CLSID入口、PIDL选择API以及任务新建窗口上的IWebBrowser2.Navigate2均未证明加载本类，显示“无关联应用”。Navigate2首次optional空指针的RPC错误已修正后重新观察；正确参数仍进入同一错误模态并阻塞，由外层有界终止自有probe。明确区分 cidl=0 只打开父目录，不计为内嵌成功。加入Desktop父PIDL UPDATEDIR与受控有界trace后，日志仍只包含独立Probe调用，没有Explorer class factory记录。原因未定位，不能把缺失可选接口或某项系统策略断言为根因。

UIA状态读取可用，Computer Use输入返回GetCursorPos 0x80070005，截图返回CreateForMonitor 0x80070057；协调线程已向用户询问桌面连接状态。未绕过权限、改变UAC/HKLM、重启Explorer或关闭用户原有窗口。注册表最终ClassPresent=false/NamespacePresent=false。任务产生的错误对话框/测试Desktop窗口仍需在输入恢复后由协调线程按所有权关闭；窗口清单已发送协调者。

## 架构、风险与后续

Core只引用生成AssetLink SDK及BCL；测试使用现有MSTest。原生Shell生产目录未修改。曾开始的新WinUI草稿完整保存在自有`.runtime/superseded-winui-draft`，未进入diff，也未删除基线文件。没有共享契约、数据库、根版本/锁或NAS部署变更。

下一步先在可交互桌面关闭自有诊断窗口，再定位真实Explorer入口；实际进入DefView后补齐子项默认Folder命令、完整parsing-name round-trip、进程外有界只读投影IPC和真实用户闭环。当前proof不能直接作为生产协议。随后才验证Host故障/取消/重连、20轮循环和8小时Explorer soak。不得把原生列表当前页的本地排序当成服务端完整范围排序。

建议在协调ADR0018/共享门禁之后合并此partial证据与组件，保持生产Shell关闭。不存在可供用户试用的Windows应用包；C#Core DLL为54,272字节，test DLL为25,088字节，仅组件/实验体积，不是发行包大小。
构建冻结补充：765235743ad2eac64836c9e09b8c169625b01387显式固定test proof Windows SDK10.0.26100.0；重新configure选中值与既有实测一致。
