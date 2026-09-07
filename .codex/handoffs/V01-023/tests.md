# V01-023 待统一验收记录

实现：bc3d151与68e7250，2026-09-07。没有执行任何测试、构建、formatter、lint、typecheck或运行时截图。passed=0、failed=0、skipped=0表示尚未启动检查，不是已有40项回归通过或被跳过。

## 已完成的只读工作

读取AGENTS、任务包、ADR0014/0015、docs12、现有认证→登记→扫描→浏览/搜索调用链；view_image查看既定Web设计图；检查现有dialog宽度/最大高度/滚动和aria-describedby；与V01-022确认/assets/<source_key>映射；人工审查连贯diff及文件范围。没有启动浏览器或开发服务。

## 保留的最小断言变更

现有tests/web/trial-library-scan.spec.mjs首个登记用例使用photos源+/assets/photos，检查关联帮助包含部署挂载表和Windows说明，确认原样提交；扫描成功后检查刷新不重扫提示。原Windows路径错误重试用例保留。没有新增测试、黄金快照或测试基础设施。

68e7250在已有失败/重试case补入entry_path_unsupported状态，检查反斜杠名称限制/索引未提交说明和重试按钮；其他失败提示未改。该断言尚未执行，需与根ec3f64a的Linux失败链在统一验收中对齐。

## 根统一验收入口

复用apps/web/package.json的format:check、lint、typecheck、build、test:browser，以及项目现有repository/architecture与真实NAS流程；具体执行与证据由V01-021安排。重点检查新增帮助在窄屏弹窗可读、焦点/滚动不受影响，容器路径及Windows路径均按原wire提交。此处未执行这些命令，不以源码阅读代替运行验证。

没有操作资产、NAS、证书、数据库或系统设置，没有测试进程/缓存/截图残留。工作区只产生本任务源码、任务包和交接。
