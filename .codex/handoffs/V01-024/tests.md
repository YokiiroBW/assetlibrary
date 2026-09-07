# V01-024 统一验收计划

当前实施中，遵循先完整实现再统一验收，不逐文件build/test。冻结现有命令：pnpm --dir apps/web run format:check/lint/typecheck/build/test:browser；dotnet format/build及受影响WebGateway/ReadCore/Packaging过滤；python -I -B scripts/verify_repository.py；已有真实PG迁移/查询入口。实际命令按源码完成后影响范围选择，不新建测试框架或重跑未变成功检查。

最终核心用户流包含：首页与分类→库/目录→面包屑和浏览器前后→刷新/深链→多选/键盘/视图→排序过滤分页/定位→桌面详情与手机抽屉→库分类CAS和默认源根登记→单库扫描任务页。拒权/失效/断连不暴露旧内容。NAS更新前备份，验收保持原库/索引和会话，不自动扫描个人资产。
