# V03-005 共享变更裁决

## 图片接口与依赖

38aedca 冻结 `image-preview-v1.md` / ADR-0019。PNG二进制端点、512/1600规格、2/12MiB上限、15/20秒独立时限及失败状态为唯一消费者契约。SkiaSharp和Linux.NoDependencies精确4.151.2限独立decoder；native notices、漏洞、实际发布大小和OS隔离仍须验证。

V03-007获准单写PreviewProvider.Contracts中的ImagePreviewSource(LibraryId, AssetObservation)、IImagePreviewQuery与lease接口，避免PreviewProvider反向依赖GatewayAuth造成模块循环。GatewayAuth应用编排复用既有授权查询后调用该公共端口。Core只编译链接私有BCL协议源，不能引用整个Skia worker程序集。

## Web 产物预算

批准将JavaScript/CSS/合计预算调整为327680/32768/360448 bytes。V01-024不可变构建证据已记录既有JS290457、CSS25942，超过原262144/16384/286720门槛；V03-008实际新产物为301917/27768/329685，增量11460/1826，总约4.2%，没有新增npm依赖。修正预算体现已交付工作区与新有界预览的真实规模，不跳过产物检查，不改源复杂度、请求期限或安全检查。

新预算留有限余量；V03-008需合入后运行原 `validate_web_dependencies.py --require-build-artifacts` 证明通过。没有把修改阈值本身记为浏览器行为或性能验收。

## 联调样例

root独占共享NativeClient fixture的可选图片模式；旧默认138文件保持不变。显式合成manifest经过路径/大小/hash检查后复制到私密runtime，图片放在同库的图片样例目录。实际decoder通过显式绝对worker路径传入，仍走生产相同的隔离检查，不添加测试绕过模式。

## 平台实测后的受限调整

V03-007的任务自有native COM/FLS观察器经原LPAC/Job启动器实测COM初始化E_ACCESSDENIED；微软官方文档明确LPAC的COM能力单独命名lpacCom。批准仅此SID的受控兼容性探针/实现，Windows图片启用仍须完整真实拒权/资源/取消/清理证据；零网络capability、LPAC与其他限制不变，不新增registryRead或全局策略。具体准入与验收条件同步ADR0019。

Linux6af1486在实际非root、父进程无cap/无seccomp环境中首次启动SIGABRT，尚未读取图片。安全stderr和精确runtime源码确认Console标准流会Dup描述符并触发懒初始化，与只许fd0/1/2的过滤冲突；owner44ab45c改固定描述符，未放宽dup/open。另补非root/零cap和RLIMIT_NPROC256软硬限制及最多257次线程探针。root按不可变SHA独立编译并进行下一轮实际验证，旧失败保留，不计decoder成功。

Windows信息类46在本机经size/fixed buffer和native诊断均证实不支持。准入范围追加自有token的AccessCheck正/负控与普通AppContainer/LPAC、AppSID/AAP自有文件对照；只有对照区分充分，才以同一有效权限检查替代失败的query。不得把API错误视作LPAC通过，不新增能力、不变更创建约束，具体条件同步ADR0019。该Windows子范围已在57d1578正式转交V03-006单写，V03-007聚焦Core接口。
