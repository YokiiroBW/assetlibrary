# M0-004 测试记录（Windows Service guardrails correction）

## 执行环境

Linux x86-64，Python 3.12；无 Windows/PowerShell。未触发 dotnet、Docker 或既有 publish 测试。

## 命令与结果

- `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest tests/spikes/server-packaging/test_windows_service_contract.py -v` — 6 passed。
- `git diff --check` — 通过。

## 覆盖范围

静态验证固定服务/端口/wrapper install 预检；created-this-run cleanup 守卫；owner marker、binPath 和 LocalService readback；start/stop/uninstall ownership gate；有界停止/删除等待与最终服务缺失确认；不解析本地化 `sc.exe` 输出。

## 未执行

真实 Windows Service 周期（install/start/health/stop/uninstall）未执行，原因是当前环境无 Windows/PowerShell。LocalService 访问用户 TEMP/仓库 artifact、SCM 状态转换以及实际 cmd wrapper 行为仍为外部风险。
