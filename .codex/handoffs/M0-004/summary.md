# M0-004 交接摘要（Windows Service guardrails correction）

## 完成状态

`partial`

## 实现提交

本 correction 从 `0faa07cc2518952f78d2fb029e777974c89df74c` 开始；实现提交与 handoff metadata 提交分开记录。

## 完成内容

`windows-service.ps1` 现在在 install 前检查固定服务名、TEMP wrapper/owner marker 与 5080 监听端口；既有或无法证明归属的对象一律拒绝。服务操作通过 `Win32_Service` 对象 readback 比对 binPath、wrapper marker 与 LocalService 账户，不解析本地化 `sc.exe` 输出。catch/finally 只清理本次调用成功创建的对象；stop/delete 使用 15 秒有界等待并最终 readback。服务不存在时 uninstall 只删除内容精确匹配的 owner marker 对应 wrapper。

新增 `test_windows_service_contract.py`，只读脚本文本，完全不导入或触发既有 publish 测试。

## 测试结果

- `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest tests/spikes/server-packaging/test_windows_service_contract.py -v` — 6 passed。
- `git diff --check` — 通过。
- 未运行 dotnet、Docker、既有 `test_spike.py` 或任何构建/发布。

## 外部门禁与风险

真实 Windows x64 上的 service install/start/health/stop/uninstall 周期仍未执行；当前 Linux 执行机没有 Windows/PowerShell。LocalService 访问用户 TEMP 与仓库 artifact 的实际权限、服务控制器状态转换和 wrapper cmd 行为仍需实机验证。Docker 与原 M0-004 其他 partial 门禁保持不变。

## 架构影响

仅修改 server-packaging test adapter 与独立静态契约测试；未修改生产目录、共享契约、任务登记、项目状态、Vault、Docker 定义或新增依赖。保留单脚本 PowerShell/sc.exe adapter 形态。
