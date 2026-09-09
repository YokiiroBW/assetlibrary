# 一次确认的诊断会话

一次Windows管理员确认已支持两轮完整捕获及状态查询，后续请求没有再次弹UAC。会话于2026-09-09 13:15:58Z启动，最晚13:45:58Z（北京时间21:45:58）停止接受请求；保留的控制台允许用户提前关闭。只适用于固定Explorer诊断观察器，不是通用管理员终端。

runner-validation.json保留启动前离线验证快照，其中notExecuted是当时状态；实际启动和复用见runner-status.jsonl与evidence.json。nonce、session.json和请求队列均未归档。runner与观察器源码/产物保存在本机.runtime诊断目录，本提交不修改产品代码或安装服务。

首次Return晚于采集结束；第二次最终Return处于有效捕获内，预填和自动补全不在范围。两轮只见私有control，没有目标COM/UserLoader失败事件；这不能证明未尝试加载。Explorer仍普通空目录，G1..G4保持开放。两轮ETW和子进程已清理；Windows owner清理了自有注册、空目录和测试窗口，保留原窗口及管理员控制台。本快照不提前声称管理员会话已到期退出。
