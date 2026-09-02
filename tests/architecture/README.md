# Architecture Tests

此目录保存 M0-009 冻结的自动化架构、语言预算、共享契约和残余发布门禁。快速层只使用 Python 标准库，避免为门禁引入第二套构建系统。

最低检查：

1. Domain 不引用 Infrastructure、UI、数据库或平台 SDK；
2. 模块不引用其他模块的 Infrastructure/内部实现；
3. 无循环依赖；
4. 客户端、Provider、Shell 不依赖 PostgreSQL；
5. Shell 不链接网络、数据库、媒体解码或第三方重型库；
6. 每个产品源码必须归属已登记 source root，新语言/框架必须与 `LANGUAGE_BUDGET.md` 和已批准 ADR 一致；
7. 公共契约变化后生成 SDK 与消费者契约测试一致；
8. 不允许新增无边界万能共享目录。

## 文件

- `architecture-rules.json`：层级、模块 owner、平台语言预算、禁止依赖和契约摘要规则；
- `m0-gates.json`：M0-002/004/006/008 每个 blocker 的一对一裁决和 release target；
- `ci-tiers.json`：快速、平台、定时/发布三层的真实命令与激活合同；
- `test_architecture_rules.py`：在临时仓库构造可通过与应失败夹具；
- `check_release_gates.py`：按目标 fail-closed 判断是否允许启动、启用或发布。

## 本地命令

```text
python scripts/validate_architecture_baseline.py
python -B -m unittest discover -s tests/architecture -p "test_*.py" -v
python tests/architecture/check_release_gates.py --target v0.1-start
python tests/architecture/check_release_gates.py --target v0.1-release
```

前三条应通过；在残余 gate 关闭前，最后一条应以退出码 3 拒绝发布。这一非零结果是预期的安全门禁，不是测试故障。

## 激活与限制

只有说明文件、没有产品源文件的 source root 会报告 inactive/not-applicable，不能据此声称该平台已验证。出现首个 C#、TypeScript、Kotlin 或 C++ 产品清单时，同一提交必须按 `ci-tiers.json.future_native_gate_activation` 加入编译器、类型、静态分析、漏洞、许可证和对应测试。

当前标准库扫描是早期、确定性的边界防线：它识别约定命名空间、源目录和明确依赖标记，但不假装取代 Roslyn、TypeScript 编译器、Gradle 或链接器的完整依赖图。发现误报时应缩小到清晰边界并增加正反夹具；禁止全局关闭规则。
