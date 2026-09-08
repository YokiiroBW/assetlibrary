# V03-005 共享变更裁决

## 图片接口与依赖

38aedca 冻结 `image-preview-v1.md` / ADR-0019。PNG二进制端点、512/1600规格、2/12MiB上限、15/20秒独立时限及失败状态为唯一消费者契约。SkiaSharp和Linux.NoDependencies精确4.151.2限独立decoder；native notices、漏洞、实际发布大小和OS隔离仍须验证。

V03-007获准单写PreviewProvider.Contracts中的ImagePreviewSource(LibraryId, AssetObservation)、IImagePreviewQuery与lease接口，避免PreviewProvider反向依赖GatewayAuth造成模块循环。GatewayAuth应用编排复用既有授权查询后调用该公共端口。Core只编译链接私有BCL协议源，不能引用整个Skia worker程序集。

## Web 产物预算

批准将JavaScript/CSS/合计预算调整为327680/32768/360448 bytes。V01-024不可变构建证据已记录既有JS290457、CSS25942，超过原262144/16384/286720门槛；V03-008实际新产物为301917/27768/329685，增量11460/1826，总约4.2%，没有新增npm依赖。修正预算体现已交付工作区与新有界预览的真实规模，不跳过产物检查，不改源复杂度、请求期限或安全检查。

新预算留有限余量；V03-008需合入后运行原 `validate_web_dependencies.py --require-build-artifacts` 证明通过。没有把修改阈值本身记为浏览器行为或性能验收。

## 联调样例

root独占共享NativeClient fixture的可选图片模式；旧默认138文件保持不变。显式合成manifest经过路径/大小/hash检查后复制到私密runtime，图片放在同库的图片样例目录。实际decoder通过显式绝对worker路径传入，仍走生产相同的隔离检查，不添加测试绕过模式。
