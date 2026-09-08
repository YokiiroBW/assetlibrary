# 2026-09-09 Explorer观测与用户停止

2026-09-08T16:44:08.3793062Z（本地09日00:44），同窗口只读WTS查询Session2=0/WTSActive，已区别于此前Disconnected。后续computer-use实际成功恢复并读取最小化的Explorer，因此这次不是仅依据权限设置推断桌面可用。

新鲜返回的原窗口为1247028。对其Ctrl+N后，前后窗口集合差分唯一确定本任务新窗口2229954，标题“此电脑 - 文件资源管理器”，并读取其界面/焦点；原窗口未改变导航。上述ID只是当时证据，后续不能作为无需重新确认所有权的句柄。

自有CLSID `{4FF8301D-2E73-4D49-9FE5-868D5F1EA302}` 通过原所有权脚本注册成功，DLL为旧强hash匹配的本地副本，目录.runtime/explorer-live/20260909-attempt1。watchdog设置120秒上限。

在正式导航前再次读取焦点时，computer-use报告用户通过physical Escape停止。立即停止全部CU及替代导航/关闭操作；终止自有watchdog（exec session16909，exit1），随后显式unregister和verify，两次均ClassPresent=false、NamespacePresent=false。目录只见被测DLL，没有据此判定任何历史加载根因。

本轮没有完成CLSID导航或真实DefView验收，不算G1通过，也不算新复现的“无关联应用”错误。任务新窗口2229954暂留，不能用其他工具绕过用户停止来关闭。root已记录并请求用户明确恢复；在回答前，所有桌面/Explorer动作暂停。独立无GUI的Windows图片测试可按后续明确授权继续。
