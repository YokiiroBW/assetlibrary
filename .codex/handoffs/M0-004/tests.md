# M0-004 测试记录（Windows Service guardrails correction）

## 执行环境

Linux x86-64，Python 3.12；无 Windows/PowerShell。未触发 dotnet、Docker 或既有 publish 测试。

## 命令与结果

- `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest tests/spikes/server-packaging/test_windows_service_contract.py -v` — 8 passed。
- `git diff --check` — 通过。

## 覆盖范围

静态验证固定服务/端口/wrapper install 预检；created-this-run cleanup 守卫；owner marker、binPath 和 LocalService readback；start/stop/uninstall ownership gate；有界停止/删除等待与最终服务缺失确认；不解析本地化 `sc.exe` 输出。

## 未执行

真实 Windows Service 周期（install/start/health/stop/uninstall）未执行，原因是当前环境无 Windows/PowerShell。LocalService 访问用户 TEMP/仓库 artifact、SCM 状态转换以及实际 cmd wrapper 行为仍为外部风险。

## 2026-09-02 correction 追加

clean replacement 的既有验证仍有效：bootstrap restore 与 linux-x64/win-x64 publish、两次 cold publish manifest 比较、Linux 行为测试 6/6、Compose 静态 config 和 `git diff --check` 已通过；Docker build/run/health 因 daemon `permission denied ... /var/run/docker.sock` 未执行；Windows Service 周期因无 Windows/PowerShell 未执行。其 artifact source commit、完整清单和 hash 证据保留在原 handoff 记录中。

本次独立测试新增覆盖：PowerShell 条件括号、wrapper 原文与 marker 双重比较、服务账户统一 readback、失败 cleanup 保留被引用文件、Data 目录仅空目录删除，以及 service 不存在时双缺失/双精确归属/部分残留三态。
